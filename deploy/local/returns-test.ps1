$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$sandbox = "http://127.0.0.1:8101"
$mailpit = "http://127.0.0.1:8025"
$reapDeep = "http://127.0.0.1:8001"
$tag = "t" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
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

function Mails([string]$query) { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$([Uri]::EscapeDataString($query))").messages_count }

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

Write-Host "`n[1] A delivered order"
$seller = (A POST "/sellers" @{ name = "Return Seller $tag"; city = "Izmir"; commission_rate = 0.15 }).Json
$null = A POST "/sellers/$($seller.id)/users" @{ email = "rseller.$tag@example.com"; name = "Return Desk"; password = "seller-pass-1" }
$me = NewClient
$null = Send $me POST "$api/api/v1/backoffice/auth/login" @{ email = "rseller.$tag@example.com"; password = "seller-pass-1" }
function S([string]$method, [string]$path, $body = $null) { Send $me $method "$api/api/v1/seller$path" $body }
$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$product = (A POST "/catalog/products" @{ title = "Return Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Blue") } }).Json
$script:productId = $product.id
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $product.variants[0].id; price = 200; stock = 10 }
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$offerId = ((A GET "/catalog/products/$($product.id)").Json.variants[0].offers | Where-Object { $_.seller_id -eq $seller.id }).id

$customer = NewClient
$null = Send $customer POST "$api/api/v1/auth/register" @{ email = "returner.$tag@example.com"; name = "Return Buyer"; password = "buyer-pass-1" }
$null = Send $customer POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 3 }
$cart = (Send $customer GET "$api/api/v1/cart").Json
$address = @{ full_name = "Return Buyer"; phone = "0532 111 22 33"; city = "Izmir"; district = "Bornova"; line = "Kazim Dirik Cd. 3"; postal_code = "35040" }
$placed = (Send $customer POST "$api/api/v1/checkout" @{ address = $address; expected_total = $cart.total }).Json
$number = $placed.order_number
$payId = $placed.payment_url.Substring($placed.payment_url.LastIndexOf("/") + 1)
$pay = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Return+Buyer&card_number=4242424242424242&expiry=12%2F30&cvc=123"
if ($pay.Status -eq 302) { $null = Send (NewClient) POST "$sandbox/pay/$payId/3ds" $null "code=123456&action=confirm" }
Check "the customer pays for three dresses" (WaitFor { (Send $customer GET "$api/api/v1/orders/$number").Json.status -eq "paid" } 20)
function Order { (Send $customer GET "$api/api/v1/orders/$number").Json }
$package = (Order).packages[0]
$line = $package.lines[0]
function Ask([int]$quantity, [string]$reason = "wrong_size", $client = $customer, [string]$comment = "Runs small") {
    Send $client POST "$api/api/v1/orders/$number/returns" @{ package_id = $package.id; items = @(@{ order_line_id = $line.id; quantity = $quantity }); reason = $reason; comment = $comment }
}
$early = Ask 1
Check "nothing can be returned before delivery" ($early.Status -eq 400 -and $early.Json.code -eq "return.not_delivered")
$null = S POST "/packages/$($package.id)/ship"
Check "the carrier delivers the package" (WaitFor { (Order).packages[0].status -eq "delivered" } 60)
$delivered = (Order).packages[0]
$deadline = [DateTime]::Parse($delivered.return_deadline).ToUniversalTime()
Check "the return window is 7 days from delivery" ($delivered.returnable -and [Math]::Abs(($deadline - [DateTime]::Parse($delivered.delivered_at).ToUniversalTime()).TotalDays - 7) -lt 0.001 -and $delivered.lines[0].returnable_quantity -eq 3)

Write-Host "`n[2] Asking for a return"
Check "at most the bought quantity" ((Ask 4).Json.code -eq "return.quantity")
Check "a known reason is required" ((Ask 1 "bored").Json.code -eq "return.reason")
$stranger = NewClient
$null = Send $stranger POST "$api/api/v1/auth/register" @{ email = "stranger.$tag@example.com"; name = "Stranger"; password = "buyer-pass-1" }
Check "another customer cannot return it (404)" ((Ask 1 "wrong_size" $stranger).Status -eq 404)
$first = Ask 2
Check "the customer asks to return two" ($first.Status -eq 201 -and $first.Json.status -eq "requested" -and $first.Json.total -eq 400 -and $first.Json.number -cmatch '^RT\d{12}$')
Check "only one is left to return" ((Order).packages[0].lines[0].returnable_quantity -eq 1 -and (Ask 2).Json.code -eq "return.quantity")
Check "the seller gets an email" (WaitFor { (Mails "subject:""$($first.Json.number)""") -ge 1 } 20)
$second = Ask 1 "damaged" $customer "Torn seam"
$list = (S GET "/returns?status=requested").Json.returns
Check "the seller sees both requests with the customer and items" (@($list | Where-Object { $_.order_number -eq $number }).Count -eq 2 -and ($list | Where-Object { $_.id -eq $first.Json.id }).lines[0].quantity -eq 2)

