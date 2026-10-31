$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$sandbox = "http://127.0.0.1:8101"
$mailpit = "http://127.0.0.1:8025"
$tag = "s" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0
$script:products = @()

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    foreach ($id in $script:products) { try { $null = A PATCH "/catalog/products/$id" @{ status = "archived" } } catch {} }
    exit 1
}

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $handler.AllowAutoRedirect = $false
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler); Cookies = $handler.CookieContainer }
}

function Send($client, [string]$method, [string]$url, $body = $null, [string]$form = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($form) { $req.Content = New-Object System.Net.Http.StringContent($form, [Text.Encoding]::UTF8, "application/x-www-form-urlencoded") }
    elseif ($null -ne $body) {
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
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    $ErrorActionPreference = $prev
    "$out".Trim()
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 700 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

Write-Host "`n[1] Seller accounts"
$seller = (A POST "/sellers" @{ name = "Panel Seller $tag"; city = "Bursa"; commission_rate = 0.12 }).Json
$sellerEmail = "seller.$tag@example.com"
$created = A POST "/sellers/$($seller.id)/users" @{ email = $sellerEmail; name = "Panel Owner"; password = "seller-pass-1" }
Check "the admin creates a login for the seller" ($created.Status -eq 201 -and $created.Json.email -eq $sellerEmail)
Check "the same email cannot be used twice" ((A POST "/sellers/$($seller.id)/users" @{ email = $sellerEmail; name = "X"; password = "seller-pass-1" }).Status -eq 409)
Check "the seller's logins are listed" (@((A GET "/sellers/$($seller.id)/users").Json.users | Where-Object { $_.email -eq $sellerEmail }).Count -eq 1)
$me = NewClient
$login = Send $me POST "$api/api/v1/backoffice/auth/login" @{ email = $sellerEmail; password = "seller-pass-1" }
Check "the seller signs in to the back office" ($login.Status -eq 200 -and $login.Json.user.role -eq "seller" -and $login.Json.user.seller_id -eq $seller.id)
function S([string]$method, [string]$path, $body = $null) { Send $me $method "$api/api/v1/seller$path" $body }
Check "the seller sees their own account" ((S GET "/account").Json.name -eq "Panel Seller $tag")
Check "a seller cannot use admin endpoints (403)" ((Send $me GET "$api/api/v1/backoffice/orders").Status -eq 403 -and (Send $me GET "$api/api/v1/backoffice/payouts").Status -eq 403)
Check "an admin is not a seller (403)" ((Send $admin GET "$api/api/v1/seller/account").Status -eq 403)
$customer = NewClient
$null = Send $customer POST "$api/api/v1/auth/register" @{ email = "buyer.$tag@example.com"; name = "Panel Buyer"; password = "buyer-pass-1" }
Check "a customer cannot sign in to the back office" ((Send (NewClient) POST "$api/api/v1/backoffice/auth/login" @{ email = "buyer.$tag@example.com"; password = "buyer-pass-1" }).Status -eq 403)

Write-Host "`n[2] Product proposals"
$dresses = (S GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (S GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$rejected = (S POST "/catalog/products" @{ title = "Proposed Gown $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Size = @("S", "M") } }).Json
$script:products += $rejected.product.id
Check "a proposal waits for review" ($rejected.product.status -eq "pending_review" -and $rejected.editable)
$proposal = (S POST "/catalog/products" @{ title = "Proposed Wrap Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Black") } }).Json
$script:products += $proposal.product.id
$variant = $proposal.product.variants[0].id
$offer = S PUT "/offers" @{ variant_id = $variant; price = 250; list_price = 300; stock = 10; handling_days = 2 }
Check "the seller prices their own proposal before approval" ($offer.Status -eq 200 -and @($offer.Json.product.variants[0].offers).Count -eq 1)
Check "the proposal is not on sale yet" ((Send (NewClient) GET "$api/api/v1/search?q=wrap+dress+$tag").Json.total -eq 0)
Check "the admin sees proposals pending review" (@((A GET "/catalog/products?status=pending_review&q=$tag").Json.products).Count -eq 2)
Check "rejecting needs a note" ((A POST "/catalog/products/$($rejected.product.id)/review" @{ decision = "reject" }).Status -eq 400)
$null = A POST "/catalog/products/$($rejected.product.id)/review" @{ decision = "reject"; note = "Please add real photos" }
$approved = A POST "/catalog/products/$($proposal.product.id)/review" @{ decision = "approve" }
Check "the admin approves a proposal" ($approved.Status -eq 200 -and $approved.Json.status -eq "active")
Check "an approved product cannot be reviewed again" ((A POST "/catalog/products/$($proposal.product.id)/review" @{ decision = "reject"; note = "x" }).Status -eq 400)
$proposals = (S GET "/proposals").Json.proposals
Check "the seller sees the outcome and the note" (($proposals | Where-Object { $_.id -eq $rejected.product.id }).status -eq "rejected" -and ($proposals | Where-Object { $_.id -eq $rejected.product.id }).review_note -eq "Please add real photos" -and ($proposals | Where-Object { $_.id -eq $proposal.product.id }).status -eq "active")
Check "the approved product goes on sale" (WaitFor { (Send (NewClient) GET "$api/api/v1/search?q=wrap+dress+$tag").Json.total -eq 1 } 15)

Write-Host "`n[3] Offers"
$other = (A POST "/catalog/products" @{ title = "Admin Draft $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Red") } }).Json
$script:products += $other.id
Check "a seller cannot offer on someone else's unpublished product (404)" ((S PUT "/offers" @{ variant_id = $other.variants[0].id; price = 100; stock = 1 }).Status -eq 404)
Check "nor see it (404)" ((S GET "/catalog/products/$($other.id)").Status -eq 404)
$offers = (S GET "/offers?q=$tag").Json
$row = $offers.offers | Where-Object { $_.variant_id -eq $variant }
Check "the seller's offers show price, stock and the buy box" ($offers.total -eq 1 -and $row.price -eq 250 -and $row.stock -eq 10 -and $row.is_buy_box -and $row.variant_label -eq "Black")
$view = (S GET "/catalog/products/$($proposal.product.id)").Json
Check "a product view shows the buy box price but only the seller's own offers" ($view.buy_box[0].price -eq 250 -and $view.buy_box[0].mine -and -not $view.editable)
Check "invalid prices are rejected" ((S PUT "/offers" @{ variant_id = $variant; price = 0; stock = 1 }).Status -eq 400)

Write-Host "`n[4] Shipping a package"
$offerId = ($view.product.variants[0].offers | Select-Object -First 1).id
$null = Send $customer POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 2 }
$cart = (Send $customer GET "$api/api/v1/cart").Json
$address = @{ full_name = "Panel Buyer"; phone = "0532 111 22 33"; city = "Bursa"; district = "Nilufer"; line = "Ataturk Cd. 5"; postal_code = "16110" }
$placed = (Send $customer POST "$api/api/v1/checkout" @{ address = $address; expected_total = $cart.total }).Json
$payId = $placed.payment_url.Substring($placed.payment_url.LastIndexOf("/") + 1)
$pay = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Panel+Buyer&card_number=4242424242424242&expiry=12%2F30&cvc=123"
if ($pay.Status -eq 302) { $null = Send (NewClient) POST "$sandbox/pay/$payId/3ds" $null "code=123456&action=confirm" }
$number = $placed.order_number
Check "the customer pays for two dresses" (WaitFor { (Send $customer GET "$api/api/v1/orders/$number").Json.status -eq "paid" } 20)
$packages = (S GET "/packages?status=preparing").Json.packages
$package = $packages | Where-Object { $_.order_number -eq $number }
Check "the paid package waits for the seller with the address and items" ($package -and $package.subtotal -eq 500 -and $package.shipping_fee -eq 0 -and $package.address.city -eq "Bursa" -and $package.lines[0].quantity -eq 2)
Check "the commission rate is fixed on the order" ($package.commission_rate -eq 0.12)
$balance = (S GET "/balance").Json.summary
Check "the balance shows it in transit: 500 - 60 commission" ($balance.in_transit -eq 440 -and $balance.available -eq 0)
$shipped = S POST "/packages/$($package.id)/ship"
Check "shipping gets a tracking number from the carrier" ($shipped.Status -eq 200 -and $shipped.Json.status -eq "shipped" -and $shipped.Json.tracking_number -cmatch '^SC\d{10}$')
Check "a package ships only once" ((S POST "/packages/$($package.id)/ship").Status -eq 400)
Check "another seller's package is not found" ((Send $me POST "$api/api/v1/seller/packages/$([Guid]::NewGuid())/ship").Status -eq 404)
Check "the customer sees the tracking number" (@((Send $customer GET "$api/api/v1/orders/$number").Json.packages | Where-Object { $_.tracking_number -eq $shipped.Json.tracking_number }).Count -eq 1)
Check "the customer gets a shipping email" (WaitFor { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=subject:%22$number%20has%20shipped%22").messages_count -eq 1 } 20)

Write-Host "`n[5] Delivery and the ledger"
Check "the carrier moves the package and delivers it (signed Sandbox webhooks)" (WaitFor { (S GET "/packages/$($package.id)").Json.status -eq "delivered" } 60)
$tracked = (Send $customer GET "$api/api/v1/orders/$number").Json.packages[0]
Check "the customer sees every tracking step" ((@($tracked.tracking | ForEach-Object { $_.status }) -join ",") -eq "in_transit,out_for_delivery,delivered" -and $tracked.shipment_status -eq "delivered")
$ledger = (S GET "/balance").Json
$entries = @($ledger.entries | Where-Object { $_.order_number -eq $number })
Check "delivery writes the sale and the commission to the ledger" ($entries.Count -eq 2 -and ($entries | Where-Object { $_.kind -eq "sale" }).amount -eq 500 -and ($entries | Where-Object { $_.kind -eq "commission" }).amount -eq -60)
Check "the money is pending for 7 days" ($ledger.summary.pending -eq 440 -and $ledger.summary.in_transit -eq 0 -and $ledger.summary.available -eq 0)
$availableAt = [DateTime]::Parse((Psql "SELECT to_char(min(available_at) AT TIME ZONE 'UTC', 'YYYY-MM-DD""T""HH24:MI:SS""Z""') FROM seller_ledger WHERE order_number = '$number'")).ToUniversalTime()
Check "it becomes available 7 days after delivery ($availableAt)" ([Math]::Abs(($availableAt - [DateTime]::UtcNow.AddDays(7)).TotalMinutes) -lt 5)
Check "the admin can no longer mark a package delivered by hand" ((Send $admin POST "$api/api/v1/backoffice/packages/$($package.id)/deliver").Status -in 404, 405)
$run = (A POST "/payouts/run").Json
Check "a payout run leaves pending money alone" (@((A GET "/payouts").Json.payouts | Where-Object { $_.seller_id -eq $seller.id }).Count -eq 0)

Write-Host "`n[6] Weekly payout"
$null = Psql "UPDATE seller_ledger SET available_at = now() - interval '8 days' WHERE order_number = '$number'"
Check "after the hold the money is available" ((S GET "/balance").Json.summary.available -eq 440)
$run = (A POST "/payouts/run").Json
$payout = (A GET "/payouts").Json.payouts | Where-Object { $_.seller_id -eq $seller.id }
Check "the payout run pays the seller's available balance" (@($payout).Count -eq 1 -and $payout.amount -eq 440 -and $payout.entry_count -eq 2)
Check "the money is sent through the Sandbox bank" ($payout.transfer_reference -cmatch '^tr_[0-9a-f]{24}$')
$periodEnd = [DateTime]::Parse($payout.period_end).ToUniversalTime()
$local = $periodEnd.AddHours(3)
Check "the period ends on Monday at midnight Turkey time ($local)" ($local.DayOfWeek -eq [DayOfWeek]::Monday -and $local.TimeOfDay -eq [TimeSpan]::Zero)
$after = (S GET "/balance").Json
Check "the seller sees the payout and a zero balance" ($after.summary.available -eq 0 -and $after.summary.paid_out -eq 440 -and @($after.payouts).Count -eq 1 -and @($after.entries | Where-Object { $_.order_number -eq $number -and $_.payout_id -eq $payout.id }).Count -eq 2)
$null = A POST "/payouts/run"
Check "running again does not pay twice" (@((A GET "/payouts").Json.payouts | Where-Object { $_.seller_id -eq $seller.id }).Count -eq 1)

Write-Host "`n[7] Seller center in a real browser"
$null = Send $customer POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
$cart = (Send $customer GET "$api/api/v1/cart").Json
$placed = (Send $customer POST "$api/api/v1/checkout" @{ address = $address; expected_total = $cart.total }).Json
$payId = $placed.payment_url.Substring($placed.payment_url.LastIndexOf("/") + 1)
$pay = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Panel+Buyer&card_number=4242424242424242&expiry=12%2F30&cvc=123"
if ($pay.Status -eq 302) { $null = Send (NewClient) POST "$sandbox/pay/$payId/3ds" $null "code=123456&action=confirm" }
$null = WaitFor { (Send $customer GET "$api/api/v1/orders/$($placed.order_number)").Json.status -eq "paid" } 20
$second = (S GET "/packages?status=preparing").Json.packages | Where-Object { $_.order_number -eq $placed.order_number }
$session = ($me.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_bo_session" }).Value
$steps = @(
    @{ cookie = @{ name = "rs_bo_session"; value = $session } },
    @{ goto = "http://localhost:3101/#/packages?status=preparing" },
    @{ waitFor = "document.querySelector('[data-package=""$($second.id)""] .btn-primary')" },
    @{ name = "nav"; eval = "[...document.querySelectorAll('nav .nav-item')].map(a => a.textContent.trim()).join(',')" },
    @{ click = "[data-package=""$($second.id)""] .btn-primary" },
    @{ waitFor = "!document.querySelector('[data-package=""$($second.id)""]')" },
    @{ goto = "http://localhost:3101/#/" },
    @{ waitFor = "document.querySelector('[data-testid=stat-available]')" },
    @{ name = "available"; eval = "document.querySelector('[data-testid=stat-available] strong').textContent" }
)
$stepsFile = Join-Path $env:TEMP "reapseek-seller-$tag.json"
[IO.File]::WriteAllText($stepsFile, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
$prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$run = (& node (Join-Path $PSScriptRoot "tools\browse.mjs") $stepsFile 2>&1 | Out-String) | ConvertFrom-Json
$ErrorActionPreference = $prev
Remove-Item $stepsFile -Force -ErrorAction SilentlyContinue
Check "the browser run finished without errors ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "a seller sees only the seller center menu" ($run.results.nav -eq "Earnings,Packages,Returns,Offers,Catalog,Proposals")
Check "the Ship button ships the package" ((S GET "/packages/$($second.id)").Json.status -in "shipped", "delivered")
Check "the earnings page shows the balance ($($run.results.available))" ($run.results.available -cmatch "(^|[^0-9.,])0\.00$")

foreach ($id in $script:products) { $null = A PATCH "/catalog/products/$id" @{ status = "archived" } }
Check "an archived product never shows as winning the buy box" (@((S GET "/offers?q=$tag").Json.offers | Where-Object { $_.is_buy_box -or $null -ne $_.buy_box_price }).Count -eq 0)

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
