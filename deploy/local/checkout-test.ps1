$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    if ($product) { try { $null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" } } catch {} }
    DockerQuiet @("start", "reapseek-sandbox")
    exit 1
}
$api = "http://127.0.0.1:8100"
$sandbox = "http://127.0.0.1:8101"
$storefront = "http://127.0.0.1:3100"
$browserBase = "http://localhost:3100"
$reapDeep = "http://127.0.0.1:8001"
$mailpit = "http://127.0.0.1:8025"
$tag = "o" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
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

function Send($client, [string]$method, [string]$url, $body = $null, [hashtable]$headers = @{}, [string]$form = $null) {
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
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text; Location = $(if ($r.Headers.Location) { $r.Headers.Location.ToString() } else { $null }) }
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

function CookieValue($client, [string]$url, [string]$name) {
    $c = $client.Cookies.GetCookies([Uri]$url) | Where-Object { $_.Name -ceq $name }
    if ($c) { $c.Value } else { $null }
}

function DockerQuiet([string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    & docker.exe @arguments 2>&1 | Out-Null
    $ErrorActionPreference = $prev
}

$address = @{ full_name = "Test Buyer"; phone = "0532 111 22 33"; city = "Izmir"; district = "Konak"; line = "Cumhuriyet Blv. 10"; postal_code = "35210" }

function Register([string]$name) {
    $c = NewClient
    $null = Send $c POST "$api/api/v1/auth/register" @{ email = "$name.$tag@example.com"; name = "Test Buyer"; password = "buyer-pass-1" }
    $c
}

function PlaceOrder($client) {
    $cart = (Send $client GET "$api/api/v1/cart").Json
    Send $client POST "$api/api/v1/checkout" @{ address = $address; expected_total = $cart.total }
}

function PayWith([string]$paymentUrl, [string]$card, [string]$code = "123456") {
    $id = $paymentUrl.Substring($paymentUrl.LastIndexOf("/") + 1)
    $s = NewClient
    $r = Send $s POST "$sandbox/pay/$id" $null @{} "holder=Test+Buyer&card_number=$card&expiry=12%2F30&cvc=123"
    if ($r.Status -eq 302) { $r = Send $s POST "$sandbox/pay/$id/3ds" $null @{} "code=$code&action=confirm" }
    [pscustomobject]@{ Id = $id; Response = $r }
}

function Order($client, [string]$number) { (Send $client GET "$api/api/v1/orders/$number").Json }
function Stock([long]$offerId) { [int](Psql "SELECT stock FROM offers WHERE id = $offerId") }

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

$cats = (A GET "/catalog/categories").Json.categories
$dresses = $cats | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$sellerA = (A POST "/sellers" @{ name = "Checkout Seller A $tag"; city = "Izmir" }).Json
$sellerB = (A POST "/sellers" @{ name = "Checkout Seller B $tag"; city = "Ankara" }).Json
$product = (A POST "/catalog/products" @{ title = "Checkout Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Black", "White") } }).Json
$black = ($product.variants | Where-Object { $_.options.Color -eq "Black" }).id
$white = ($product.variants | Where-Object { $_.options.Color -eq "White" }).id
$null = A PUT "/catalog/offers" @{ seller_id = $sellerA.id; variant_id = $black; price = 120; stock = 20 }
$null = A PUT "/catalog/offers" @{ seller_id = $sellerB.id; variant_id = $white; price = 600; stock = 20 }
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$p = (A GET "/catalog/products/$($product.id)").Json
$offerA = ($p.variants | Where-Object { $_.id -eq $black }).offers[0].id
$offerB = ($p.variants | Where-Object { $_.id -eq $white }).offers[0].id

