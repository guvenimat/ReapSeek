param(
    [string]$ReapDeepEnv = (Join-Path $PSScriptRoot "..\..\..\ReapDeep\deploy\local\.env"),
    [string]$ReapDeepApi = "http://localhost:8001",
    [string]$ProjectName = "ReapSeek",
    [string]$ReapSeekApi = "http://127.0.0.1:8100",
    [string]$AlertWebhookUrl = "http://host.docker.internal:8100/api/v1/reapdeep/alerts",
    [switch]$NewKeys
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

function Read-EnvFile([string]$path) {
    $values = @{}
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -cmatch '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') { $values[$matches[1]] = $matches[2].Trim() }
    }
    $values
}

function Set-EnvValues([hashtable]$updates) {
    $path = Join-Path $PSScriptRoot ".env"
    $lines = [Collections.Generic.List[string]]::new([IO.File]::ReadAllLines($path))
    foreach ($key in $updates.Keys) {
        $index = -1
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -cmatch "^\s*$key\s*=") { $index = $i } }
        if ($index -ge 0) { $lines[$index] = "$key=$($updates[$key])" } else { $lines.Add("$key=$($updates[$key])") }
    }
    [IO.File]::WriteAllLines($path, $lines, (New-Object Text.UTF8Encoding($false)))
}

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = New-Object System.Net.CookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)

function Call([string]$method, [string]$path, $body = $null, [string]$project = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), "$ReapDeepApi/api/v1$path")
    if ($project) { $req.Headers.Add("X-Project-Id", $project) }
    if ($null -ne $body) { $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 10), [Text.Encoding]::UTF8, "application/json") }
    $r = $client.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    if (-not $r.IsSuccessStatusCode) { throw "$method $path failed with $([int]$r.StatusCode): $text" }
    if ($text) { $text | ConvertFrom-Json } else { $null }
}

if (-not (Test-Path $ReapDeepEnv)) { throw "ReapDeep .env not found at $ReapDeepEnv. Start the ReapDeep stack first or pass -ReapDeepEnv." }
$reapDeep = Read-EnvFile $ReapDeepEnv
$local = Initialize-LocalEnv

Write-Host "Signing in to ReapDeep at $ReapDeepApi as $($reapDeep.ADMIN_EMAIL)"
$me = Call POST "/auth/login" @{ email = $reapDeep.ADMIN_EMAIL; password = $reapDeep.ADMIN_PASSWORD }
$owned = @($me.projects | Where-Object { $_.role -eq "owner" })
if ($owned.Count -eq 0) { throw "The ReapDeep admin is not an owner of any project." }

$project = @($me.projects | Where-Object { $_.name -eq $ProjectName }) | Select-Object -First 1
if ($project) {
    Write-Host "Using the existing ReapDeep project '$ProjectName' (id $($project.id))"
    $projectId = "$($project.id)"
} else {
    $created = Call POST "/projects" @{ name = $ProjectName; timezone = "Europe/Istanbul" } "$($owned[0].id)"
    $projectId = "$($created.id)"
    Write-Host "Created the ReapDeep project '$ProjectName' (id $projectId)"
}
$detail = Call GET "/project" $null $projectId

$updates = @{ REAPDEEP_PROJECT_ID = $projectId; REAPDEEP_PUBLIC_KEY = $detail.public_key }
$sameProject = $local.REAPDEEP_PROJECT_ID -eq $projectId
if ($NewKeys -or -not $sameProject -or -not $local.REAPDEEP_CATALOG_KEY) {
    $updates.REAPDEEP_CATALOG_KEY = (Call POST "/api-keys" @{ name = "ReapSeek catalog sync"; scope = "catalog_write" } $projectId).key
    Write-Host "Created a catalog_write key for the catalog sync"
}
if ($NewKeys -or -not $sameProject -or -not $local.REAPDEEP_READ_KEY) {
    $updates.REAPDEEP_READ_KEY = (Call POST "/api-keys" @{ name = "ReapSeek read"; scope = "read" } $projectId).key
    Write-Host "Created a read key"
}
if ($NewKeys -or -not $sameProject -or -not $local.REAPDEEP_RECOMMENDATION_SECRET) {
    if (-not $NewKeys -and $detail.recommendation_secret) {
        $updates.REAPDEEP_RECOMMENDATION_SECRET = $detail.recommendation_secret
        Write-Host "Using the project's recommendation secret"
    } else {
        $updates.REAPDEEP_RECOMMENDATION_SECRET = (Call POST "/project/recommendation-secret" @{} $projectId).recommendation_secret
        Write-Host "Created a recommendation secret for personalized recommendations"
    }
}
Set-EnvValues $updates

