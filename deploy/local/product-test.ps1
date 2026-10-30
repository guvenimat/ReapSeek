$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$storefront = "http://127.0.0.1:3100"
$browserBase = "http://localhost:3100"
$reapDeep = "http://127.0.0.1:8001"
$tag = "p" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
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
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 6 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
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

$plain = NewClient
$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

Write-Host "`n[1] Product API"
$cats = (A GET "/catalog/categories").Json.categories
$dresses = $cats | Where-Object { $_.slug -eq "dresses" }
$brand = ((A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" })
$sa = (A POST "/sellers" @{ name = "PDP Seller A $tag"; city = "Izmir" }).Json
$sb = (A POST "/sellers" @{ name = "PDP Seller B $tag"; city = "Ankara" }).Json
$sc = (A POST "/sellers" @{ name = "PDP Seller C $tag"; city = "Bursa" }).Json
$product = (A POST "/catalog/products" @{ title = "Aurelia Silk Gown $tag"; brand_id = $brand.id; category_id = $dresses.id; description = "A flowing silk gown."; attributes = @{ Material = "Silk" }; variant_axes = @{ Color = @("Black", "White"); Size = @("M", "S") } }).Json
$v = @{}
foreach ($x in $product.variants) { $v["$($x.options.Color)/$($x.options.Size)"] = $x.id }
$null = A PUT "/catalog/offers" @{ seller_id = $sa.id; variant_id = $v["Black/S"]; price = 100; list_price = 125; stock = 3; handling_days = 1 }
$null = A PUT "/catalog/offers" @{ seller_id = $sb.id; variant_id = $v["Black/S"]; price = 120; stock = 10; handling_days = 2 }
$null = A PUT "/catalog/offers" @{ seller_id = $sb.id; variant_id = $v["Black/M"]; price = 130; stock = 0 }
$null = A PUT "/catalog/offers" @{ seller_id = $sc.id; variant_id = $v["White/S"]; price = 90; stock = 8 }
$null = A PATCH "/sellers/$($sc.id)" @{ status = "suspended" }
Check "draft product is 404" ((Send $plain GET "$api/api/v1/catalog/products/$($product.slug)").Status -eq 404)
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$p = (Send $plain GET "$api/api/v1/catalog/products/$($product.slug)").Json
Check "active product is served" ($p.id -eq $product.id -and $p.brand -eq "Velora" -and $p.attributes.Material -eq "Silk")
Check "category trail from department to leaf" ((@($p.categories | ForEach-Object { $_.slug }) -join ",") -eq "women,dresses")
Check "axes: color first, sizes in chart order" ($p.axes[0].name -eq "Color" -and (@($p.axes[1].values) -join ",") -eq "S,M")
$blackS = $p.variants | Where-Object { $_.id -eq $v["Black/S"] }
Check "offers sorted by price with the buy box first" ($blackS.offers.Count -eq 2 -and $blackS.offers[0].seller_id -eq $sa.id -and $blackS.offers[0].is_buy_box -and -not $blackS.offers[1].is_buy_box)
Check "discount and low stock are exposed, exact stock is not" ($blackS.offers[0].discount -eq 20 -and $blackS.offers[0].low_stock -eq 3 -and $null -eq $blackS.offers[1].low_stock -and -not ($blackS.offers[0].PSObject.Properties.Name -contains "stock"))
Check "out-of-stock offers are hidden" (@(($p.variants | Where-Object { $_.id -eq $v["Black/M"] }).offers).Count -eq 0)
Check "offers of suspended sellers are hidden" (@(($p.variants | Where-Object { $_.id -eq $v["White/S"] }).offers).Count -eq 0)
Check "price range and seller count" ($p.min_price -eq 100 -and $p.max_price -eq 100 -and $p.seller_count -eq 2)
Check "images carry their color" ((@($p.images | ForEach-Object { $_.color }) -join ",") -eq "Black,White")
Check "unknown slug is 404" ((Send $plain GET "$api/api/v1/catalog/products/no-such-product-$tag").Status -eq 404)
$sitemap = (Send $plain GET "$api/api/v1/catalog/sitemap").Json
Check "sitemap lists active products and categories" (@($sitemap.products | Where-Object { $_.slug -eq $product.slug }).Count -eq 1 -and $sitemap.categories -contains "dresses")

Write-Host "`n[2] Product page"
$r = Send $plain GET "$storefront/p/$($product.slug)"
Check "page renders on the server" ($r.Status -eq 200 -and $r.Text -cmatch 'data-testid="product-title">Aurelia Silk Gown' -and $r.Text -cmatch "data-variant=`"$($v["Black/S"])`"")
Check "buy box seller, price, list price and low stock" ($r.Text -cmatch "PDP Seller A $tag" -and $r.Text -cmatch '100\.00' -and $r.Text -cmatch '125\.00' -and $r.Text -cmatch 'Only <!-- -->3<!-- --> left')
Check "other sellers list" ($r.Text -cmatch 'Other sellers \(<!-- -->1<!-- -->\)' -and $r.Text -cmatch "PDP Seller B $tag")
Check "estimated delivery date is shown" ($r.Text -cmatch 'Estimated delivery')
Check "product JSON-LD with an aggregate offer" ($r.Text -cmatch '"@type":"Product"' -and $r.Text -cmatch '"lowPrice":100' -and $r.Text -cmatch '"priceCurrency":"TRY"' -and $r.Text -cmatch 'InStock')
Check "breadcrumb JSON-LD" ($r.Text -cmatch '"@type":"BreadcrumbList"' -and $r.Text -cmatch '"name":"Dresses"')
Check "canonical and Open Graph image" ($r.Text -cmatch "<link rel=`"canonical`" href=`"[^`"]*/p/$($product.slug)`"" -and $r.Text -cmatch '<meta property="og:image" content="http[^"]*/media/catalog/dress/black/')
Check "attributes table" ($r.Text -cmatch '<dt>Material</dt><dd>Silk</dd>')
$r = Send $plain GET "$storefront/p/$($product.slug)?variant=$($v["Black/M"])"
Check "requested variant is preselected and shown as out of stock" ($r.Text -cmatch "data-variant=`"$($v["Black/M"])`"" -and $r.Text -cmatch 'This option is out of stock')
Check "unknown product page is 404" ((Send $plain GET "$storefront/p/no-such-product-$tag").Status -eq 404)
$r = Send $plain GET "$storefront/sitemap.xml"
Check "sitemap.xml lists the product" ($r.Status -eq 200 -and $r.Text -cmatch "/p/$($product.slug)</loc>" -and $r.Text -cmatch "/c/dresses</loc>")
Check "robots.txt points to the sitemap" ((Send $plain GET "$storefront/robots.txt").Text -cmatch 'Sitemap: http[^\s]*/sitemap.xml')

Write-Host "`n[3] Tracking and identity in a real browser"
$readKey = $envValues.REAPDEEP_READ_KEY
if (-not $readKey -or -not $envValues.REAPDEEP_PUBLIC_KEY) {
    Write-Host "  SKIP ReapDeep is not connected (run connect-reapdeep.ps1)" -ForegroundColor Yellow
} else {
    $shopper = NewClient
    $reg = Send $shopper POST "$api/api/v1/auth/register" @{ email = "pdp.$tag@example.com"; name = "Pdp Shopper"; password = "pdp-password-1" }
    $userId = $reg.Json.user.id
    $session = ($shopper.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_session" }).Value
    $stateJs = "JSON.parse(localStorage.getItem(Object.keys(localStorage).find(k => k.startsWith('reapdeep_'))))"
    $run = Browse @(
        @{ goto = "$browserBase/search?q=aurelia+silk+$tag" },
        @{ waitFor = "document.querySelector('.product-card')" },
        @{ sleep = 1500 },
        @{ click = ".product-card" },
        @{ waitFor = "document.querySelector('[data-testid=product-view]')" },
        @{ sleep = 1500 },
        @{ name = "anonymous"; eval = $stateJs },
        @{ cookie = @{ name = "rs_session"; value = $session } },
        @{ goto = "$browserBase/p/$($product.slug)" },
        @{ waitFor = "document.querySelector('[data-testid=account-link]')" },
        @{ sleep = 2000 },
        @{ name = "identified"; eval = $stateJs },
        @{ name = "logout"; eval = "fetch('/api/v1/auth/logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }).then(r => r.status)" },
        @{ goto = "$browserBase/" },
        @{ waitFor = "document.querySelector('[data-testid=sign-in-link]')" },
        @{ sleep = 2000 },
        @{ name = "signedOut"; eval = $stateJs },
        @{ goto = "about:blank" },
        @{ sleep = 1500 }
    )
    Check "browser run finished without errors ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
    $anon = $run.results.anonymous.distinctId
    $customer = "customer-$userId"
    Check "anonymous visitor has an SDK id" ($anon -and -not $run.results.anonymous.identified)
    Check "signing in identifies the customer" ($run.results.identified.distinctId -eq $customer -and $run.results.identified.identified)
    Check "signing out resets to a new anonymous id" ($run.results.logout -eq 204 -and $run.results.signedOut.distinctId -ne $customer -and $run.results.signedOut.distinctId -ne $anon -and -not $run.results.signedOut.identified)
    Check "events go straight to ReapDeep ingestion" (@($run.requests | Where-Object { $_ -like "POST http://localhost:8000/capture*" }).Count -gt 0)

    $rd = NewClient
    function Events([string]$distinctId, [string]$event) {
        $q = "distinct_id=$([Uri]::EscapeDataString($distinctId))&limit=100" + $(if ($event) { "&event=$([Uri]::EscapeDataString($event))" } else { "" })
        @((Send $rd GET "$reapDeep/api/v1/events?$q" $null @{ Authorization = "Bearer $readKey" }).Json.events)
    }
    Check "search_performed reached ReapDeep" (WaitFor { @(Events $anon "search_performed" | Where-Object { $_.properties.query -eq "aurelia silk $tag" -and $_.properties.results -eq 1 }).Count -eq 1 } 60)
    Check "search_result_clicked with position 1" (@(Events $anon "search_result_clicked" | Where-Object { $_.columns.item_id -eq "$($product.id)" -and $_.properties.position -eq 1 }).Count -eq 1)
    Check "anonymous product_viewed with item_id, price and category" (@(Events $anon "product_viewed" | Where-Object { $_.columns.item_id -eq "$($product.id)" -and $_.properties.price -eq 100 -and $_.properties.category -eq "dresses" }).Count -ge 1)
    Check "page views are tracked" (@(Events $anon '$pageview').Count -ge 2)
    Check "identify links the anonymous id to the customer" (WaitFor { @(Events $customer '$identify' | Where-Object { $_.properties.'$anon_distinct_id' -eq $anon }).Count -eq 1 } 60)
    Check "product_viewed after sign-in belongs to the customer" (WaitFor { @(Events $customer "product_viewed" | Where-Object { $_.columns.item_id -eq "$($product.id)" }).Count -ge 1 } 60)
    Check "ReapDeep merged both ids into one person" (WaitFor {
        $person = @((Send $rd GET "$reapDeep/api/v1/persons?search=$customer" $null @{ Authorization = "Bearer $readKey" }).Json.persons)[0]
        $person -and $person.distinct_ids -contains $anon -and $person.distinct_ids -contains $customer } 60)
}

$null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" }
Check "archived product page is 404" (WaitFor { (Send $plain GET "$storefront/p/$($product.slug)").Status -eq 404 } 5)
$null = Psql "DELETE FROM products WHERE id = $($product.id); DELETE FROM offers WHERE seller_id IN ($($sa.id), $($sb.id), $($sc.id)); DELETE FROM sellers WHERE id IN ($($sa.id), $($sb.id), $($sc.id)); DELETE FROM users WHERE email = 'pdp.$tag@example.com';"

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
