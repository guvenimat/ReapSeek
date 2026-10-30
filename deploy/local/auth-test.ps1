$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://localhost:8100"
$storefront = "http://localhost:3100"
$backoffice = "http://localhost:3101"
$mailpit = "http://localhost:8025"
$tag = "t" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$email = "jane.$tag@example.com"
$password = "first-pass-$tag"
$newPassword = "second-pass-$tag"
$script:failed = 0

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    [pscustomobject]@{ Http = $client; Cookies = $handler.CookieContainer }
}

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text; Location = $(if ($r.Headers.Location) { $r.Headers.Location.ToString() } else { $null }) }
}

function CookieValue($client, [string]$url, [string]$name) {
    $c = $client.Cookies.GetCookies([Uri]$url) | Where-Object { $_.Name -ceq $name }
    if ($c) { $c.Value } else { $null }
}

function Psql([string]$sql) {
    $out = $sql | docker exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    "$out".Trim()
}

Write-Host "`n[1] Health and migrations"
$plain = NewClient
Check "API /healthz is ok" ((Send $plain GET "$api/healthz").Json.status -eq "ok")
Check "back office proxies /healthz" ((Send $plain GET "$backoffice/healthz").Json.status -eq "ok")
Check "migration 0001 is recorded" ((Psql "SELECT count(*) FROM schema_migrations WHERE version = '0001_identity_and_outbox'") -eq "1")

Write-Host "`n[2] Registration"
$jane = NewClient
$r = Send $jane POST "$api/api/v1/auth/register" @{ email = "not-an-email"; name = "Jane"; password = $password }
Check "invalid email is rejected (400)" ($r.Status -eq 400 -and $r.Json.error -like "*email*")
$r = Send $jane POST "$api/api/v1/auth/register" @{ email = $email; name = "Jane"; password = "short" }
Check "short password is rejected (400)" ($r.Status -eq 400 -and $r.Json.error -like "*8-1024*")
$r = Send $jane POST "$api/api/v1/auth/register" "{not json"
Check "malformed JSON is rejected (400)" ($r.Status -eq 400)
$r = Send $jane POST "$api/api/v1/auth/register" @{ email = "  " + $email.ToUpper() + " "; name = "  Jane Doe "; password = $password }
Check "registration succeeds (201)" ($r.Status -eq 201)
Check "email is normalized and role is customer" ($r.Json.user.email -eq $email -and $r.Json.user.name -eq "Jane Doe" -and $r.Json.user.role -eq "customer")
Check "storefront session cookie is set" ($null -ne (CookieValue $jane $api "rs_session"))
$r = Send $jane GET "$api/api/v1/auth/me"
Check "registered customer is signed in" ($r.Status -eq 200 -and $r.Json.user.email -eq $email)
$r = Send (NewClient) POST "$api/api/v1/auth/register" @{ email = $email; name = "Other"; password = $password }
Check "duplicate email is rejected (409)" ($r.Status -eq 409)
Check "password is stored as a PBKDF2 hash" ((Psql "SELECT password_hash LIKE 'pbkdf2-sha256`$600000`$%' FROM users WHERE email = '$email'") -eq "t")

Write-Host "`n[3] Welcome email through the outbox"
$mail = $null
$deadline = (Get-Date).AddSeconds(20)
while (-not $mail -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    $search = Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=to:$email"
    if ($search.messages_count -gt 0) { $mail = $search.messages[0] }
}
Check "welcome email reached Mailpit" ($null -ne $mail)
Check "email subject is 'Welcome to ReapSeek'" ($mail.Subject -ceq "Welcome to ReapSeek")
$outbox = Psql "SELECT processed_at IS NOT NULL, attempts, failed_at IS NULL FROM outbox WHERE type = 'user_registered' AND payload->>'email' = '$email'"
Check "outbox message processed once ($outbox)" ($outbox -eq "t|1|t")

Write-Host "`n[4] Sign in"
$r = Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = "wrong-password" }
Check "wrong password is rejected (401)" ($r.Status -eq 401)
$r = Send (NewClient) POST "$api/api/v1/auth/login" @{ email = "nobody.$tag@example.com"; password = $password }
Check "unknown email gets the same 401" ($r.Status -eq 401 -and $r.Json.error -eq "Invalid email or password")
$second = NewClient
$r = Send $second POST "$storefront/api/v1/auth/login" @{ email = $email.ToUpper(); password = $password }
Check "sign in through the storefront proxy works" ($r.Status -eq 200 -and $null -ne (CookieValue $second $storefront "rs_session"))
Check "second session is valid" ((Send $second GET "$storefront/api/v1/auth/me").Status -eq 200)
Check "anonymous /me is 401" ((Send (NewClient) GET "$api/api/v1/auth/me").Status -eq 401)