$audiences = @(
    @{ name = "Champions and loyal customers"; segments = @("champions", "loyal") },
    @{ name = "Customers at risk"; segments = @("at_risk", "cant_lose", "about_to_sleep") },
    @{ name = "New customers"; segments = @("new_customers", "promising") }
)
$existing = @((Call GET "/cohorts" $null $projectId).cohorts | ForEach-Object { $_.name })
foreach ($a in $audiences) {
    if ($existing -contains $a.name) { continue }
    $definition = @{ groups = @(@{ conditions = @(@{ type = "rfm_segment"; segments = $a.segments; within_days = 365; event = "purchase" }) }) }
    $null = Call POST "/cohorts" @{ name = $a.name; kind = "dynamic"; definition = $definition } $projectId
    Write-Host "Created the ReapDeep cohort '$($a.name)' for campaigns"
}
Write-Host "Updated deploy/local/.env: $(($updates.Keys | Sort-Object) -join ', ')"

Write-Host "Restarting the worker and the API with the new settings"
$ErrorActionPreference = "Continue"
docker compose up -d worker api storefront
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }
docker compose run --rm migrate --reindex
if ($LASTEXITCODE -ne 0) { throw "Requesting a catalog sync failed." }
$ErrorActionPreference = "Stop"

$alertName = "Purchases dropped"
$alert = @((Call GET "/alerts" $null $projectId).alerts | Where-Object { $_.name -eq $alertName }) | Select-Object -First 1
if (-not $alert) {
    $insight = Call POST "/insights" @{ name = "Purchases per day"; kind = "trends"; query = @{ series = @(@{ event = "purchase"; metric = "total" }); filters = @(); chart = "line" } } $projectId
    $alert = Call POST "/alerts" @{ name = $alertName; insight_id = $insight.id; condition = "decrease"; threshold = 50; window = "day"; webhook_url = $AlertWebhookUrl } $projectId
    Write-Host "Created the ReapDeep alert '$alertName'"
}
$reapSeek = New-Object System.Net.Http.HttpClient((New-Object System.Net.Http.HttpClientHandler -Property @{ CookieContainer = New-Object System.Net.CookieContainer }))
function ReapSeekCall([string]$method, [string]$path, $body) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), "$ReapSeekApi/api/v1$path")
    $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress), [Text.Encoding]::UTF8, "application/json")
    $r = $reapSeek.SendAsync($req).Result
    if (-not $r.IsSuccessStatusCode) { throw "$method $path failed with $([int]$r.StatusCode): $($r.Content.ReadAsStringAsync().Result)" }
}
$ready = $false
for ($i = 0; $i -lt 30 -and -not $ready; $i++) { try { $ready = (Invoke-WebRequest -UseBasicParsing "$ReapSeekApi/healthz").StatusCode -eq 200 } catch { Start-Sleep 2 } }
ReapSeekCall POST "/backoffice/auth/login" @{ email = $local.ADMIN_EMAIL; password = $local.ADMIN_PASSWORD }
ReapSeekCall PUT "/backoffice/alerts/subscriptions" @{ alert_id = $alert.id; secret = $alert.secret }
Write-Host "ReapDeep alert '$alertName' (id $($alert.id)) delivers to ReapSeek"
Write-Host "ReapSeek is connected to ReapDeep project $projectId. The ReapDeep public key is $($detail.public_key)." -ForegroundColor Green
