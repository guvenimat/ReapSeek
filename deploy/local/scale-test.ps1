$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$a = "http://127.0.0.1:8100"
$b = "http://127.0.0.1:8102"
$tag = "s" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
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

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) {
        $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 6), [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    $retry = $null
    if ($r.Headers.RetryAfter -and $r.Headers.RetryAfter.Delta) { $retry = $r.Headers.RetryAfter.Delta.TotalSeconds }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; RetryAfter = $retry }
}

function Redis([string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & docker.exe exec reapseek-redis redis-cli @arguments 2>&1
    $ErrorActionPreference = $prev
    "$out".Trim()
}

function Psql([string]$sql) {
    $out = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    "$out".Trim()
}

function ContainerQuiet([string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    & docker.exe @arguments 2>&1 | Out-Null
    $ErrorActionPreference = $prev
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 300 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function ClearRateLimits {
    foreach ($key in @((Redis @("--scan", "--pattern", "ratelimit:*")) -split "\s+" | Where-Object { $_ })) { $null = Redis @("DEL", $key) }
}

Write-Host "`n[0] Two API instances"
$probe = NewClient
Check "instance A answers" ((Send $probe GET "$a/healthz").Status -eq 200)
Check "instance B answers (docker compose --profile scale up -d)" ((Send $probe GET "$b/healthz").Status -eq 200)
Check "Redis answers" ((Redis @("PING")) -eq "PONG")
if ($script:failed -gt 0) { throw "Start the stack with: docker compose --profile scale up -d" }

Write-Host "`n[1] Shared sign-in rate limit"
ClearRateLimits
$limit = 20
$guest = NewClient
$statuses = @()
for ($i = 0; $i -lt $limit; $i++) {
    $url = if ($i % 2 -eq 0) { $a } else { $b }
    $statuses += (Send $guest POST "$url/api/v1/auth/login" @{ email = "nobody.$tag@example.com"; password = "wrong-pass-1" }).Status
}
Check "$limit failed sign-ins spread over both instances are all answered (401)" (@($statuses | Where-Object { $_ -eq 401 }).Count -eq $limit)
$overA = Send $guest POST "$a/api/v1/auth/login" @{ email = "nobody.$tag@example.com"; password = "wrong-pass-1" }
$overB = Send $guest POST "$b/api/v1/auth/login" @{ email = "nobody.$tag@example.com"; password = "wrong-pass-1" }
Check "the next attempt is 429 on instance A" ($overA.Status -eq 429)
Check "and on instance B: the limit is shared, not per instance" ($overB.Status -eq 429)
Check "429 carries Retry-After" ($overA.RetryAfter -gt 0 -and $overA.RetryAfter -le 60)
Check "one counter in Redis for the client" ((Redis @("--scan", "--pattern", "ratelimit:auth:*")) -cmatch '^ratelimit:auth:\d+\.\d+\.\d+\.\d+$')
ClearRateLimits

Write-Host "`n[2] Sessions across instances"
$email = "scale.$tag@example.com"
$one = NewClient
Check "register on A" ((Send $one POST "$a/api/v1/auth/register" @{ email = $email; name = "Scale Buyer"; password = "scale-pass-1" }).Status -eq 201)
Check "the session works on B" ((Send $one GET "$b/api/v1/auth/me").Status -eq 200)
Check "the session is cached in Redis" ((Redis @("--scan", "--pattern", "session:*")).Length -gt 0)
Check "sign out on A" ((Send $one POST "$a/api/v1/auth/logout").Status -in 200, 204)
Check "B rejects the signed-out session at once" ((Send $one GET "$b/api/v1/auth/me").Status -eq 401)

$first = NewClient
$second = NewClient
$null = Send $first POST "$a/api/v1/auth/login" @{ email = $email; password = "scale-pass-1" }
$null = Send $second POST "$b/api/v1/auth/login" @{ email = $email; password = "scale-pass-1" }
Check "two devices are signed in, cached on both instances" ((Send $first GET "$b/api/v1/auth/me").Status -eq 200 -and (Send $second GET "$a/api/v1/auth/me").Status -eq 200)
Check "password change on A" ((Send $first POST "$a/api/v1/auth/password" @{ current_password = "scale-pass-1"; new_password = "scale-pass-2" }).Status -in 200, 204)
Check "the other device is signed out on B at once" ((Send $second GET "$b/api/v1/auth/me").Status -eq 401)
Check "the device that changed the password stays signed in on B" ((Send $first GET "$b/api/v1/auth/me").Status -eq 200)

Write-Host "`n[3] Product page cache"
$admin = NewClient
if ((Send $admin POST "$a/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed" }
function Bo([string]$method, [string]$path, $body = $null) { Send $admin $method "$a/api/v1/backoffice$path" $body }
$dresses = (Bo GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (Bo GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$seller = (Bo POST "/sellers" @{ name = "Scale Seller $tag"; city = "Izmir" }).Json
$product = (Bo POST "/catalog/products" @{ title = "Scale Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Black") } }).Json
$variant = $product.variants[0].id
$null = Bo PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $variant; price = 300; stock = 10 }
$null = Bo PATCH "/catalog/products/$($product.id)" @{ status = "active" }
$null = WaitFor { (Psql "SELECT count(*) FROM outbox WHERE type = 'products_changed' AND processed_at IS NULL AND failed_at IS NULL") -eq "0" } 20
function Page([string]$base) { Send $probe GET "$base/api/v1/catalog/products/$($product.slug)" }
function PriceOn([string]$base) { $p = (Page $base).Json; if ($p) { [double]$p.variants[0].offers[0].price } else { $null } }
Check "B serves the new product" ((PriceOn $b) -eq 300)
Check "and caches it with a TTL" ([int](Redis @("TTL", "product:$($product.slug)")) -gt 0)
$null = Bo PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $variant; price = 250; stock = 10 }
Check "a price change on A reaches B within 5 seconds (outbox invalidation)" (WaitFor { (PriceOn $b) -eq 250 } 5)
$null = Psql "UPDATE offers SET stock = 3 WHERE variant_id = $variant"
Start-Sleep -Seconds 2
Check "a change that bypasses the outbox stays cached (bounded by the TTL)" ($null -eq (Page $b).Json.variants[0].offers[0].low_stock)
$null = Bo PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $variant; price = 250; stock = 3 }
Check "the next outbox change refreshes it" (WaitFor { (Page $b).Json.variants[0].offers[0].low_stock -eq 3 } 5)
$null = Bo PATCH "/catalog/products/$($product.id)" @{ status = "archived" }
Check "archiving on A removes the page on B" (WaitFor { (Page $b).Status -eq 404 } 5)

Write-Host "`n[4] Category tree cache"
$null = Send $probe GET "$b/api/v1/catalog/categories"
Check "the tree is cached" ((Redis @("EXISTS", "catalog:categories")) -eq "1")
$women = (Bo GET "/catalog/categories").Json.categories | Where-Object { $_.path -eq "women" }
$created = (Bo POST "/catalog/categories" @{ parent_id = $women.id; name = "Scale Scarves $tag"; image_kind = "bag" }).Json
$tree = (Send $probe GET "$b/api/v1/catalog/categories").Json
$womenNode = $tree.departments | Where-Object { $_.slug -eq "women" }
Check "a category created on A shows on B at once" (@($womenNode.children | Where-Object { $_.id -eq $created.id }).Count -eq 1)
$null = Psql "DELETE FROM categories WHERE id = $($created.id)"
$null = Redis @("DEL", "catalog:categories")

Write-Host "`n[5] Redis outage"
ContainerQuiet @("stop", "reapseek-redis")
$down = NewClient
Check "product pages are served from Postgres" ((Send $down GET "$b/api/v1/catalog/products/mira-lane-tiered-crepe-shirt-dress-p1").Status -eq 200)
Check "sign-in still works (local rate limit)" ((Send $down POST "$a/api/v1/auth/login" @{ email = $email; password = "scale-pass-2" }).Status -eq 200)
Check "the session is read from Postgres" ((Send $down GET "$b/api/v1/auth/me").Status -eq 200)
Check "search still works" ((Send $down GET "$b/api/v1/search?q=dress").Status -eq 200)
ContainerQuiet @("start", "reapseek-redis")
Check "Redis is back" (WaitFor { (Redis @("PING")) -eq "PONG" } 30)
Check "the instances use it again" (WaitFor { $null = Send $down GET "$b/api/v1/catalog/products/mira-lane-tiered-crepe-shirt-dress-p1"; (Redis @("EXISTS", "product:mira-lane-tiered-crepe-shirt-dress-p1")) -eq "1" } 30)
ClearRateLimits

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
