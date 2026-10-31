$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$mailpit = "http://127.0.0.1:8025"
$tag = "ac" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
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
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler); Cookies = $handler.CookieContainer }
}

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) { $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 6), [Text.Encoding]::UTF8, "application/json") }
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
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 500 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function Mails([string]$to, [string]$subject) {
    $q = [Uri]::EscapeDataString("to:$to subject:`"$subject`"")
    @((Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$q").messages)
}

function LatestLink([string]$to, [string]$subject, [string]$pattern) {
    $m = Mails $to $subject | Sort-Object { [DateTime]$_.Created } -Descending | Select-Object -First 1
    if (-not $m) { return $null }
    $text = (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/message/$($m.ID)").Text
    $match = [regex]::Match($text, $pattern)
    if ($match.Success) { $match.Groups[1].Value } else { $null }
}

function ClearLimits {
    $pw = $envValues.REDIS_PASSWORD
    $null = docker.exe exec reapseek-redis sh -c "redis-cli -a '$pw' --no-auth-warning --scan --pattern 'ratelimit:*' | xargs -r redis-cli -a '$pw' --no-auth-warning DEL" 2>&1
}

function Browse($steps) {
    $file = Join-Path $env:TEMP "reapseek-account-$tag.json"
    [IO.File]::WriteAllText($file, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & node (Join-Path $PSScriptRoot "tools\browse.mjs") $file 2>&1 | Out-String
    $ErrorActionPreference = $prev
    Remove-Item $file -Force -ErrorAction SilentlyContinue
    $out | ConvertFrom-Json
}

$tokenPattern = "token=([A-Za-z0-9_-]{43})"
ClearLimits
$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }

Write-Host "`n[1] Email verification"
$email = "account.$tag@example.com"
$shopper = NewClient
$null = Send $shopper POST "$api/api/v1/auth/register" @{ email = $email; name = "Account Tester"; password = "first-pass-1" }
$userId = (Send $shopper GET "$api/api/v1/auth/me").Json.user.id
Check "a new customer is not verified yet" ((Send $shopper GET "$api/api/v1/auth/me").Json.user.email_verified -eq $false)
Check "the welcome email carries a confirmation link" (WaitFor { LatestLink $email "Welcome to ReapSeek" "verify-email\?$tokenPattern" } 20)
$firstLink = LatestLink $email "Welcome to ReapSeek" "verify-email\?$tokenPattern"
Check "only a hash of the token is stored" ((Psql "SELECT count(*) FROM account_tokens WHERE token_hash = '$firstLink'") -eq "0" -and (Psql "SELECT count(*) FROM account_tokens WHERE user_id = $userId AND purpose = 'email_verification'") -eq "1")
Check "the outbox never holds a token" ((Psql "SELECT count(*) FROM outbox WHERE payload::text LIKE '%$firstLink%'") -eq "0")
$cookie = ($shopper.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_session" }).Value
$run = Browse @(
    @{ cookie = @{ name = "rs_session"; value = $cookie } },
    @{ goto = "http://localhost:3100/account" },
    @{ waitFor = "document.querySelector('[data-testid=verify-notice]')" },
    @{ click = "[data-testid=verify-notice] button" },
    @{ waitFor = "document.querySelector('[data-testid=verify-resent]')" }
)
Check "the account page asks to confirm and resends a link ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "the resent link replaces the first one" (WaitFor { @(Mails $email "Confirm your email address").Count -eq 1 } 20)
$secondLink = LatestLink $email "Confirm your email address" "verify-email\?$tokenPattern"
Check "the first link no longer works" ((Send (NewClient) POST "$api/api/v1/auth/email/verify" @{ token = $firstLink }).Status -eq 400)
$run = Browse @(
    @{ goto = "http://localhost:3100/verify-email?token=$secondLink" },
    @{ waitFor = "document.querySelector('[data-testid=verify-done], [data-testid=verify-failed]')" },
    @{ name = "state"; eval = "document.querySelector('[data-testid^=verify-]').dataset.testid" }
)
Check "opening the link in a browser confirms the address" ($run.results.state -eq "verify-done" -and (Send $shopper GET "$api/api/v1/auth/me").Json.user.email_verified -eq $true)
Check "a used link cannot be used again" ((Send (NewClient) POST "$api/api/v1/auth/email/verify" @{ token = $secondLink }).Status -eq 400)
Check "a verified customer cannot ask for another link (409)" ((Send $shopper POST "$api/api/v1/auth/email/verification").Status -eq 409)
Check "a malformed token is rejected" ((Send (NewClient) POST "$api/api/v1/auth/email/verify" @{ token = "x" }).Status -eq 400)

Write-Host "`n[2] Forgotten password"
$other = NewClient
$null = Send $other POST "$api/api/v1/auth/login" @{ email = $email; password = "first-pass-1" }
$unknown = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = "nobody.$tag@example.com" }
$known = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $email.ToUpper() }
Check "known and unknown addresses get the same answer (202)" ($unknown.Status -eq 202 -and $known.Status -eq 202 -and $unknown.Text -eq $known.Text)
Check "an invalid address is rejected" ((Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = "not-an-email" }).Status -eq 400)
Check "the reset email arrives with a storefront link" (WaitFor { LatestLink $email "Reset your ReapSeek password" "3100/reset-password\?$tokenPattern" } 20)
Start-Sleep -Seconds 2
Check "nothing is sent to an unknown address" (@(Mails "nobody.$tag@example.com" "Reset").Count -eq 0)
$reset = LatestLink $email "Reset your ReapSeek password" "reset-password\?$tokenPattern"
Check "the link checks as valid" ((Send (NewClient) GET "$api/api/v1/auth/password/reset?token=$reset").Json.valid -eq $true)
Check "a short password is rejected" ((Send (NewClient) POST "$api/api/v1/auth/password/reset" @{ token = $reset; password = "short" }).Status -eq 400)
$run = Browse @(
    @{ goto = "http://localhost:3100/reset-password?token=$reset" },
    @{ waitFor = "document.querySelector('[data-testid=reset-form]')" },
    @{ eval = "(() => { const f = document.querySelector('[data-testid=reset-form]'); f.password.value = 'second-pass-2'; f.confirm.value = 'second-pass-2'; return true })()"; name = "filled" },
    @{ click = "[data-testid=reset-form] button[type=submit]" },
    @{ waitFor = "document.querySelector('[data-testid=reset-done]')" }
)
Check "the customer sets a new password in the browser ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "every session is signed out" ((Send $shopper GET "$api/api/v1/auth/me").Status -eq 401 -and (Send $other GET "$api/api/v1/auth/me").Status -eq 401)
Check "the old password no longer works, the new one does" ((Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = "first-pass-1" }).Status -eq 401 -and
    (Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = "second-pass-2" }).Status -eq 200)