Write-Host "`n[3] The seller decides"
Check "rejecting needs a note" ((S POST "/returns/$($second.Json.id)/reject" @{ note = " " }).Status -eq 400)
$rejected = S POST "/returns/$($second.Json.id)/reject" @{ note = "The photos show normal wear" }
Check "the seller rejects one" ($rejected.Status -eq 200 -and $rejected.Json.status -eq "rejected")
Check "a rejected item can be asked for again" ((Order).packages[0].lines[0].returnable_quantity -eq 1)
Check "the customer is told why" (WaitFor { (Mails "subject:""$($second.Json.number) was not accepted""") -eq 1 } 20)
$approved = S POST "/returns/$($first.Json.id)/approve"
Check "the seller approves the other and the carrier gives a return code" ($approved.Status -eq 200 -and $approved.Json.status -eq "approved" -and $approved.Json.return_tracking -cmatch '^SC\d{10}$')
Check "a decision is final" ((S POST "/returns/$($first.Json.id)/reject" @{ note = "x" }).Status -eq 400)
Check "the customer gets the return code" (WaitFor { (Mails "subject:""$($first.Json.number) is approved""") -eq 1 } 20)

Write-Host "`n[4] Refund"
Check "the return reaches the seller and the card is refunded" (WaitFor { ((Order).returns | Where-Object { $_.id -eq $first.Json.id }).status -eq "refunded" } 90)
$ret = (Order).returns | Where-Object { $_.id -eq $first.Json.id }
Check "the customer sees the return's journey" ((@($ret.tracking | ForEach-Object { $_.status }) -join ",") -eq "in_transit,out_for_delivery,delivered" -and $ret.received_at)
Check "the refund went through the payment provider" ((Psql "SELECT refund_reference FROM returns WHERE id = '$($first.Json.id)'") -cmatch '^re_[0-9a-f]{24}$')
Check "the customer gets a refund email" (WaitFor { (Mails "subject:""for return $($first.Json.number)""") -eq 1 } 20)
$ledger = @((S GET "/balance").Json.entries | Where-Object { $_.order_number -eq $number })
Check "the refund comes off the seller's ledger with the commission given back" (($ledger | Where-Object { $_.kind -eq "refund" }).amount -eq -400 -and ($ledger | Where-Object { $_.kind -eq "commission_refund" }).amount -eq 60)
$summary = (S GET "/balance").Json.summary
Check "the sale is pending and the refund is due at once" ($summary.pending -eq 510 -and $summary.available -eq -340)
$null = A POST "/payouts/run"
Check "a seller who owes money is not paid" (@((A GET "/payouts").Json.payouts | Where-Object { $_.seller_id -eq $seller.id }).Count -eq 0)
$null = Psql "UPDATE seller_ledger SET available_at = now() - interval '8 days' WHERE order_number = '$number' AND return_id IS NULL"
$null = A POST "/payouts/run"
$payout = (A GET "/payouts").Json.payouts | Where-Object { $_.seller_id -eq $seller.id }
Check "the next payout is the sale minus the refund: 600 - 90 - 400 + 60 = 170" (@($payout).Count -eq 1 -and $payout.amount -eq 170)
if ($envValues.REAPDEEP_READ_KEY) {
    $customerId = (Send $customer GET "$api/api/v1/auth/me").Json.user.id
    Check "a refund event reached ReapDeep with the amount and return number" (WaitFor {
        @((Send (NewClient) GET "$reapDeep/api/v1/events?distinct_id=customer-$customerId&event=refund&limit=10" $null $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events |
            Where-Object { $_.properties.order_id -eq $number -and $_.properties.return_id -eq $first.Json.number -and [double]$_.properties.amount -eq 400 }).Count -eq 1 } 60)
}

Write-Host "`n[5] In a real browser"
function Browse($steps) {
    $file = Join-Path $env:TEMP "reapseek-returns-$tag.json"
    [IO.File]::WriteAllText($file, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & node (Join-Path $PSScriptRoot "tools\browse.mjs") $file 2>&1 | Out-String
    $ErrorActionPreference = $prev
    Remove-Item $file -Force -ErrorAction SilentlyContinue
    $out | ConvertFrom-Json
}
$session = ($customer.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_session" }).Value
$run = Browse @(
    @{ cookie = @{ name = "rs_session"; value = $session } },
    @{ goto = "http://localhost:3100/account/orders/$number" },
    @{ waitFor = "document.querySelector('[data-testid=tracking] li.current[data-step=delivered]')" },
    @{ name = "returnsShown"; eval = "document.querySelectorAll('[data-return]').length" },
    @{ click = "[data-testid=start-return]" },
    @{ waitFor = "document.querySelector('[data-testid=return-form] select')" },
    @{ eval = "(() => { const set = (el, v) => { Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(el, v); el.dispatchEvent(new Event('change', { bubbles: true })) }; const s = document.querySelectorAll('[data-testid=return-form] select'); set(s[0], '1'); set(document.querySelector('[data-testid=return-form] select[name=reason]'), 'not_as_described'); return true })()"; name = "filled" },
    @{ sleep = 300 },
    @{ click = "[data-testid=return-form] button[type=submit]" },
    @{ waitFor = "document.querySelectorAll('[data-return]').length === 3" },
    @{ name = "newStatus"; eval = "[...document.querySelectorAll('[data-return]')].at(-1).querySelector('[data-testid=return-status]').textContent" }
)
Check "the storefront shows tracking and the earlier returns ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0 -and $run.results.returnsShown -eq 2)
Check "the customer asks for a return from the order page" ($run.results.newStatus -eq "Waiting for the seller")
$third = (Order).returns | Where-Object { $_.status -eq "requested" }
$boSession = ($me.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_bo_session" }).Value
$run = Browse @(
    @{ cookie = @{ name = "rs_bo_session"; value = $boSession } },
    @{ goto = "http://localhost:3101/#/returns?status=requested" },
    @{ waitFor = "document.querySelector('[data-return=""$($third.id)""] .btn-primary')" },
    @{ click = "[data-return=""$($third.id)""] .btn-primary" },
    @{ waitFor = "!document.querySelector('[data-return=""$($third.id)""]')" }
)
Check "the seller approves it in the seller center ($($run.failure))" ($null -eq $run.failure -and ((Order).returns | Where-Object { $_.id -eq $third.id }).status -in "approved", "received", "refunded")

Write-Host "`n[6] Window and webhook safety"
$null = Psql "UPDATE order_packages SET delivered_at = delivered_at - interval '8 days' WHERE id = '$($package.id)'"
Check "after 7 days nothing can be returned" ((Ask 1).Json.code -eq "return.window_closed" -and -not (Order).packages[0].returnable)
$body = '{"id":"evt_forged","type":"shipment.delivered","created":"2026-01-01T00:00:00Z","data":{"tracking_number":"SC0000000000","reference":"x","kind":"outbound","status":"delivered","occurred_at":"2026-01-01T00:00:00Z"}}'
Check "an unsigned carrier webhook is rejected (401)" ((Send (NewClient) POST "$api/api/v1/shipping/sandbox/webhook" $body $null @{ "Sandbox-Signature" = "t=1,v1=00" }).Status -eq 401)
$tracking = (Order).packages[0].tracking_number
$eventId = Psql "SELECT event_id FROM shipment_events WHERE tracking_number = '$tracking' AND status = 'delivered'"
$replay = '{"id":"' + $eventId + '","type":"shipment.delivered","created":"2026-01-01T00:00:00Z","data":{"tracking_number":"' + $tracking + '","reference":"x","kind":"outbound","status":"delivered","occurred_at":"2026-01-01T00:00:00Z"}}'
$t = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$hmac = New-Object System.Security.Cryptography.HMACSHA256 (,[Text.Encoding]::UTF8.GetBytes($envValues.SANDBOX_WEBHOOK_SECRET))
$sig = (($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$t.$replay")) | ForEach-Object { $_.ToString("x2") }) -join "")
$before = Psql "SELECT count(*) FROM seller_ledger WHERE order_number = '$number'"
Check "a replayed carrier event is accepted once" ((Send (NewClient) POST "$api/api/v1/shipping/sandbox/webhook" $replay $null @{ "Sandbox-Signature" = "t=$t,v1=$sig" }).Status -eq 200 -and (Psql "SELECT count(*) FROM seller_ledger WHERE order_number = '$number'") -eq $before)

$null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" }

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
