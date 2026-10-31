$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$mailpit = "http://127.0.0.1:8025"
$tag = "jobs_test_" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0

function Psql([string]$sql) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
    $ErrorActionPreference = $prev
    "$out".Trim()
}

function Cleanup {
    $null = Psql "DELETE FROM outbox WHERE type LIKE '$tag%' OR (type = 'outbox_failed' AND payload->>'type' LIKE '$tag%')"
}

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    Cleanup
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
    if ($null -ne $body) {
        $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 6), [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed }
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 500 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function AddDoomed([string]$type) {
    (Psql "INSERT INTO outbox (type, payload, attempts) VALUES ('$type', '{}'::jsonb, 9) RETURNING id") -replace '\s.*$', ''
}

function State([string]$id) {
    Psql "SELECT CASE WHEN processed_at IS NOT NULL THEN 'processed' WHEN discarded_at IS NOT NULL THEN 'discarded' WHEN failed_at IS NOT NULL THEN 'failed' ELSE 'open' END || '/' || attempts FROM outbox WHERE id = $id"
}

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice/outbox$path" $body }

Write-Host "`n[1] Access"
Check "guests get 401" ((Send (NewClient) GET "$api/api/v1/backoffice/outbox").Status -eq 401)
Check "an unknown state is rejected" ((A GET "?state=lost").Status -eq 400)

Write-Host "`n[2] A job that gives up"
$typeA = "${tag}_a"
$idA = AddDoomed $typeA
Check "the worker gives up after the tenth attempt" (WaitFor { (State $idA) -eq "failed/10" } 20)
Check "an outbox_failed job is queued for it" ((Psql "SELECT count(*) FROM outbox WHERE type = 'outbox_failed' AND payload->>'message_id' = '$idA'") -eq "1")
$mailQuery = [Uri]::EscapeDataString('subject:"Background job ' + $idA + '"')
Check "the admins get an email" (WaitFor { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$mailQuery").messages_count -ge 1 } 20)
$list = A GET "?state=failed&type=$typeA"
$row = @($list.Json.messages | Where-Object { $_.id -eq [long]$idA })[0]
Check "the failed list shows it with the error" ($list.Status -eq 200 -and $row -and $row.attempts -eq 10 -and $row.last_error -like "*Unknown outbox message type*")
$summary = @($list.Json.types | Where-Object { $_.type -eq $typeA })[0]
Check "the summary counts it as failed" ($summary -and $summary.failed -eq 1 -and $summary.pending -eq 0)

Write-Host "`n[3] Retry and discard"
Check "retry resets the attempts" ((A POST "/$idA/retry").Status -eq 204 -and (Psql "SELECT attempts <= 1 AND failed_at IS NULL FROM outbox WHERE id = $idA") -eq "t")
Check "the retried job runs again" (WaitFor { (Psql "SELECT attempts FROM outbox WHERE id = $idA") -ge "1" } 10)
Check "an open job cannot be retried or discarded" ((A POST "/$idA/retry").Status -eq 409 -and (A POST "/$idA/discard").Status -eq 409)
$null = Psql "UPDATE outbox SET attempts = 9, available_at = now() WHERE id = $idA"
Check "it fails again" (WaitFor { (State $idA) -eq "failed/10" } 20)
Check "discard moves it out of the failed list" ((A POST "/$idA/discard").Status -eq 204 -and (State $idA) -eq "discarded/10" -and
    @((A GET "?state=failed&type=$typeA").Json.messages).Count -eq 0 -and @((A GET "?state=discarded&type=$typeA").Json.messages).Count -eq 1)
Check "an unknown job is 404" ((A POST "/999999999/retry").Status -eq 404)

Write-Host "`n[4] Retry all failed jobs of a type"
$typeB = "${tag}_b"
$b1 = AddDoomed $typeB
$b2 = AddDoomed $typeB
Check "both give up" (WaitFor { (State $b1) -eq "failed/10" -and (State $b2) -eq "failed/10" } 20)
$bulk = A POST "/retry" @{ type = $typeB }
Check "bulk retry picks only that type" ($bulk.Status -eq 200 -and $bulk.Json.retried -eq 2 -and (State $idA) -eq "discarded/10")
Check "both run again" (WaitFor { (Psql "SELECT count(*) FROM outbox WHERE type = '$typeB' AND failed_at IS NULL AND attempts >= 1") -eq "2" } 10)

Write-Host "`n[5] A failure notice never chains"
$null = Psql "INSERT INTO outbox (type, payload, attempts) VALUES ('outbox_failed', '{`"message_id`": 0, `"type`": `"${tag}_c`"}'::jsonb, 9)"
Check "a failed notice does not queue another notice" (WaitFor { (Psql "SELECT count(*) FROM outbox WHERE type = 'outbox_failed' AND payload->>'type' = '${tag}_c' AND (processed_at IS NOT NULL OR failed_at IS NOT NULL)") -eq "1" } 20)
Check "no notice about the notice" ((Psql "SELECT count(*) FROM outbox o WHERE o.type = 'outbox_failed' AND (o.payload->>'type') = 'outbox_failed'") -eq "0")

Cleanup
Write-Host ""
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "All checks passed." -ForegroundColor Green
