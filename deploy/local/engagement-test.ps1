param([string]$ReapDeepEnv = (Join-Path $PSScriptRoot "..\..\..\ReapDeep\deploy\local\.env"))

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$mailpit = "http://127.0.0.1:8025"
$reapDeep = "http://127.0.0.1:8001"
$ingest = "http://127.0.0.1:8000"
$tag = "e" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0
$script:cleanup = @()

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    foreach ($c in $script:cleanup) { try { & $c } catch {} }
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

function Send($client, [string]$method, [string]$url, $body = $null, [hashtable]$headers = @{}) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    foreach ($h in $headers.Keys) { $req.Headers.TryAddWithoutValidation($h, $headers[$h]) | Out-Null }
    if ($null -ne $body) {
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

function Mails([string]$query) { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$([Uri]::EscapeDataString($query))").messages_count }

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

Write-Host "`n[1] Abandoned cart reminder"
$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$seller = (A POST "/sellers" @{ name = "Reminder Seller $tag"; city = "Izmir" }).Json
$product = (A POST "/catalog/products" @{ title = "Reminder Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Navy") } }).Json
$script:cleanup += { $null = A PATCH "/catalog/products/$($product.id)" @{ status = "archived" } }
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $product.variants[0].id; price = 220; stock = 10 }
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$offerId = ((A GET "/catalog/products/$($product.id)").Json.variants[0].offers | Where-Object { $_.seller_id -eq $seller.id }).id

