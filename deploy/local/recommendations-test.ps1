$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$storefront = "http://127.0.0.1:3100"
$browserBase = "http://localhost:3100"
$ingest = "http://127.0.0.1:8000"
$reapDeep = "http://127.0.0.1:8001"
$tag = "r" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $handler.AllowAutoRedirect = $false
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler); Cookies = $handler.CookieContainer }
}

function Send($client, [string]$method, [string]$url, $body = $null, [hashtable]$headers = @{}) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    foreach ($h in $headers.Keys) { $req.Headers.TryAddWithoutValidation($h, $headers[$h]) | Out-Null }
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 8 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text }
}

function Redis([string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & docker.exe exec reapseek-redis redis-cli @arguments 2>&1
    $ErrorActionPreference = $prev
    "$out".Trim()
}

function ContainerQuiet([string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    & docker.exe @arguments 2>&1 | Out-Null
    $ErrorActionPreference = $prev
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 1000 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function Browse($steps) {
    $file = Join-Path $env:TEMP "reapseek-steps-$tag.json"
    [IO.File]::WriteAllText($file, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & node (Join-Path $PSScriptRoot "tools\browse.mjs") $file 2>&1
    $ErrorActionPreference = $prev
    Remove-Item $file -Force -ErrorAction SilentlyContinue
    ("$out" | ConvertFrom-Json)
}

if (-not $envValues.REAPDEEP_PUBLIC_KEY -or -not $envValues.REAPDEEP_READ_KEY) { throw "ReapDeep is not connected (run connect-reapdeep.ps1)" }

$plain = NewClient
$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }
function Reco([string]$placement, [string]$query = "", $client = $plain) { (Send $client GET "$api/api/v1/recommendations/$placement$query").Json }
function Ids($reco) { @($reco.items | ForEach-Object { $_.product.id }) }
function Item($reco, $id) { $reco.items | Where-Object { $_.product.id -eq $id } | Select-Object -First 1 }

Write-Host "`n[1] Catalog and behavior"
$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$seller = (A POST "/sellers" @{ name = "Reco Seller $tag"; city = "Izmir" }).Json
$products = @{}
$offers = @{}
foreach ($name in "A", "B", "C", "D") {
    $p = (A POST "/catalog/products" @{ title = "Reco $name Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Black") } }).Json
    $null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $p.variants[0].id; price = 200; stock = 10 }
    $null = A PATCH "/catalog/products/$($p.id)" @{ status = "active" }
    $products[$name] = $p
    $offers[$name] = $p.variants[0].id
}
$a = $products.A.id; $b = $products.B.id; $c = $products.C.id; $d = $products.D.id
Check "four products are searchable" (WaitFor { (Send $plain GET "$api/api/v1/search?q=reco+dress+$tag").Json.total -eq 4 } 15)

$now = [DateTimeOffset]::UtcNow
$batch = New-Object System.Collections.ArrayList
$n = 0
function Add-Event([string]$user, [string]$event, [long]$item, [hashtable]$extra = @{}) {
    $script:n++
    $props = @{ item_id = "$item" } + $extra
    $null = $batch.Add(@{ event = $event; distinct_id = "reco-$tag-$user"; event_id = "reco-$tag-$($script:n)"; timestamp = $now.AddSeconds(-600 + $script:n).ToString("o"); properties = $props })
}
foreach ($u in 1..3) { Add-Event "v$u" "product_viewed" $a; Add-Event "v$u" "product_viewed" $b }
foreach ($u in 1..2) { Add-Event "v$u" "product_viewed" $c }
foreach ($u in 1..2) {
    Add-Event "p$u" "purchase" $a @{ order_id = "RECO-$tag-$u"; price = 200; quantity = 1; amount = 200 }
    Add-Event "p$u" "purchase" $d @{ order_id = "RECO-$tag-$u"; price = 200; quantity = 1; amount = 200 }
}
$capture = Send $plain POST "$ingest/capture" @{ api_key = $envValues.REAPDEEP_PUBLIC_KEY; sent_at = $now.ToString("o"); batch = @($batch) }
Check "behavior events are accepted by ReapDeep ($($batch.Count))" ($capture.Status -eq 202)

Write-Host "`n[2] Product page recommendations (waits for ReapDeep to retrain, up to 4 minutes)"
$similarA = $null
$ready = WaitFor { $script:similarA = Reco "product_similar" "?product_id=$a"; (Ids $script:similarA) -contains $b -and (Ids $script:similarA) -contains $c } 240
Check "customers also viewed: B and C for A" $ready
Check "B (3 shared viewers) ranks above C (2)" ([array]::IndexOf((Ids $similarA), $b) -lt [array]::IndexOf((Ids $similarA), $c))
Check "they come from ReapDeep's co_viewed model" ((Item $similarA $b).source -eq "co_viewed" -and $similarA.model -eq "co_viewed")
Check "the product itself is never recommended" (-not ((Ids $similarA) -contains $a))
Check "the shelf is filled up to the limit from the same category" (@($similarA.items).Count -eq 8 -and @($similarA.items | Where-Object { $_.product.category_name -ne "Dresses" }).Count -eq 0)
$together = $null
Check "frequently bought together: D for A, from real orders" (WaitFor { $script:together = Reco "product_bought_together" "?product_id=$a"; (Ids $script:together) -contains $d } 120)
Check "bought together is never padded with other products" (@($together.items | Where-Object { $_.source -ne "bought_together" }).Count -eq 0)
Check "the list is cached in Redis" ([int](Redis @("TTL", "reco:co_viewed:$a")) -gt 0)

