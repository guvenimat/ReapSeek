$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$storefront = "http://127.0.0.1:3100"
$backoffice = "http://127.0.0.1:3101"
$reapDeep = "http://127.0.0.1:8001"
$tag = "c" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    New-Object System.Net.Http.HttpClient($handler)
}

function Send($client, [string]$method, [string]$url, $body = $null, [hashtable]$headers = @{}) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    foreach ($h in $headers.Keys) { $req.Headers.TryAddWithoutValidation($h, $headers[$h]) | Out-Null }
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 6 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{
        Status = [int]$r.StatusCode; Json = $parsed; Text = $text
        ContentType = $(if ($r.Content.Headers.ContentType) { $r.Content.Headers.ContentType.MediaType } else { "" })
        CacheControl = "$($r.Headers.CacheControl)"
    }
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

$admin = NewClient
$login = Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }
if ($login.Status -ne 200) { throw "Admin sign-in failed with $($login.Status) (rate limited? wait a minute)" }
$c = "$api/api/v1/backoffice/catalog"
$s = "$api/api/v1/backoffice/sellers"
function A([string]$method, [string]$url, $body = $null) { Send $admin $method $url $body }

Write-Host "`n[1] Access"
Check "anonymous catalog request is 401" ((Send (NewClient) GET "$c/products").Status -eq 401)
$customer = NewClient
$null = Send $customer POST "$api/api/v1/auth/register" @{ email = "shopper.$tag@example.com"; name = "Shopper"; password = "shopper-pass-1" }
Check "customer session cannot use the back office catalog" ((Send $customer GET "$c/products").Status -eq 401)
Check "anonymous sellers request is 401" ((Send (NewClient) GET $s).Status -eq 401)

Write-Host "`n[2] Seeded catalog"
$stats = (A GET "$c/stats").Json
Check "seed has 2,000+ active products ($($stats.active_products))" ($stats.active_products -ge 2000)
Check "seed has 14+ active sellers and 40+ categories" ($stats.active_sellers -ge 14 -and $stats.categories -ge 40)
$categories = (A GET "$c/categories").Json.categories
$women = $categories | Where-Object { $_.slug -eq "women" }
$dresses = $categories | Where-Object { $_.slug -eq "dresses" }
Check "category tree has departments and subcategories" ($women.depth -eq 0 -and $dresses.parent_id -eq $women.id -and $dresses.path -eq "women/dresses")
Check "department product count includes subcategories" ($women.product_count -ge $dresses.product_count -and $dresses.product_count -gt 0)
$list = (A GET "$c/products?category=women/dresses&page_size=5").Json
Check "category filter returns only dresses" ($list.total -eq $dresses.product_count -and @($list.products | Where-Object { $_.category_path -ne "women/dresses" }).Count -eq 0)
$page = (A GET "$c/products?page=3&page_size=10").Json
Check "pagination returns the requested page" ($page.page -eq 3 -and @($page.products).Count -eq 10)

Write-Host "`n[3] Categories, brands and sellers"
$r = A POST "$c/categories" @{ parent_id = $women.id; name = "Test Scarves $tag"; image_kind = "bag" }
$cat = $r.Json
Check "subcategory is created with a path" ($r.Status -eq 201 -and $cat.path -eq "women/test-scarves-$tag" -and $cat.depth -eq 1 -and $cat.image_kind -eq "bag")
Check "duplicate slug is 409" ((A POST "$c/categories" @{ parent_id = $women.id; name = "Test Scarves $tag" }).Status -eq 409)
Check "unknown image kind is 400" ((A POST "$c/categories" @{ name = "x$tag"; image_kind = "rocket" }).Status -eq 400)
$deep = (A POST "$c/categories" @{ parent_id = $cat.id; name = "Silk $tag" }).Json
Check "third level inherits the image kind" ($deep.depth -eq 2 -and $deep.image_kind -eq "bag")
Check "a fourth level is rejected" ((A POST "$c/categories" @{ parent_id = $deep.id; name = "Too deep $tag" }).Status -eq 400)
$brand = (A POST "$c/brands" @{ name = "Brand $tag" }).Json
Check "brand is created" ($brand.slug -eq "brand-$tag")
Check "duplicate brand name is 409 (case-insensitive)" ((A POST "$c/brands" @{ name = "BRAND $tag" }).Status -eq 409)
$sellerA = (A POST $s @{ name = "Seller A $tag"; city = "Izmir"; commission_rate = 0.12 }).Json
$sellerB = (A POST $s @{ name = "Seller B $tag"; city = "Ankara" }).Json
Check "sellers are created with default commission" ($sellerA.commission_rate -eq 0.12 -and $sellerB.commission_rate -eq 0.15 -and $sellerB.status -eq "active")
Check "duplicate seller is 409" ((A POST $s @{ name = "seller a $tag" }).Status -eq 409)
Check "commission above 50% is 400" ((A POST $s @{ name = "Greedy $tag"; commission_rate = 0.6 }).Status -eq 400)

