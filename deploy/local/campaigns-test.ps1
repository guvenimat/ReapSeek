param([string]$ReapDeepEnv = (Join-Path $PSScriptRoot "..\..\..\ReapDeep\deploy\local\.env"))

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$sandbox = "http://127.0.0.1:8101"
$mailpit = "http://127.0.0.1:8025"
$reapDeep = "http://127.0.0.1:8001"
$tag = "c" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0
$script:productId = $null

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    if ($script:productId) { try { $null = A PATCH "/catalog/products/$($script:productId)" @{ status = "archived" } } catch {} }
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

function Send($client, [string]$method, [string]$url, $body = $null, [string]$form = $null, [hashtable]$headers = @{}) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    foreach ($h in $headers.Keys) { $req.Headers.TryAddWithoutValidation($h, $headers[$h]) | Out-Null }
    if ($form) { $req.Content = New-Object System.Net.Http.StringContent($form, [Text.Encoding]::UTF8, "application/x-www-form-urlencoded") }
    elseif ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 10 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text }
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 1000 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function Mails([string]$query) { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$([Uri]::EscapeDataString($query))") }

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }
function M([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice/marketing$path" $body }

Write-Host "`n[1] An audience in ReapDeep"
function Customer([string]$name, [string]$display) {
    $c = NewClient
    $null = Send $c POST "$api/api/v1/auth/register" @{ email = "$name.$tag@example.com"; name = $display; password = "buyer-pass-1" }
    $c | Add-Member -NotePropertyName Id -NotePropertyValue (Send $c GET "$api/api/v1/auth/me").Json.user.id
    $address = "$name.$tag@example.com"
    $null = WaitFor { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=to:$address").messages_count -ge 1 } 20
    $mailId = (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=to:$address").messages[0].ID
    $token = [regex]::Match((Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/message/$mailId").Text, "verify-email\?token=([A-Za-z0-9_-]{43})").Groups[1].Value
    $null = Send $c POST "$api/api/v1/auth/email/verify" @{ token = $token }
    $c
}
$ada = Customer "ada" "Ada Kaya"
$bora = Customer "bora" "Bora Tan"
$cem = Customer "cem" "Cem Ak"
$deniz = Customer "deniz" "Deniz Er"
$rd = @{}
foreach ($line in [IO.File]::ReadAllLines($ReapDeepEnv)) { if ($line -cmatch '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') { $rd[$matches[1]] = $matches[2].Trim() } }
$rdSession = NewClient
$null = Send $rdSession POST "$reapDeep/api/v1/auth/login" @{ email = $rd.ADMIN_EMAIL; password = $rd.ADMIN_PASSWORD }
function ReapDeepCall([string]$method, [string]$path, $body = $null) { Send $rdSession $method "$reapDeep/api/v1$path" $body $null @{ "X-Project-Id" = $envValues.REAPDEEP_PROJECT_ID } }
$cohort = (ReapDeepCall POST "/cohorts" @{ name = "Campaign test $tag"; kind = "static"; members = @("customer-$($ada.Id)", "customer-$($bora.Id)", "customer-$($cem.Id)", "anon-$tag") }).Json
Check "a static cohort is created in ReapDeep" ($cohort.id -gt 0)
Check "ReapSeek lists the ReapDeep cohorts" (WaitFor { @((M GET "/cohorts").Json.cohorts | Where-Object { $_.id -eq $cohort.id }).Count -eq 1 } 30)
$reach = $null
Check "the audience preview counts only customers with an account (3 of 4 members)" (WaitFor { $script:reach = (M POST "/audience" @{ cohort_id = $cohort.id }).Json; $script:reach.customers -eq 3 } 90)

Write-Host "`n[2] A campaign"
$campaign = @{ name = "Win back $tag"; cohort_id = $cohort.id; kind = "percent"; value = 10; min_subtotal = 200; max_discount = 50; valid_days = 14; subject = "A thank-you code $tag"; message = "We miss you. Here is 10% off." }
$over = $campaign.Clone(); $over.value = 95
Check "a percentage above 90 is refused" ((M POST "/campaigns" $over).Status -eq 400)
$bad = $campaign.Clone(); $bad.kind = "fixed"; $bad.value = 300
Check "a fixed discount larger than the minimum basket is refused" ((M POST "/campaigns" $bad).Status -eq 400)
$unknown = $campaign.Clone(); $unknown.cohort_id = 999999
Check "an unknown cohort is refused" ((M POST "/campaigns" $unknown).Status -eq 400)
$created = (M POST "/campaigns" $campaign).Json
Check "the campaign is saved as a draft" ($created.status -eq "draft" -and $created.cohort_name -eq "Campaign test $tag")
$sent = M POST "/campaigns/$($created.id)/send"
Check "sending starts" ($sent.Status -eq 200 -and $sent.Json.status -eq "sending")
function Campaign { (M GET "/campaigns").Json.campaigns | Where-Object { $_.id -eq $created.id } }
Check "every customer in the cohort gets a coupon" (WaitFor { (Campaign).status -eq "sent" -and (Campaign).recipients -eq 3 } 60)
Check "a campaign is sent once" ((M POST "/campaigns/$($created.id)/send").Status -eq 409)
Check "each customer gets an email with their own code" (WaitFor { (Mails "subject:""A thank-you code $tag""").messages_count -eq 3 } 30)
function Codes($c) { @((Send $c GET "$api/api/v1/account/coupons").Json.coupons | Where-Object { $_.campaign -eq "Win back $tag" } | ForEach-Object { $_.code }) }
$adaCode = @(Codes $ada)[0]
$boraCode = @(Codes $bora)[0]
Check "the codes are personal and different" ($adaCode -cmatch '^RS-[A-Z2-9]{8}$' -and $boraCode -and $adaCode -ne $boraCode -and @(Codes $deniz).Count -eq 0)
$mail = Mails "subject:""A thank-you code $tag"" to:ada.$tag@example.com"
$body = (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/message/$($mail.messages[0].ID)").Text
Check "the email carries the code and the terms" ($body -match [regex]::Escape($adaCode) -and $body -match "10% off" -and $body -match "200.00 TRY or more")

Write-Host "`n[3] Using a code"
$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$seller = (A POST "/sellers" @{ name = "Coupon Seller $tag"; city = "Izmir" }).Json
$product = (A POST "/catalog/products" @{ title = "Coupon Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Green") } }).Json
$script:productId = $product.id
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $product.variants[0].id; price = 150; stock = 20 }
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$offerId = ((A GET "/catalog/products/$($product.id)").Json.variants[0].offers | Where-Object { $_.seller_id -eq $seller.id }).id
function Preview($c, [string]$code) { Send $c GET "$api/api/v1/checkout/coupon?code=$code" }
$null = Send $ada POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
Check "the basket must reach the minimum" ((Preview $ada $adaCode).Json.code -eq "coupon.min_subtotal")
$null = Send $ada POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
$p = (Preview $ada $adaCode.ToLower()).Json
Check "10% off 300 is 30, codes are case-insensitive: 300 + 39.90 shipping - 30 = 309.90" ($p.discount -eq 30 -and $p.total -eq 309.90)
$null = Send $deniz POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 2 }
$other = Preview $deniz $adaCode
Check "someone else cannot use the code ($($other.Status) $($other.Json.code))" ($other.Json.code -eq "coupon.not_found")
Check "an unknown code is refused" ((Preview $ada "RS-NOPE1234").Json.code -eq "coupon.not_found")
$address = @{ full_name = "Ada Kaya"; phone = "0100 000 00 01"; city = "Izmir"; district = "Konak"; line = "Ataturk Cd. 1"; postal_code = "35210" }
Check "the total must include the discount" ((Send $ada POST "$api/api/v1/checkout" @{ address = $address; expected_total = 339.90; coupon_code = $adaCode }).Json.code -eq "price_changed")
$placed = (Send $ada POST "$api/api/v1/checkout" @{ address = $address; expected_total = 309.90; coupon_code = $adaCode }).Json
Check "the order is placed for 309.90" ($placed.total -eq 309.90)
Check "a code works only once" ((Preview $ada $adaCode).Json.code -eq "coupon.used")
$payId = $placed.payment_url.Substring($placed.payment_url.LastIndexOf("/") + 1)
$pay = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Ada+Kaya&card_number=4242424242424242&expiry=12%2F30&cvc=123"
if ($pay.Status -eq 302) { $null = Send (NewClient) POST "$sandbox/pay/$payId/3ds" $null "code=123456&action=confirm" }
Check "the payment of the discounted total succeeds" (WaitFor { (Send $ada GET "$api/api/v1/orders/$($placed.order_number)").Json.status -eq "paid" } 20)
$order = (Send $ada GET "$api/api/v1/orders/$($placed.order_number)").Json
Check "the order shows the discount and the code" ($order.discount_total -eq 30 -and $order.coupon_code -eq $adaCode -and $order.total -eq 309.90)
Check "the seller is settled on the full price" ($order.packages[0].subtotal -eq 300)
Check "the used code is gone from the customer's codes" (@(Codes $ada).Count -eq 0)

$null = Send $bora POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 2 }
$boraPlaced = (Send $bora POST "$api/api/v1/checkout" @{ address = $address; expected_total = 309.90; coupon_code = $boraCode }).Json
$payId = $boraPlaced.payment_url.Substring($boraPlaced.payment_url.LastIndexOf("/") + 1)
$null = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Bora+Tan&card_number=4000000000000002&expiry=12%2F30&cvc=123"
Check "a failed payment gives the code back" (WaitFor { (Send $bora GET "$api/api/v1/orders/$($boraPlaced.order_number)").Json.status -eq "payment_failed" -and (Preview $bora $boraCode).Status -eq 200 } 30)
Check "the campaign reports one paid order and its revenue" (WaitFor { (Campaign).redeemed -eq 1 -and (Campaign).revenue -eq 309.90 } 10)
if ($envValues.REAPDEEP_READ_KEY) {
    Check "the purchase events in ReapDeep carry the code" (WaitFor {
        @((Send (NewClient) GET "$reapDeep/api/v1/events?distinct_id=customer-$($ada.Id)&event=purchase&limit=10" $null $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events |
            Where-Object { $_.properties.coupon -eq $adaCode }).Count -eq 1 } 60)
}

Write-Host "`n[4] In a real browser"
$null = Send $cem POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 2 }
$session = ($cem.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_session" }).Value
$steps = @(
    @{ cookie = @{ name = "rs_session"; value = $session } },
    @{ goto = "http://localhost:3100/checkout" },
    @{ waitFor = "document.querySelector('[data-testid=coupon-box] .coupon-chip')" },
    @{ click = "[data-testid=coupon-box] .coupon-chip" },
    @{ waitFor = "document.querySelector('[data-testid=checkout-discount]')" },
    @{ name = "discount"; eval = "document.querySelector('[data-testid=checkout-discount] dd').textContent" },
    @{ name = "total"; eval = "document.querySelector('[data-testid=checkout-total]').textContent" },
    @{ name = "pay"; eval = "document.querySelector('[data-testid=pay-button]').textContent" }
)
$file = Join-Path $env:TEMP "reapseek-campaign-$tag.json"
[IO.File]::WriteAllText($file, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
$prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$run = (& node (Join-Path $PSScriptRoot "tools\browse.mjs") $file 2>&1 | Out-String) | ConvertFrom-Json
$ErrorActionPreference = $prev
Remove-Item $file -Force -ErrorAction SilentlyContinue
Check "the browser run finished without errors ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "one click applies the customer's code at checkout ($($run.results.discount) / $($run.results.total))" ($run.results.discount -match "30\.00" -and $run.results.total -match "309\.90" -and $run.results.pay -match "309\.90")

Write-Host "`n[5] Returns after a discount"
$package = $order.packages[0]
$sellerUser = "cseller.$tag@example.com"
$null = A POST "/sellers/$($seller.id)/users" @{ email = $sellerUser; name = "Coupon Desk"; password = "seller-pass-1" }
$me = NewClient
$null = Send $me POST "$api/api/v1/backoffice/auth/login" @{ email = $sellerUser; password = "seller-pass-1" }
$null = Send $me POST "$api/api/v1/seller/packages/$($package.id)/ship"
Check "the carrier delivers the package" (WaitFor { (Send $ada GET "$api/api/v1/orders/$($placed.order_number)").Json.packages[0].status -eq "delivered" } 60)
$line = (Send $ada GET "$api/api/v1/orders/$($placed.order_number)").Json.packages[0].lines[0]
$ret = Send $ada POST "$api/api/v1/orders/$($placed.order_number)/returns" @{ package_id = $package.id; items = @(@{ order_line_id = $line.id; quantity = 1 }); reason = "changed_mind" }
$shown = ((Send $ada GET "$api/api/v1/orders/$($placed.order_number)").Json.returns | Where-Object { $_.id -eq $ret.Json.id }).total
Check "a returned item refunds what was paid for it: 150 - 10% = 135" ($ret.Status -eq 201 -and $shown -eq 135)

$null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" }
$null = ReapDeepCall DELETE "/cohorts/$($cohort.id)"

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