Write-Host "`n[3] Live catalog data"
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $offers.B; price = 150; stock = 10 }
Check "a price change shows at once although the list is cached" (WaitFor { $r = Reco "product_similar" "?product_id=$a"; (Item $r $b).product.price -eq 150 } 10)
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $offers.B; price = 150; stock = 0 }
Check "an out-of-stock product drops out of the list" (WaitFor { -not ((Ids (Reco "product_similar" "?product_id=$a")) -contains $b) } 10)
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $offers.B; price = 150; stock = 10 }
Check "and comes back when restocked" (WaitFor { (Ids (Reco "product_similar" "?product_id=$a")) -contains $b } 10)

Write-Host "`n[4] Cart"
$shopper = NewClient
Check "an empty cart has no recommendations" (@((Reco "cart" "" $shopper).items).Count -eq 0)
$null = Send $shopper POST "$api/api/v1/cart/items" @{ offer_id = ((A GET "/catalog/products/$a").Json.variants[0].offers[0].id); quantity = 1 }
$cartReco = Reco "cart" "" $shopper
Check "the cart suggests what was bought with its items" ((Ids $cartReco) -contains $d -and -not ((Ids $cartReco) -contains $a))

Write-Host "`n[5] Validation"
Check "unknown placement is 400" ((Send $plain GET "$api/api/v1/recommendations/sidebar").Status -eq 400)
Check "a product placement needs product_id" ((Send $plain GET "$api/api/v1/recommendations/product_similar").Status -eq 400)
Check "limit is at most 24" ((Send $plain GET "$api/api/v1/recommendations/home_popular?limit=30").Status -eq 400)

Write-Host "`n[6] Storefront"
$page = Send $plain GET "$storefront/p/$($products.A.slug)"
Check "the product page renders customers also viewed with B" ($page.Text -cmatch 'data-testid="rec-product_similar"' -and $page.Text -cmatch "/p/$($products.B.slug)")
Check "a one-item bought-together list is not shown" (-not ($page.Text -cmatch 'data-testid="rec-product_bought_together"'))
Check "the home page has popular and trending shelves" ((Send $plain GET "$storefront/").Text -cmatch 'data-testid="rec-home_popular"')

$stateJs = "JSON.parse(localStorage.getItem(Object.keys(localStorage).find(k => k.startsWith('reapdeep_'))))"
$run = Browse @(
    @{ goto = "$browserBase/p/$($products.A.slug)" },
    @{ waitFor = "document.querySelector('[data-testid=rec-product_similar] .product-card')" },
    @{ sleep = 1500 },
    @{ name = "state"; eval = $stateJs },
    @{ name = "first"; eval = "document.querySelector('[data-testid=rec-product_similar] .product-card').getAttribute('data-product')" },
    @{ click = "[data-testid=rec-product_similar] .product-card" },
    @{ waitFor = "document.querySelector('[data-testid=product-view]')" },
    @{ sleep = 1500 },
    @{ goto = "about:blank" },
    @{ sleep = 1500 }
)
Check "browser run finished without errors ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
$anon = $run.results.state.distinctId
$first = [long]$run.results.first
function Events([string]$event) {
    @((Send $plain GET "$reapDeep/api/v1/events?distinct_id=$([Uri]::EscapeDataString($anon))&event=$event&limit=50" $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events)
}
Check "recommendations_shown reached ReapDeep with the placement and items" (WaitFor {
    @(Events "recommendations_shown" | Where-Object { $_.properties.placement -eq "product_similar" -and $_.properties.source_item_id -eq "$a" -and $_.properties.count -eq 10 }).Count -ge 1 } 60)
Check "recommendation_clicked records the item, position and model" (WaitFor {
    @(Events "recommendation_clicked" | Where-Object { $_.columns.item_id -eq "$first" -and $_.properties.position -eq 1 -and $_.properties.model -eq "co_viewed" -and $_.properties.source_item_id -eq "$a" }).Count -eq 1 } 60)

Write-Host "`n[7] ReapDeep outage"
ContainerQuiet @("stop", "reapdeep-recommender")
$aged = (Redis @("GET", "reco:co_viewed:$a")) -creplace '"fetched_at":"[^"]+"', '"fetched_at":"2020-01-01T00:00:00Z"'
$agedFile = Join-Path $env:TEMP "reapseek-reco-$tag.json"
[IO.File]::WriteAllText($agedFile, $aged, (New-Object Text.UTF8Encoding($false)))
ContainerQuiet @("cp", $agedFile, "reapseek-redis:/tmp/aged.json")
ContainerQuiet @("exec", "reapseek-redis", "sh", "-c", "redis-cli -x SET reco:co_viewed:$a < /tmp/aged.json")
Remove-Item $agedFile -Force
$stale = Reco "product_similar" "?product_id=$a"
Check "an expired list is served as stale while ReapDeep is down" ($stale.served_from -eq "stale" -and (Ids $stale) -contains $b)
$null = Redis @("DEL", "reco:co_viewed:$a")
$none = Reco "product_similar" "?product_id=$a"
Check "without any list the shelf falls back to the same category" ($none.served_from -eq "none" -and @($none.items).Count -eq 8 -and @($none.items | Where-Object { $_.source -ne "fallback" -or $_.product.category_name -ne "Dresses" }).Count -eq 0)
Check "the product page still renders" ((Send $plain GET "$storefront/p/$($products.A.slug)").Status -eq 200)
ContainerQuiet @("start", "reapdeep-recommender")
Check "ReapDeep is used again once it is back" (WaitFor { (Reco "product_similar" "?product_id=$a").served_from -eq "reapdeep" } 60)

foreach ($p in $products.Values) { $null = A PATCH "/catalog/products/$($p.id)" @{ status = "archived" } }

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