Write-Host "`n[5] Storefront and back office sessions are separate"
Check "storefront session is not accepted by the back office" ((Send $jane GET "$api/api/v1/backoffice/auth/me").Status -eq 401)
$r = Send (NewClient) POST "$api/api/v1/backoffice/auth/login" @{ email = $email; password = $password }
Check "customer cannot sign in to the back office (403)" ($r.Status -eq 403)
$admin = NewClient
$r = Send $admin POST "$backoffice/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }
Check "bootstrap admin signs in to the back office" ($r.Status -eq 200 -and $r.Json.user.role -eq "admin")
Check "back office cookie is rs_bo_session" ($null -ne (CookieValue $admin $backoffice "rs_bo_session") -and $null -eq (CookieValue $admin $backoffice "rs_session"))
Check "back office /me works" ((Send $admin GET "$backoffice/api/v1/backoffice/auth/me").Json.user.email -eq $envValues.ADMIN_EMAIL)
Check "back office session is not a storefront session" ((Send $admin GET "$storefront/api/v1/auth/me").Status -eq 401)
Check "sessions table records both audiences" ((Psql "SELECT count(DISTINCT audience) FROM sessions") -eq "2")

Write-Host "`n[6] Password change"
$r = Send $jane POST "$api/api/v1/auth/password" @{ current_password = "wrong"; new_password = $newPassword }
Check "wrong current password is rejected (400)" ($r.Status -eq 400)
$r = Send $jane POST "$api/api/v1/auth/password" @{ current_password = $password; new_password = "short" }
Check "short new password is rejected (400)" ($r.Status -eq 400)
$r = Send $jane POST "$api/api/v1/auth/password" @{ current_password = $password; new_password = $newPassword }
Check "password change succeeds (204)" ($r.Status -eq 204)
Check "current session stays signed in" ((Send $jane GET "$api/api/v1/auth/me").Status -eq 200)
Start-Sleep -Seconds 1
Check "other session is signed out" ((Send $second GET "$storefront/api/v1/auth/me").Status -eq 401)
Check "old password no longer works" ((Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = $password }).Status -eq 401)
Check "new password works" ((Send (NewClient) POST "$api/api/v1/auth/login" @{ email = $email; password = $newPassword }).Status -eq 200)

Write-Host "`n[7] Storefront pages (server-side rendering)"
$sf = NewClient
$r = Send $sf POST "$storefront/api/v1/auth/login" @{ email = $email; password = $newPassword }
$homePage = Send $sf GET "$storefront/"
Check "home page greets the signed-in customer" ($homePage.Status -eq 200 -and $homePage.Text -cmatch 'data-testid="account-link"' -and $homePage.Text -cmatch "Jane")
$anonHome = Send (NewClient) GET "$storefront/"
Check "anonymous home page shows Sign in" ($anonHome.Status -eq 200 -and $anonHome.Text -cmatch 'data-testid="sign-in-link"')
Check "home page renders category tiles" ($anonHome.Text -cmatch 'data-testid="category-grid"' -and $anonHome.Text -cmatch "Home &amp; Living")
$r = Send $sf GET "$storefront/account"
Check "account page shows the customer" ($r.Status -eq 200 -and $r.Text -cmatch [regex]::Escape($email))
$r = Send (NewClient) GET "$storefront/account"
Check "anonymous account page redirects to sign in" ($r.Status -in 302, 303, 307 -and $r.Location -cmatch "/login\?next=(/|%2F)account")
$r = Send $sf GET "$storefront/login"
Check "signed-in customer is redirected away from /login" ($r.Status -in 302, 303, 307)
$r = Send (NewClient) GET "$storefront/login?next=//evil.example.com"
Check "login page renders for anonymous users" ($r.Status -eq 200 -and $r.Text -cmatch 'data-testid="login-form"')
$r = Send (NewClient) GET "$storefront/c/electronics"
Check "category page renders" ($r.Status -eq 200 -and $r.Text -cmatch "<h1>Electronics</h1>")
Check "unknown category is 404" ((Send (NewClient) GET "$storefront/c/no-such-category").Status -eq 404)
Check "robots.txt is served" ((Send (NewClient) GET "$storefront/robots.txt").Text -cmatch "Disallow: /account")

Write-Host "`n[8] Sign out"
Check "sign out returns 204" ((Send $sf POST "$storefront/api/v1/auth/logout").Status -eq 204)
Check "session is gone after sign out" ((Send $sf GET "$storefront/api/v1/auth/me").Status -eq 401)
Check "back office sign out returns 204" ((Send $admin POST "$backoffice/api/v1/backoffice/auth/logout").Status -eq 204)
Check "back office session is gone" ((Send $admin GET "$backoffice/api/v1/backoffice/auth/me").Status -eq 401)

Write-Host "`n[9] Rate limit (run last; the limit resets after a minute)"
$limited = $false
$flood = NewClient
for ($i = 0; $i -lt 25 -and -not $limited; $i++) {
    if ((Send $flood POST "$api/api/v1/auth/login" @{ email = $email; password = "wrong" }).Status -eq 429) { $limited = $true }
}
Check "repeated sign-in attempts are rate limited (429)" $limited

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