Check "the link works only once" ((Send (NewClient) POST "$api/api/v1/auth/password/reset" @{ token = $reset; password = "third-pass-3" }).Status -eq 400 -and
    (Send (NewClient) GET "$api/api/v1/auth/password/reset?token=$reset").Json.valid -eq $false)
Check "the customer is told the password changed" (WaitFor { @(Mails $email "Your ReapSeek password was changed").Count -eq 1 } 20)
$run = Browse @(@{ goto = "http://localhost:3100/reset-password?token=$reset" }, @{ waitFor = "document.querySelector('[data-testid=reset-invalid]')" })
Check "a used link shows an expired page" ($null -eq $run.failure)

Write-Host "`n[3] Expiry and limits"
ClearLimits
$null = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $email }
Check "a second reset link arrives" (WaitFor { @(Mails $email "Reset your ReapSeek password").Count -eq 2 } 20)
$late = LatestLink $email "Reset your ReapSeek password" "reset-password\?$tokenPattern"
$null = Psql "UPDATE account_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = $userId AND purpose = 'password_reset' AND used_at IS NULL"
Check "an expired link is refused" ((Send (NewClient) POST "$api/api/v1/auth/password/reset" @{ token = $late; password = "third-pass-3" }).Status -eq 400)
$null = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $email }
$null = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $email }
Start-Sleep -Seconds 4
Check "at most three reset emails per hour" (@(Mails $email "Reset your ReapSeek password").Count -eq 3)

