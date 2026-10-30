$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$storefront = "http://127.0.0.1:3100"
$opensearch = "http://127.0.0.1:9200"
$tag = "s" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    New-Object System.Net.Http.HttpClient($handler)
}

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 6 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text }
}

function Psql([string]$sql) {
    $out = $sql | docker exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    "$out".Trim()
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 700 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

$plain = NewClient
function S([string]$query) { (Send $plain GET "$api/api/v1/search?$query").Json }
function Titles($result) { @($result.products | ForEach-Object { $_.title }) }
function Enc([string]$s) { [System.Web.HttpUtility]::UrlEncode($s) }

Write-Host "`n[1] Index"
Check "health reports search ok" ((Send $plain GET "$api/healthz").Json.search -eq "ok")
$aliases = (Send $plain GET "$opensearch/products/_alias").Json
$indexName = @($aliases.PSObject.Properties.Name)[0]
Check "alias points to exactly one index ($indexName)" (@($aliases.PSObject.Properties).Count -eq 1)
$active = [int](Psql "SELECT count(*) FROM products WHERE status = 'active'")
Check "index holds every active product ($active)" (WaitFor { (Send $plain GET "$opensearch/products/_count").Json.count -eq $active } 30)
Check "mapping carries the index version" ((Send $plain GET "$opensearch/products/_mapping").Json.$indexName.mappings._meta.version -ge 2)

Write-Host "`n[2] Relevance and language"
$dress = S "q=dress"
Check "dress: top results are dresses" (@($dress.products | Select-Object -First 6 | Where-Object { $_.title -cmatch "Dress" }).Count -eq 6)
$typo = S "q=dreses"
Check "typo 'dreses' finds the dresses" ($typo.total -gt 0 -and $typo.products[0].title -cmatch "Dress")
$typo2 = S "q=sneekers"
Check "typo 'sneekers' finds sneakers" ($typo2.total -gt 0 -and $typo2.products[0].title -cmatch "Sneakers")
$couch = S "q=couch"
Check "synonym 'couch' finds sofas" ($couch.total -gt 0 -and @($couch.products | Where-Object { $_.title -cmatch "Sofa" }).Count -gt 0)
$tee = S "q=tee"
Check "synonym 'tee' finds t-shirts and nothing like tea" ($tee.total -gt 0 -and @($tee.products | Where-Object { $_.title -cnotmatch "T-Shirt" }).Count -eq 0)
$cross = S "q=velora+linen"
Check "brand and material in one query" ($cross.total -gt 0 -and @($cross.products | Where-Object { $_.brand -ne "Velora" -or $_.title -cnotmatch "Linen" }).Count -eq 0)
$cafe = S "q=cafe"
Check "accents are folded (cafe finds Cafe)" (@($cafe.products | Where-Object { $_.title -match "Caf" }).Count -gt 0)
$none = S "q=zzqqxxyy"
Check "nonsense finds nothing" ($none.total -eq 0 -and @($none.products).Count -eq 0)

Write-Host "`n[3] Facets"
$women = S "category=women"
$leafSum = ($women.facets.categories | Where-Object { $_.value -like "women/*" } | Measure-Object count -Sum).Sum
Check "department total equals the sum of its subcategories ($($women.total))" ($women.total -eq $leafSum -and $women.facets.departments.Where({ $_.value -eq "women" })[0].selected)
$brand = $women.facets.brands[0]
$byBrand = S "category=women&brand=$(Enc $brand.value)"
Check "brand filter total matches its facet count" ($byBrand.total -eq $brand.count -and @($byBrand.products | Where-Object { $_.brand -ne $brand.value }).Count -eq 0)
$black = S "category=women&color=Black"
$navy = S "category=women&color=Navy"
$both = S "category=women&color=Black&color=Navy"
Check "two colors are OR-ed" ($both.total -ge [Math]::Max($black.total, $navy.total) -and $both.total -le $black.total + $navy.total)
$navyCount = ($women.facets.colors | Where-Object { $_.value -eq "Navy" }).count
Check "color counts ignore the color filter" ((($black.facets.colors | Where-Object { $_.value -eq "Navy" }).count) -eq $navyCount)
Check "selected color is marked" (($black.facets.colors | Where-Object { $_.value -eq "Black" }).selected)
Check "filtered color image is shown" (@($black.products | Where-Object { $_.image_url -notmatch "/black/" }).Count -eq 0)
$range = S "category=women&min_price=500&max_price=1000"
Check "price range filter" ($range.total -gt 0 -and @($range.products | Where-Object { $_.price -lt 500 -or $_.price -gt 1000 }).Count -eq 0)
Check "price stats ignore the price filter" ($range.facets.price.min -lt 500 -and $range.facets.price.max -gt 1000)
$inStock = S "in_stock=true&sort=price_asc"
Check "in-stock filter" ($inStock.total -lt (S "").total -and @($inStock.products | Where-Object { -not $_.in_stock }).Count -eq 0)

Write-Host "`n[4] Sorting and paging"
$asc = @((S "category=electronics&sort=price_asc&in_stock=true").products | ForEach-Object { [double]$_.price })
Check "price low to high" ((($asc | Sort-Object) -join ",") -eq ($asc -join ","))
$desc = @((S "category=electronics&sort=price_desc&in_stock=true").products | ForEach-Object { [double]$_.price })
Check "price high to low" ((($desc | Sort-Object -Descending) -join ",") -eq ($desc -join ","))
$disc = @((S "sort=discount").products | ForEach-Object { [int]$_.discount })
Check "biggest discount first" ((($disc | Sort-Object -Descending) -join ",") -eq ($disc -join ",") -and $disc[0] -gt 0)
$p1 = @((S "category=women&page=1").products | ForEach-Object { $_.id })
$p2 = @((S "category=women&page=2").products | ForEach-Object { $_.id })
Check "page 2 continues page 1" ($p1.Count -eq 24 -and $p2.Count -eq 24 -and @($p1 | Where-Object { $p2 -contains $_ }).Count -eq 0)
Check "page 101 is 400" ((Send $plain GET "$api/api/v1/search?page=101").Status -eq 400)
Check "unknown sort is 400" ((Send $plain GET "$api/api/v1/search?sort=cheap").Status -eq 400)

Write-Host "`n[5] Suggestions"
$sug = (Send $plain GET "$api/api/v1/search/suggest?q=vel").Json
Check "brands by prefix" (@($sug.brands | Where-Object { $_.value -eq "Velora" }).Count -eq 1)
Check "products by prefix" (@($sug.products).Count -gt 0 -and @($sug.products | Where-Object { $_.title -match "Vel" }).Count -gt 0)
$sug2 = (Send $plain GET "$api/api/v1/search/suggest?q=sneak").Json
Check "categories of the matches" (@($sug2.categories | Where-Object { $_.label -eq "Sneakers" }).Count -eq 1)
Check "blank suggest is 400" ((Send $plain GET "$api/api/v1/search/suggest?q=").Status -eq 400)

Write-Host "`n[6] Index follows the catalog"
$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
$c = "$api/api/v1/backoffice/catalog"
$cats = (Send $admin GET "$c/categories").Json.categories
$scarves = $cats | Where-Object { $_.slug -eq "handbags" }
$brandId = ((Send $admin GET "$c/brands").Json.brands | Where-Object { $_.name -eq "Velora" }).id
$seller = (Send $admin GET "$api/api/v1/backoffice/sellers").Json.sellers[0]
$product = (Send $admin POST "$c/products" @{ title = "Zephyrine Tote $tag"; brand_id = $brandId; category_id = $scarves.id; variant_axes = @{ Color = @("Black", "White") } }).Json
$white = ($product.variants | Where-Object { $_.options.Color -eq "White" }).id
$null = Send $admin PUT "$c/offers" @{ seller_id = $seller.id; variant_id = $white; price = 777; list_price = 999; stock = 4 }
Check "draft product is not searchable" (WaitFor { (S "q=zephyrine+$tag").total -eq 0 } 5)
$null = Send $admin PATCH "$c/products/$($product.id)" @{ status = "active" }
Check "published product becomes searchable" (WaitFor { (S "q=zephyrine+$tag").total -eq 1 } 20)
$hit = (S "q=zephyrine+$tag").products[0]
Check "indexed with buy box price and discount" ($hit.price -eq 777 -and $hit.list_price -eq 999 -and $hit.discount -eq 22 -and $hit.in_stock)
Check "color filter shows the white image" ((S "q=zephyrine+$tag&color=White").products[0].image_url -like "*/white/$($product.id).svg")
$null = Send $admin PUT "$c/offers" @{ seller_id = $seller.id; variant_id = $white; price = 555; stock = 4 }
Check "price change is reindexed" (WaitFor { (S "q=zephyrine+$tag").products[0].price -eq 555 } 20)
$null = Send $admin PUT "$c/offers" @{ seller_id = $seller.id; variant_id = $white; price = 555; stock = 0 }
Check "sold out product stays searchable but out of stock" (WaitFor { $p = (S "q=zephyrine+$tag").products; $p.Count -eq 1 -and -not $p[0].in_stock } 20)
$null = Send $admin PATCH "$c/products/$($product.id)" @{ status = "archived" }
Check "archived product leaves the index" (WaitFor { (S "q=zephyrine+$tag").total -eq 0 } 20)

Write-Host "`n[7] Full reindex"
$prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
docker compose run --rm migrate --reindex 2>&1 | Out-Null
$ErrorActionPreference = $prev
Check "alias moves to a new index" (WaitFor { @((Send $plain GET "$opensearch/products/_alias").Json.PSObject.Properties.Name)[0] -ne $indexName } 30)
Check "old index is deleted" (WaitFor { (Send $plain GET "$opensearch/$indexName").Status -eq 404 } 15)
Check "new index has every active product" (WaitFor { (Send $plain GET "$opensearch/products/_count").Json.count -eq [int](Psql "SELECT count(*) FROM products WHERE status = 'active'") } 30)
Check "search works after the swap" ((S "q=dress").total -eq $dress.total)

Write-Host "`n[8] Storefront"
$sf = NewClient
$r = Send $sf GET "$storefront/search?q=dress"
Check "search page renders results on the server" ($r.Status -eq 200 -and $r.Text -cmatch 'data-testid="product-grid"' -and $r.Text -cmatch [regex]::Escape($dress.products[0].title.Replace("&", "&amp;")))
Check "search page is not indexed by search engines" ($r.Text -cmatch '<meta name="robots" content="noindex')
$r = Send $sf GET "$storefront/c/dresses"
Check "category page lists products with facets" ($r.Status -eq 200 -and $r.Text -cmatch 'data-testid="facet-brand"' -and $r.Text -cmatch 'data-testid="product-grid"')
Check "category page has a canonical url" ($r.Text -cmatch '<link rel="canonical" href="[^"]*/c/dresses"')
$r = Send $sf GET "$storefront/c/women?color=Black"
Check "filtered category page offers Clear filters" ($r.Text -cmatch 'Clear filters' -and $r.Text -cmatch 'class="facet-option check selected"')
$r = Send $sf GET "$storefront/search?q=zzqqxxyy"
Check "no results message" ($r.Text -cmatch 'data-testid="no-results"')
$r = Send $sf GET "$storefront/"
Check "home shows deals and the menu from the catalog" ($r.Text -cmatch 'data-testid="shelf-deals"' -and $r.Text -cmatch 'Books &amp; Hobbies' -and $r.Text -cmatch 'href="/c/dresses"')
Check "storefront proxies suggestions" ((Send $sf GET "$storefront/api/v1/search/suggest?q=lin").Json.products.Count -gt 0)

$null = Psql "DELETE FROM products WHERE id = $($product.id);"

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