Write-Host "`n[4] Products and variants"
Check "too short title is 400" ((A POST "$c/products" @{ title = "ab"; brand_id = $brand.id; category_id = $cat.id }).Status -eq 400)
Check "products cannot sit in a department" ((A POST "$c/products" @{ title = "Scarf $tag"; brand_id = $brand.id; category_id = $women.id }).Status -eq 400)
Check "unknown brand is 400" ((A POST "$c/products" @{ title = "Scarf $tag"; brand_id = 99999999; category_id = $cat.id }).Status -eq 400)
$many = @{ A = @(1..11 | ForEach-Object { "$_" }); B = @(1..10 | ForEach-Object { "$_" }) }
Check "more than 100 variants is 400" ((A POST "$c/products" @{ title = "Scarf $tag"; brand_id = $brand.id; category_id = $cat.id; variant_axes = $many }).Status -eq 400)
$r = A POST "$c/products" @{ title = "Silk Scarf $tag"; brand_id = $brand.id; category_id = $cat.id; description = "Soft silk."; attributes = @{ Material = "Silk" }; variant_axes = @{ Color = @("Black", "White"); Size = @("S", "M") } }
$product = $r.Json
Check "product is created as a draft (201)" ($r.Status -eq 201 -and $product.status -eq "draft" -and $product.slug -eq "silk-scarf-$tag-p$($product.id)")
Check "four variants from Color x Size" (@($product.variants).Count -eq 4 -and @($product.variants | Where-Object { $_.options.Color -and $_.options.Size }).Count -eq 4)
Check "SKUs are generated" ($product.variants[0].sku -eq "RS$($product.id)-01")
Check "one image per color in the category style" (@($product.images).Count -eq 2 -and $product.images[0].url -eq "/media/catalog/bag/black/$($product.id).svg" -and $product.images[1].url -like "*/white/*")
$v1 = $product.variants[0].id
$v2 = $product.variants[1].id

Write-Host "`n[5] Offers and the buy box"
Check "price 0 is 400" ((A PUT "$c/offers" @{ seller_id = $sellerA.id; variant_id = $v1; price = 0; stock = 5 }).Status -eq 400)
Check "list price below the price is 400" ((A PUT "$c/offers" @{ seller_id = $sellerA.id; variant_id = $v1; price = 100; list_price = 90; stock = 5 }).Status -eq 400)
Check "unknown variant is 404" ((A PUT "$c/offers" @{ seller_id = $sellerA.id; variant_id = 99999999; price = 100; stock = 5 }).Status -eq 404)
Check "unknown seller is 404" ((A PUT "$c/offers" @{ seller_id = 99999999; variant_id = $v1; price = 100; stock = 5 }).Status -eq 404)
$null = A PUT "$c/offers" @{ seller_id = $sellerA.id; variant_id = $v1; price = 100; list_price = 140; stock = 5; handling_days = 2 }
$p = (A PUT "$c/offers" @{ seller_id = $sellerB.id; variant_id = $v1; price = 90; stock = 5; handling_days = 1 }).Json
function BuyBox($detail) { @($detail.variants | Where-Object { $_.id -eq $v1 })[0].offers | Where-Object { $_.is_buy_box } }
Check "the cheaper offer wins the buy box" ((BuyBox $p).seller_id -eq $sellerB.id)
$p = (A PUT "$c/offers" @{ seller_id = $sellerB.id; variant_id = $v1; price = 90; stock = 0 }).Json
Check "out of stock offer loses the buy box" ((BuyBox $p).seller_id -eq $sellerA.id)
$p = (A PUT "$c/offers" @{ seller_id = $sellerB.id; variant_id = $v1; price = 90; stock = 5; status = "paused" }).Json
Check "paused offer loses the buy box" ((BuyBox $p).seller_id -eq $sellerA.id)
$p = (A PUT "$c/offers" @{ seller_id = $sellerB.id; variant_id = $v1; price = 90; stock = 5 }).Json
Check "re-saving the same seller and variant updates the offer" (@(@($p.variants | Where-Object { $_.id -eq $v1 })[0].offers).Count -eq 2 -and (BuyBox $p).seller_id -eq $sellerB.id)
Check "suspending a seller -> 200" ((A PATCH "$s/$($sellerB.id)" @{ status = "suspended" }).Json.status -eq "suspended")
Check "suspended seller loses the buy box" ((BuyBox (A GET "$c/products/$($product.id)").Json).seller_id -eq $sellerA.id)
$null = A PATCH "$s/$($sellerB.id)" @{ status = "active" }
Check "reactivated seller wins it back" ((BuyBox (A GET "$c/products/$($product.id)").Json).seller_id -eq $sellerB.id)
Check "seller list counts offers and products" (@((A GET $s).Json.sellers | Where-Object { $_.id -eq $sellerA.id -and $_.offer_count -eq 1 -and $_.product_count -eq 1 }).Count -eq 1)