Write-Host "`n[4] Suspended accounts"
$suspended = NewClient
$sEmail = "suspended.$tag@example.com"
$null = Send $suspended POST "$api/api/v1/auth/register" @{ email = $sEmail; name = "Suspended Tester"; password = "first-pass-1" }
$sId = (Send $suspended GET "$api/api/v1/auth/me").Json.user.id
$null = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $sEmail }
$null = WaitFor { LatestLink $sEmail "Reset your ReapSeek password" "reset-password\?$tokenPattern" } 20
$pending = LatestLink $sEmail "Reset your ReapSeek password" "reset-password\?$tokenPattern"
$null = Send $admin POST "$api/api/v1/backoffice/customers/$sId/suspend" @{ reason = "Account test $tag" }
Check "a link issued before the suspension stops working" ((Send (NewClient) POST "$api/api/v1/auth/password/reset" @{ token = $pending; password = "second-pass-2" }).Status -eq 400)
$null = Send (NewClient) POST "$api/api/v1/auth/password/forgot" @{ email = $sEmail }
Start-Sleep -Seconds 3
Check "a suspended account gets no new link" (@(Mails $sEmail "Reset your ReapSeek password").Count -eq 1)
Check "staff cannot send one either until it is reinstated (409)" ((Send $admin POST "$api/api/v1/backoffice/customers/$sId/password-reset").Status -eq 409)

Write-Host "`n[5] Sellers and staff"
ClearLimits
$seller = (Send $admin POST "$api/api/v1/backoffice/sellers" @{ name = "Account Seller $tag"; city = "Izmir"; commission_rate = 0.1 }).Json
$sellerEmail = "seller.$tag@example.com"
$null = Send $admin POST "$api/api/v1/backoffice/sellers/$($seller.id)/users" @{ email = $sellerEmail; name = "Seller Owner"; password = "seller-pass-1" }
Check "a seller asks for a reset from the back office (202)" ((Send (NewClient) POST "$api/api/v1/backoffice/auth/password/forgot" @{ email = $sellerEmail }).Status -eq 202)
Check "the seller gets a back office link" (WaitFor { LatestLink $sellerEmail "Reset your ReapSeek password" "3101/#/reset-password\?$tokenPattern" } 20)
$sellerReset = LatestLink $sellerEmail "Reset your ReapSeek password" "reset-password\?$tokenPattern"
$run = Browse @(
    @{ goto = "http://localhost:3101/#/reset-password?token=$sellerReset" },
    @{ waitFor = "document.querySelector('[data-testid=reset-form]')" },
    @{ eval = "(() => { const f = document.querySelector('[data-testid=reset-form]'); f.password.value = 'seller-pass-2'; f.confirm.value = 'seller-pass-2'; return true })()"; name = "filled" },
    @{ click = "[data-testid=reset-form] button[type=submit]" },
    @{ waitFor = "document.querySelector('[data-testid=reset-done]')" }
)
Check "the seller sets a new password in the back office ($($run.failure))" ($null -eq $run.failure -and
    (Send (NewClient) POST "$api/api/v1/backoffice/auth/login" @{ email = $sellerEmail; password = "seller-pass-2" }).Status -eq 200)
$null = Send (NewClient) POST "$api/api/v1/backoffice/auth/password/forgot" @{ email = $envValues.ADMIN_EMAIL }
$null = Send (NewClient) POST "$api/api/v1/backoffice/auth/password/forgot" @{ email = $email }
Start-Sleep -Seconds 3
Check "the back office form never resets staff or customer accounts" (@(Mails $envValues.ADMIN_EMAIL "Reset your ReapSeek password").Count -eq 0 -and
    @(Mails $email "Reset your ReapSeek password").Count -eq 3)

Write-Host "`n[6] Staff sends a reset link"
$helped = NewClient
$hEmail = "helped.$tag@example.com"
$null = Send $helped POST "$api/api/v1/auth/register" @{ email = $hEmail; name = "Helped Tester"; password = "first-pass-1" }
$hId = (Send $helped GET "$api/api/v1/auth/me").Json.user.id
Check "staff emails a reset link (202)" ((Send $admin POST "$api/api/v1/backoffice/customers/$hId/password-reset").Status -eq 202)
Check "the customer receives it" (WaitFor { @(Mails $hEmail "Reset your ReapSeek password").Count -eq 1 } 20)
$adminId = Psql "SELECT id FROM users WHERE role = 'admin' LIMIT 1"
Check "only customers (404 for staff)" ((Send $admin POST "$api/api/v1/backoffice/customers/$adminId/password-reset").Status -eq 404)
Check "customers cannot use it" ((Send $helped POST "$api/api/v1/backoffice/customers/$hId/password-reset").Status -in 401, 403)
$null = Send $helped POST "$api/api/v1/auth/password" @{ current_password = "first-pass-1"; new_password = "second-pass-2" }
Check "changing the password while signed in also sends a notice" (WaitFor { @(Mails $hEmail "Your ReapSeek password was changed").Count -eq 1 } 20)

Write-Host ""
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "All checks passed." -ForegroundColor Green
