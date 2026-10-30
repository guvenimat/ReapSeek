$ErrorActionPreference = "Continue"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
$log = Join-Path $PSScriptRoot "run-local.log"
Start-Transcript -Path $log -Force | Out-Null

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

try {
    Step "Checking Docker"
    docker version --format "Client {{.Client.Version}} / Server {{.Server.Version}}"
    if ($LASTEXITCODE -ne 0) { throw "Docker is not running. Start Docker Desktop." }

    Step "Preparing .env"
    $values = Initialize-LocalEnv

    Step "docker compose up -d --build (the first build takes a few minutes)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }

    Step "Waiting for the API (up to 3 minutes)"
    $deadline = (Get-Date).AddMinutes(3)
    $ready = $false
    while ((Get-Date) -lt $deadline -and -not $ready) {
        try { $ready = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 "http://localhost:8100/healthz").StatusCode -eq 200 } catch { Start-Sleep -Seconds 3 }
    }
    if (-not $ready) { docker compose logs --tail 50 migrate api; throw "The API did not become healthy within 3 minutes." }

    Step "Seeding the demo catalog (skipped when the catalog has data)"
    docker compose run --rm migrate --seed
    if ($LASTEXITCODE -ne 0) { throw "Seeding failed." }

    Step "Container status"
    docker compose ps -a

    Step "Ready"
    Write-Host "Storefront:  http://localhost:3100"
    Write-Host "Back office: http://localhost:3101  (sign in as $($values.ADMIN_EMAIL), password in deploy/local/.env)"
    Write-Host "API:         http://localhost:8100"
    Write-Host "Mailpit:     http://localhost:8025"
    if (-not $values.REAPDEEP_CATALOG_KEY) { Write-Host "`nReapDeep is not connected yet. With the ReapDeep stack running, run .\connect-reapdeep.ps1" -ForegroundColor Yellow }
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
}