Write-Host "`n[6] Editing and listing"
$p = (A PATCH "$c/products/$($product.id)" @{ title = "Silk Square Scarf $tag"; attributes = @{ Material = "Silk"; Origin = "Turkiye" } }).Json
Check "title and attributes are updated" ($p.title -eq "Silk Square Scarf $tag" -and $p.attributes.Origin -eq "Turkiye" -and $p.status -eq "draft")
Check "unknown status is 400" ((A PATCH "$c/products/$($product.id)" @{ status = "deleted" }).Status -eq 400)
Check "unknown product is 404" ((A PATCH "$c/products/99999999" @{ title = "Nope nope" }).Status -eq 404)
$found = (A GET "$c/products?q=$tag").Json
Check "search finds the product with the buy box price" ($found.total -eq 1 -and $found.products[0].price -eq 90 -and $found.products[0].seller_count -eq 2 -and $found.products[0].variant_count -eq 4)
Check "search treats % literally" ((A GET "$c/products?q=%25%25$tag").Json.total -eq 0)
Check "status filter: the product is a draft" ((A GET "$c/products?q=$tag&status=active").Json.total -eq 0 -and (A GET "$c/products?q=$tag&status=draft").Json.total -eq 1)
Check "id search finds the product" (@((A GET "$c/products?q=$($product.id)").Json.products | Where-Object { $_.id -eq $product.id }).Count -eq 1)

Write-Host "`n[7] Sync to ReapDeep"
$readKey = $envValues.REAPDEEP_READ_KEY
if (-not $readKey) {
    Write-Host "  SKIP ReapDeep is not connected (run connect-reapdeep.ps1)" -ForegroundColor Yellow
} else {
    $rd = New-Object System.Net.Http.HttpClient
    function Item([string]$id) { Send $rd GET "$reapDeep/api/v1/items/$id" $null @{ Authorization = "Bearer $readKey" } }
    Check "draft product is not in ReapDeep" ((Item $product.id).Status -eq 404)
    $null = A PATCH "$c/products/$($product.id)" @{ status = "active" }
    Check "published product reaches ReapDeep with the buy box price" (WaitFor { $i = (Item $product.id).Json.item; $i.price -eq 90 -and $i.in_stock } 30)
    $item = (Item $product.id).Json.item
    Check "ReapDeep item has category, brand, url and image" ($item.category -eq "test-scarves-$tag" -and $item.attributes.brand -eq "Brand $tag" -and $item.attributes.department -eq "women" -and $item.url -like "*/p/silk-scarf-$tag-p$($product.id)" -and $item.image_url -like "*/media/catalog/bag/black/$($product.id).svg")
    $null = A PUT "$c/offers" @{ seller_id = $sellerB.id; variant_id = $v1; price = 80; stock = 5 }
    Check "price change is synced" (WaitFor { (Item $product.id).Json.item.price -eq 80 } 30)
    $null = A PATCH "$s/$($sellerB.id)" @{ status = "suspended" }
    Check "suspending the seller re-syncs the product" (WaitFor { (Item $product.id).Json.item.price -eq 100 } 30)
    $null = A PATCH "$c/products/$($product.id)" @{ status = "archived" }
    Check "archived product is removed from ReapDeep" (WaitFor { (Item $product.id).Status -eq 404 } 30)
    Check "outbox has no failed messages" ((Psql "SELECT count(*) FROM outbox WHERE (failed_at IS NOT NULL AND discarded_at IS NULL) OR (processed_at IS NULL AND attempts > 1)") -eq "0")
}

Write-Host "`n[8] Product images"
$r = Send (NewClient) GET "$api/media/catalog/dress/navy/42.svg"
Check "API serves the SVG" ($r.Status -eq 200 -and $r.ContentType -eq "image/svg+xml" -and $r.Text -cmatch '^<svg ' -and $r.Text -cmatch '#1f3a68')
Check "SVG is cached as immutable" ($r.CacheControl -match "immutable")
Check "same URL renders the same image" ((Send (NewClient) GET "$api/media/catalog/dress/navy/42.svg").Text -eq $r.Text)
Check "storefront proxies images" ((Send (NewClient) GET "$storefront/media/catalog/phone/black/1.svg").ContentType -eq "image/svg+xml")
Check "back office proxies images" ((Send (NewClient) GET "$backoffice/media/catalog/sofa/green/1.svg").ContentType -eq "image/svg+xml")
Check "unknown kind is 404" ((Send (NewClient) GET "$api/media/catalog/rocket/navy/1.svg").Status -eq 404)
Check "unknown color is 404" ((Send (NewClient) GET "$api/media/catalog/dress/teal/1.svg").Status -eq 404)

$null = Psql "DELETE FROM products WHERE id = $($product.id); DELETE FROM offers WHERE seller_id IN ($($sellerA.id), $($sellerB.id)); DELETE FROM sellers WHERE id IN ($($sellerA.id), $($sellerB.id)); DELETE FROM brands WHERE id = $($brand.id); DELETE FROM categories WHERE id IN ($($deep.id), $($cat.id)); DELETE FROM users WHERE email = 'shopper.$tag@example.com';"

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