Write-Host "`n[1] Cart"
$guest = NewClient
$r = Send $guest POST "$api/api/v1/cart/items" @{ offer_id = $offerA; quantity = 2 }
Check "guest adds an item (200) and gets a cart cookie" ($r.Status -eq 200 -and $r.Json.item_count -eq 2 -and (CookieValue $guest $api "rs_cart"))
Check "cart groups by seller and charges shipping under 500" ($r.Json.packages.Count -eq 1 -and $r.Json.packages[0].shipping_fee -eq 39.9 -and $r.Json.total -eq 279.9)
$null = Send $guest POST "$api/api/v1/cart/items" @{ offer_id = $offerB; quantity = 1 }
$cart = (Send $guest GET "$api/api/v1/cart").Json
Check "second seller becomes a second package with free shipping" ($cart.packages.Count -eq 2 -and ($cart.packages | Where-Object { $_.seller_id -eq $sellerB.id }).shipping_fee -eq 0 -and $cart.total -eq 879.9)
$r = Send $guest POST "$api/api/v1/cart/items" @{ offer_id = $offerA; quantity = 9 }
Check "more than 10 of one item is 400 (cart.max_quantity)" ($r.Status -eq 400 -and $r.Json.code -eq "cart.max_quantity")
$null = Psql "UPDATE offers SET stock = 3 WHERE id = $offerA"
Check "more than the seller has in stock is 409" ((Send $guest POST "$api/api/v1/cart/items" @{ offer_id = $offerA; quantity = 2 }).Status -eq 409)
$null = Psql "UPDATE offers SET stock = 20 WHERE id = $offerA"
Check "quantity 11 is 400" ((Send $guest PATCH "$api/api/v1/cart/items/$offerA" @{ quantity = 11 }).Status -eq 400)
Check "unknown offer is 404" ((Send $guest POST "$api/api/v1/cart/items" @{ offer_id = 999999999; quantity = 1 }).Status -eq 404)
$r = Send $guest PATCH "$api/api/v1/cart/items/$offerA" @{ quantity = 3 }
Check "quantity can be changed" ($r.Status -eq 200 -and ($r.Json.packages.lines | Where-Object { $_.offer_id -eq $offerA }).quantity -eq 3)
$null = A PUT "/catalog/offers" @{ seller_id = $sellerA.id; variant_id = $black; price = 110; stock = 20 }
$line = ((Send $guest GET "$api/api/v1/cart").Json.packages.lines | Where-Object { $_.offer_id -eq $offerA })
Check "price change is flagged with the new price" ($line.status -eq "price_changed" -and $line.unit_price -eq 110 -and $line.added_price -eq 120)
$null = A PUT "/catalog/offers" @{ seller_id = $sellerA.id; variant_id = $black; price = 120; stock = 20 }
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "draft" }
Check "unpublished product makes the line unavailable" (((Send $guest GET "$api/api/v1/cart").Json.packages.lines | Where-Object { $_.offer_id -eq $offerA }).status -eq "unavailable")
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
Check "removing a missing line is 404" ((Send $guest DELETE "$api/api/v1/cart/items/123456789").Status -eq 404)
$r = Send $guest GET "$storefront/cart"
Check "cart page renders the items" ($r.Status -eq 200 -and $r.Text -cmatch 'data-testid="cart"' -and $r.Text -cmatch "Checkout Seller A $tag" -and $r.Text -cmatch 'Sign in to check out')
$r = Send $guest POST "$api/api/v1/auth/register" @{ email = "merge.$tag@example.com"; name = "Merge Buyer"; password = "buyer-pass-1" }
Check "signing up merges the guest cart into the customer cart" ($r.Status -eq 201 -and (Send $guest GET "$api/api/v1/cart/summary").Json.item_count -eq 4)
Check "guest cart cookie is cleared after the merge" (-not (CookieValue $guest $api "rs_cart"))
Check "header shows the cart count" ((Send $guest GET "$storefront/").Text -cmatch 'data-testid="cart-count">4<')

Write-Host "`n[2] Checkout rules"
Check "anonymous checkout is 401" ((Send (NewClient) POST "$api/api/v1/checkout" @{ address = $address; expected_total = 1 }).Status -eq 401)
$empty = Register "empty"
$r = Send $empty POST "$api/api/v1/checkout" @{ address = $address; expected_total = 0 }
Check "empty cart is 409 cart_empty" ($r.Status -eq 409 -and $r.Json.code -eq "cart_empty")
$bad = $address.Clone(); $bad.phone = "123"
$r = Send $guest POST "$api/api/v1/checkout" @{ address = $bad; expected_total = 879.9 }
Check "invalid phone is 400 with a domain code" ($r.Status -eq 400 -and $r.Json.code -eq "address.phone")
$r = Send $guest POST "$api/api/v1/checkout" @{ address = $address; expected_total = 100 }
Check "a different expected total is 409 price_changed" ($r.Status -eq 409 -and $r.Json.code -eq "price_changed")
$null = Psql "UPDATE offers SET stock = 2 WHERE id = $offerA"
$r = PlaceOrder $guest
Check "low stock blocks checkout (409)" ($r.Status -eq 409 -and $r.Json.code -eq "cart_needs_attention")
$null = Psql "UPDATE offers SET stock = 20 WHERE id = $offerA"