$shopper = NewClient
$null = Send $shopper POST "$api/api/v1/auth/register" @{ email = "cart.$tag@example.com"; name = "Cart Shopper"; password = "buyer-pass-1" }
$emptier = NewClient
$null = Send $emptier POST "$api/api/v1/auth/register" @{ email = "empty.$tag@example.com"; name = "Cart Emptier"; password = "buyer-pass-1" }
$guest = NewClient
$null = Send $shopper POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 2 }
$null = Send $emptier POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
$null = Send $guest POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
$null = Send $emptier DELETE "$api/api/v1/cart/items/$offerId"
$subject = "subject:""You left something in your cart"""
Check "an idle cart gets one reminder email" (WaitFor { (Mails "$subject to:cart.$tag@example.com") -eq 1 } 60)
$message = (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$([Uri]::EscapeDataString("$subject to:cart.$tag@example.com"))").messages[0]
$text = (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/message/$($message.ID)").Text
Check "it lists the items with the current price and links to the cart" ($text -match "2 x Reminder Dress $tag - 440.00 TRY" -and $text -match "/cart")
Start-Sleep -Seconds 30
Check "only one reminder while the cart does not change" ((Mails "$subject to:cart.$tag@example.com") -eq 1)
Check "an emptied cart gets no reminder" ((Mails "$subject to:empty.$tag@example.com") -eq 0)
$null = Send $shopper POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
Check "changing the cart starts a new idle period" (WaitFor { (Mails "$subject to:cart.$tag@example.com") -eq 2 } 60)
if ($envValues.REAPDEEP_READ_KEY) {
    $customerId = (Send $shopper GET "$api/api/v1/auth/me").Json.user.id
    Check "cart_reminder_sent reached ReapDeep" (WaitFor { @((Send (NewClient) GET "$reapDeep/api/v1/events?distinct_id=customer-$customerId&event=cart_reminder_sent&limit=10" $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events).Count -ge 1 } 60)
}

Write-Host "`n[2] ReapDeep alerts"
$rd = @{}
foreach ($line in [IO.File]::ReadAllLines($ReapDeepEnv)) { if ($line -cmatch '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') { $rd[$matches[1]] = $matches[2].Trim() } }
$rdSession = NewClient
$null = Send $rdSession POST "$reapDeep/api/v1/auth/login" @{ email = $rd.ADMIN_EMAIL; password = $rd.ADMIN_PASSWORD }
function ReapDeepCall([string]$method, [string]$path, $body = $null) { Send $rdSession $method "$reapDeep/api/v1$path" $body @{ "X-Project-Id" = $envValues.REAPDEEP_PROJECT_ID } }
$probe = "alert_probe_$tag"
$insight = (ReapDeepCall POST "/insights" @{ name = "Probe $tag"; kind = "trends"; query = @{ series = @(@{ event = $probe; metric = "total" }); filters = @(); chart = "line" } }).Json
$alert = (ReapDeepCall POST "/alerts" @{ name = "Probe volume $tag"; insight_id = $insight.id; condition = "above"; threshold = 2; window = "day"; webhook_url = "http://host.docker.internal:8100/api/v1/reapdeep/alerts" }).Json
$script:cleanup += { $null = ReapDeepCall DELETE "/alerts/$($alert.id)"; $null = ReapDeepCall DELETE "/insights/$($insight.id)" }
Check "an alert is defined in ReapDeep with its own secret" ($alert.id -gt 0 -and $alert.secret)
Check "ReapSeek rejects a webhook from an alert it does not know (401)" ((Send (NewClient) POST "$api/api/v1/reapdeep/alerts" "{}" @{ "X-ReapDeep-Alert" = "$($alert.id)"; "X-ReapDeep-Signature" = "sha256=00" }).Status -eq 401)
Check "the admin connects the alert" ((A PUT "/alerts/subscriptions" @{ alert_id = $alert.id; secret = $alert.secret }).Status -eq 204)
$now = [DateTimeOffset]::UtcNow
$batch = @(1..4 | ForEach-Object { @{ event = $probe; distinct_id = "probe-$tag-$_"; timestamp = $now.ToString("o"); properties = @{} } })
$null = Send (NewClient) POST "$ingest/capture" @{ api_key = $envValues.REAPDEEP_PUBLIC_KEY; sent_at = $now.ToString("o"); batch = $batch }
Check "the alert fires in ReapDeep when the trend crosses the threshold" (WaitFor { (ReapDeepCall POST "/alerts/$($alert.id)/check").Json.state -eq "firing" } 60)
function Events { @((A GET "/alerts").Json.events | Where-Object { $_.alert_id -eq $alert.id }) }
Check "the signed webhook lands in the back office" (WaitFor { @(Events | Where-Object { $_.state -eq "firing" -and $_.value -eq 4 -and $_.insight_name -eq "Probe $tag" }).Count -eq 1 } 30)
Check "admins are emailed" (WaitFor { (Mails "subject:""Alert: Probe volume $tag""") -ge 1 } 30)

$checkedAt = [DateTime]::UtcNow.ToString("o")
$payload = '{"alert_id":' + $alert.id + ',"alert_name":"Probe volume ' + $tag + '","project_id":' + $envValues.REAPDEEP_PROJECT_ID + ',"state":"ok","condition":"above","threshold":2,"window":"day","value":1,"previous_value":4,"message":"1 is not above 2","insight":{"id":' + $insight.id + ',"name":"Probe ' + $tag + '"},"checked_at":"' + $checkedAt + '"}'
$hmac = New-Object System.Security.Cryptography.HMACSHA256 (,[Text.Encoding]::UTF8.GetBytes($alert.secret))
$signature = "sha256=" + (($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload)) | ForEach-Object { $_.ToString("x2") }) -join "")
$headers = @{ "X-ReapDeep-Alert" = "$($alert.id)"; "X-ReapDeep-Signature" = $signature }
Check "a tampered body is rejected (401)" ((Send (NewClient) POST "$api/api/v1/reapdeep/alerts" ($payload -replace '"value":1', '"value":9') $headers).Status -eq 401)
$first = Send (NewClient) POST "$api/api/v1/reapdeep/alerts" $payload $headers
$again = Send (NewClient) POST "$api/api/v1/reapdeep/alerts" $payload $headers
Check "a resolved alert is recorded once even if delivered twice" ($first.Status -eq 200 -and $again.Status -eq 200 -and @(Events | Where-Object { $_.state -eq "ok" }).Count -eq 1)
Check "sellers cannot read alerts (401/403)" ((Send (NewClient) GET "$api/api/v1/backoffice/alerts").Status -eq 401)

foreach ($c in $script:cleanup) { & $c }

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
