$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$tag = "cu" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    exit 1
}

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler) }
}

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) { $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 6), [Text.Encoding]::UTF8, "application/json") }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed }
}

function Psql([string]$sql) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    $ErrorActionPreference = $prev
    "$out".Trim()
}

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice/customers$path" $body }

Write-Host "`n[1] Access"
Check "guests get 401" ((Send (NewClient) GET "$api/api/v1/backoffice/customers").Status -eq 401)
$shopper = NewClient
$email = "customer.$tag@example.com"
$null = Send $shopper POST "$api/api/v1/auth/register" @{ email = $email; name = "Selin Test"; password = "shopper-pass-1" }
Check "customers cannot open the back office list" ((Send $shopper GET "$api/api/v1/backoffice/customers").Status -in 401, 403)

Write-Host "`n[2] List"
$found = A GET "?q=$tag"
$row = @($found.Json.customers)[0]
Check "search finds the new customer by email" ($found.Status -eq 200 -and $found.Json.total -eq 1 -and $row.email -eq $email -and $row.orders -eq 0 -and $row.spent -eq 0)
Check "search by name works and wildcards are literal" ((A GET "?q=Selin%20Test").Json.total -ge 1 -and (A GET "?q=%25").Json.total -eq 0)
$top = (A GET "?sort=spent").Json
$spent = @($top.customers | ForEach-Object { [decimal]$_.spent })
Check "top spenders are sorted by money spent" ($spent.Count -gt 1 -and (($spent | Sort-Object -Descending) -join ",") -eq ($spent -join ","))
$dbTop = Psql "SELECT round(sum(total), 2) FROM orders WHERE status = 'paid' GROUP BY customer_id ORDER BY 1 DESC LIMIT 1"
Check "the top spender's total matches the database" ([decimal]$spent[0] -eq [decimal]$dbTop)
Check "paging works" ((A GET "?sort=spent&page=2").Json.page -eq 2 -and @((A GET "?sort=spent&page=2").Json.customers)[0].id -ne @($top.customers)[0].id)
Check "an unknown sort is rejected" ((A GET "?sort=random").Status -eq 400)
Check "admins and sellers are not listed" ([int](Psql "SELECT count(*) FROM users WHERE role = 'customer'") -eq (A GET "").Json.total)

Write-Host "`n[3] Detail"
$topId = @($top.customers)[0].id
$detail = (A GET "/$topId").Json
$dbOrders = Psql "SELECT count(*) FROM orders WHERE customer_id = $topId AND status = 'paid'"
Check "the detail has the orders, stats and ReapDeep id" ($detail.stats.orders -eq [int]$dbOrders -and $detail.orders.Count -gt 0 -and $detail.distinct_id -eq "customer-$topId")
Check "an unknown customer is 404" ((A GET "/999999999").Status -eq 404)
$adminId = Psql "SELECT id FROM users WHERE role = 'admin' LIMIT 1"
Check "an admin is not a customer (404)" ((A GET "/$adminId").Status -eq 404)
$analytics = A GET "/$topId/reapdeep"
if ($envValues.REAPDEEP_READ_KEY) {
    Check "ReapDeep gives the RFM segment of a buyer" ($analytics.Status -eq 200 -and $analytics.Json.status -eq "ok" -and $analytics.Json.rfm.segment -and $analytics.Json.rfm.orders -ge 1)
    $withTitle = @($analytics.Json.activity | Where-Object { $_.product_title })
    Check "recent behaviour comes with product titles" ($analytics.Json.activity.Count -gt 0 -and $withTitle.Count -gt 0)
    $newId = $row.id
    $fresh = (A GET "/$newId/reapdeep").Json
    Check "a new customer has no segment yet" ($fresh.status -eq "ok" -and $null -eq $fresh.rfm)
} else {
    Check "without ReapDeep the panel says so" ($analytics.Json.status -eq "not_connected")
}

Write-Host "`n[4] Suspension"
$id = $row.id
$other = NewClient
$null = Send $other POST "$api/api/v1/auth/login" @{ email = $email; password = "shopper-pass-1" }
Check "the customer is signed in on two devices" ((Send $shopper GET "$api/api/v1/auth/me").Status -eq 200 -and (Send $other GET "$api/api/v1/auth/me").Status -eq 200)
Check "a reason is required" ((A POST "/$id/suspend" @{ reason = " " }).Status -eq 400)
Check "the admin suspends the account" ((A POST "/$id/suspend" @{ reason = "Chargeback abuse $tag" }).Status -eq 204)
Check "every session ends at once" ((Send $shopper GET "$api/api/v1/auth/me").Status -eq 401 -and (Send $other GET "$api/api/v1/auth/me").Status -eq 401)
$blocked = Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = "shopper-pass-1" }
Check "sign-in is refused with a clear message" ($blocked.Status -eq 403 -and $blocked.Json.error -like "*suspended*")
Check "suspending twice is a conflict" ((A POST "/$id/suspend" @{ reason = "again" }).Status -eq 409)
$d = (A GET "/$id").Json
Check "the detail shows the reason" ($d.suspended_at -and $d.suspended_reason -eq "Chargeback abuse $tag" -and @((A GET "?status=suspended&q=$tag").Json.customers).Count -eq 1)
Check "the admin reinstates it" ((A POST "/$id/reinstate").Status -eq 204 -and (Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = "shopper-pass-1" }).Status -eq 200)
Check "reinstating twice is a conflict" ((A POST "/$id/reinstate").Status -eq 409)
Check "admins cannot be suspended here (404)" ((A POST "/$adminId/suspend" @{ reason = "x" }).Status -eq 404)

Write-Host ""
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "All checks passed." -ForegroundColor Green