Write-Host "`n[3] Successful payment"
$stockA = Stock $offerA; $stockB = Stock $offerB
$r = PlaceOrder $guest
$number = $r.Json.order_number
Check "order is placed (201) with a payment link" ($r.Status -eq 201 -and $number -cmatch '^RS\d{12}$' -and $r.Json.payment_url -like "http://localhost:8101/pay/pay_*" -and $r.Json.total -eq 999.9)
Check "stock is reserved" ((Stock $offerA) -eq $stockA - 3 -and (Stock $offerB) -eq $stockB - 1)
$o = Order $guest $number
Check "order awaits payment with two seller packages" ($o.status -eq "pending_payment" -and $o.packages.Count -eq 2 -and @($o.packages | Where-Object { $_.status -eq "awaiting_payment" }).Count -eq 2)
$hosted = Send (NewClient) GET $r.Json.payment_url.Replace("http://localhost:8101", $sandbox)
Check "hosted payment page shows the amount" ($hosted.Status -eq 200 -and $hosted.Text -cmatch '999\.90 TRY' -and $hosted.Text -cmatch 'data-testid="card-form"')
$id = $r.Json.payment_url.Substring($r.Json.payment_url.LastIndexOf("/") + 1)
$invalid = Send (NewClient) POST "$sandbox/pay/$id" $null @{} "holder=Test&card_number=4242424242424241&expiry=12%2F30&cvc=123"
Check "an invalid card number stays on the form (400)" ($invalid.Status -eq 400 -and $invalid.Text -cmatch 'card number is not valid')
$pay = PayWith $r.Json.payment_url "4242424242424242"
Check "3-D Secure returns to the storefront" ($pay.Response.Status -eq 302 -and $pay.Response.Location -like "http://localhost:3100/checkout/complete?order=$number*")
Check "webhook marks the order paid" (WaitFor { (Order $guest $number).status -eq "paid" } 20)
$o = Order $guest $number
Check "packages move to preparing and the payment succeeded" (@($o.packages | Where-Object { $_.status -eq "preparing" }).Count -eq 2 -and $o.payments[0].status -eq "succeeded" -and $o.paid_at)
Check "purchased items leave the cart" ((Send $guest GET "$api/api/v1/cart/summary").Json.item_count -eq 0)
Check "stock stays reduced after payment" ((Stock $offerA) -eq $stockA - 3)
Check "confirmation email is sent" (WaitFor { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=subject:$number").messages_count -eq 1 } 20)
Check "order_paid was processed by the worker" (WaitFor { (Psql "SELECT count(*) FROM outbox WHERE type = 'order_paid' AND payload->>'number' = '$number' AND processed_at IS NOT NULL") -eq "1" } 20)
$sk = $envValues.SANDBOX_SECRET_KEY
$eventId = Psql "SELECT event_id FROM payment_events WHERE provider_payment_id = '$($pay.Id)'"
$null = Send (NewClient) POST "$sandbox/v1/payments/$($pay.Id)/webhooks/resend?event_id=$eventId" $null @{ Authorization = "Bearer $sk" }
$null = Send (NewClient) POST "$sandbox/v1/payments/$($pay.Id)/webhooks/resend" $null @{ Authorization = "Bearer $sk" }
Start-Sleep -Seconds 1
Check "repeated webhooks are idempotent" ((Order $guest $number).status -eq "paid" -and (Psql "SELECT count(*) FROM outbox WHERE type = 'order_paid' AND payload->>'number' = '$number'") -eq "1" -and (Psql "SELECT count(*) FROM payment_events WHERE provider_payment_id = '$($pay.Id)'") -eq "2")
$forged = Send (NewClient) POST "$api/api/v1/payments/sandbox/webhook" '{"id":"evt_forged","type":"payment.succeeded","created":"2026-10-30T00:00:00Z","data":{"payment_id":"x","reference":"x","amount":1,"currency":"TRY","status":"succeeded"}}' @{ "Sandbox-Signature" = "t=1,v1=00" }
Check "forged webhook is 401" ($forged.Status -eq 401)
Check "orders list shows the order" (@((Send $guest GET "$api/api/v1/orders").Json.orders | Where-Object { $_.number -eq $number -and $_.status -eq "paid" -and $_.item_count -eq 4 }).Count -eq 1)
Check "another customer cannot see the order (404)" ((Send $empty GET "$api/api/v1/orders/$number").Status -eq 404)
$bo = (A GET "/orders?q=$number").Json
Check "back office lists the order" ($bo.total -eq 1 -and $bo.orders[0].customer_email -eq "merge.$tag@example.com")
Check "back office order detail shows packages and payments" ((A GET "/orders/$number").Json.payments.Count -eq 1)
$page = Send $guest GET "$storefront/account/orders/$number"
Check "order page renders" ($page.Status -eq 200 -and $page.Text -cmatch 'data-testid="order-status">Paid<')
if ($envValues.REAPDEEP_READ_KEY) {
    $rd = NewClient
    $customerId = (Send $guest GET "$api/api/v1/auth/me").Json.user.id
    Check "purchase events reached ReapDeep with the order id" (WaitFor {
        $events = @((Send $rd GET "$reapDeep/api/v1/events?distinct_id=customer-$customerId&event=purchase&limit=10" $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events)
        $events.Count -eq 2 -and @($events | Where-Object { $_.properties.order_id -eq $number }).Count -eq 2 -and ($events | ForEach-Object { [double]$_.properties.amount } | Measure-Object -Sum).Sum -eq 960 } 60)
} else {
    Write-Host "  SKIP ReapDeep is not connected" -ForegroundColor Yellow
}

Write-Host "`n[4] Failed payments"
$buyer = Register "declined"
$null = Send $buyer POST "$api/api/v1/cart/items" @{ offer_id = $offerA; quantity = 1 }
$before = Stock $offerA
$r = PlaceOrder $buyer
$pay = PayWith $r.Json.payment_url "4000000000000002"
Check "declined card shows the failure page" ($pay.Response.Status -eq 200 -and $pay.Response.Text -cmatch 'Your card was declined')
Check "order becomes payment_failed" (WaitFor { (Order $buyer $r.Json.order_number).status -eq "payment_failed" } 20)
Check "stock is released and the cart keeps the item" ((Stock $offerA) -eq $before -and (Send $buyer GET "$api/api/v1/cart/summary").Json.item_count -eq 1)
Check "failure reason is recorded" ((Order $buyer $r.Json.order_number).close_reason -eq "card_declined")
$r = PlaceOrder $buyer
$pay = PayWith $r.Json.payment_url "4242424242424242" "000000"
Check "wrong 3-D Secure code fails the payment" (WaitFor { $x = Order $buyer $r.Json.order_number; $x.status -eq "payment_failed" -and $x.close_reason -eq "authentication_failed" } 20)

Write-Host "`n[5] Payment timeout"
$before = Stock $offerA
$r = PlaceOrder $buyer
$null = Psql "UPDATE orders SET placed_at = placed_at - interval '20 minutes' WHERE number = '$($r.Json.order_number)'"
Check "unpaid order is cancelled after the payment window" (WaitFor { $x = Order $buyer $r.Json.order_number; $x.status -eq "cancelled" -and $x.close_reason -eq "payment_timeout" } 30)
Check "stock is released" ((Stock $offerA) -eq $before)

Write-Host "`n[6] Payment provider outage"
DockerQuiet @("stop", "reapseek-sandbox")
$before = Stock $offerA
$r = PlaceOrder $buyer
Check "checkout fails with 503 payment_unavailable" ($r.Status -eq 503 -and $r.Json.code -eq "payment_unavailable")
Check "the reserved stock is given back" ((Stock $offerA) -eq $before)
Check "the order is closed, not left pending" ((Psql "SELECT status || '/' || close_reason FROM orders WHERE customer_id = (SELECT id FROM users WHERE email = 'declined.$tag@example.com') ORDER BY placed_at DESC LIMIT 1") -eq "payment_failed/payment_provider_unavailable")
DockerQuiet @("start", "reapseek-sandbox")
$null = WaitFor { try { (Send (NewClient) GET "$sandbox/healthz").Status -eq 200 } catch { $false } } 30

Write-Host "`n[7] Shopping in a real browser"
$shopper = Register "browser"
$session = CookieValue $shopper $api "rs_session"
$prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$stepsFile = Join-Path $env:TEMP "reapseek-checkout-$tag.json"
$steps = @(
    @{ cookie = @{ name = "rs_session"; value = $session } },
    @{ goto = "$browserBase/p/$($product.slug)" },
    @{ waitFor = "document.querySelector('[data-testid=add-to-cart-button]') && !document.querySelector('[data-testid=add-to-cart-button]').disabled" },
    @{ sleep = 1000 },
    @{ click = "[data-testid=add-to-cart-button]" },
    @{ waitFor = "document.querySelector('[data-testid=add-to-cart-message]')" },
    @{ name = "added"; eval = "document.querySelector('[data-testid=add-to-cart-message]').textContent" },
    @{ goto = "$browserBase/cart" },
    @{ waitFor = "document.querySelector('[data-testid=checkout-link]')" },
    @{ click = "[data-testid=checkout-link]" },
    @{ waitFor = "document.querySelector('[data-testid=address-form]')" },
    @{ eval = "(() => { const set = (n, v) => { const i = document.querySelector('[name=' + n + ']'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, v); i.dispatchEvent(new Event('input', { bubbles: true })) }; set('phone', '05321112233'); set('city', 'Izmir'); set('district', 'Bornova'); set('line', 'Kazimdirik Mah. 5'); set('postal_code', '35100'); return true })()"; name = "filled" },
    @{ sleep = 500 },
    @{ click = "[data-testid=pay-button]" },
    @{ waitFor = "location.host === 'localhost:8101' && document.querySelector('[data-testid=card-form]')"; timeout = 20000 },
    @{ eval = "(() => { document.querySelector('[name=holder]').value = 'Browser Buyer'; document.querySelector('[name=card_number]').value = '4242 4242 4242 4242'; document.querySelector('[name=expiry]').value = '12/30'; document.querySelector('[name=cvc]').value = '123'; document.querySelector('form').submit(); return true })()"; name = "card" },
    @{ waitFor = "document.querySelector('[data-testid=three-ds]')"; timeout = 15000 },
    @{ eval = "(() => { document.querySelector('[name=code]').value = '123456'; document.querySelector('button[value=confirm]').click(); return true })()"; name = "threeDs" },
    @{ waitFor = "document.querySelector('[data-testid=order-paid]')"; timeout = 30000 },
    @{ name = "result"; eval = "document.querySelector('[data-testid=order-paid]').textContent" },
    @{ name = "url"; eval = "location.href" },
    @{ goto = "about:blank" },
    @{ sleep = 1500 }
)
[IO.File]::WriteAllText($stepsFile, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
$run = (& node (Join-Path $PSScriptRoot "tools\browse.mjs") $stepsFile 2>&1 | Out-String) | ConvertFrom-Json
Remove-Item $stepsFile -Force -ErrorAction SilentlyContinue
$ErrorActionPreference = $prev
Check "browser flow finished ($($run.failure) $($run.results.failedAt))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "product page added the item" ($run.results.added -match "Added 1 to your cart")
Check "payment result page confirms the order" ($run.results.result -match "your order is confirmed" -and $run.results.url -match "/checkout/complete\?order=RS\d{12}")
$browserOrder = ([regex]::Match($run.results.url, "RS\d{12}")).Value
Check "the browser order is paid" (WaitFor { (Order $shopper $browserOrder).status -eq "paid" } 10)
if ($envValues.REAPDEEP_READ_KEY) {
    $rd = NewClient
    $customerId = (Send $shopper GET "$api/api/v1/auth/me").Json.user.id
    function Events([string]$event) { @((Send $rd GET "$reapDeep/api/v1/events?distinct_id=customer-$customerId&event=$event&limit=10" $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events) }
    Check "add_to_cart and checkout_started reached ReapDeep" (WaitFor { @(Events "add_to_cart").Count -ge 1 -and @(Events "checkout_started").Count -ge 1 } 60)
    Check "the server sent the purchase" (WaitFor { @(Events "purchase" | Where-Object { $_.properties.order_id -eq $browserOrder }).Count -eq 1 } 60)
}

$null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" }

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
