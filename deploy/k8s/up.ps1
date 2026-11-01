param(
    [string]$Revision = "main",
    [switch]$SkipBuild,
    [switch]$Local,
    [switch]$ConnectReapDeep
)

$ErrorActionPreference = "Stop"
$env:Path = [Environment]::GetEnvironmentVariable("Path", "User") + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine")
foreach ($tool in "docker", "k3d", "kubectl", "helm") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not installed" }
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$k8s = $PSScriptRoot
$envFile = Join-Path $root "deploy\local\.env"
if (-not (Test-Path $envFile)) { throw "Missing $envFile - run deploy\local\env.ps1 first" }

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Run([scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $block" }
}

if (-not ((k3d cluster list -o json | ConvertFrom-Json) | Where-Object { $_.name -eq "reapseek" })) {
    Step "Creating the k3d cluster"
    Run { k3d cluster create --config (Join-Path $k8s "cluster.yaml") --wait }
}
Run { kubectl config use-context k3d-reapseek | Out-Null }

$images = "reapseek-api", "reapseek-worker", "reapseek-sandbox", "reapseek-storefront", "reapseek-backoffice", "reapseek-simulator"
if (-not $SkipBuild) {
    Step "Building images"
    Push-Location (Join-Path $root "deploy\local")
    try {
        Run { docker compose --profile simulator build migrate worker sandbox storefront backoffice simulator }
    } finally { Pop-Location }
}
Step "Importing images into the cluster"
Run { k3d image import ($images | ForEach-Object { "${_}:local" }) --cluster reapseek }

Step "Creating the namespace and secrets"
Run { kubectl create namespace reapseek --dry-run=client -o yaml | kubectl apply -f - | Out-Null }
$secretFile = Join-Path $env:TEMP "reapseek-k8s.env"
Get-Content $envFile | Where-Object { $ConnectReapDeep -or $_ -notmatch "^REAPDEEP_" } | Set-Content -Path $secretFile -Encoding ascii
try {
    Run { kubectl -n reapseek create secret generic reapseek-secrets "--from-env-file=$secretFile" --dry-run=client -o yaml | kubectl apply -f - | Out-Null }
} finally { Remove-Item $secretFile -Force }

Step "Installing Kyverno"
Run { helm repo add kyverno https://kyverno.github.io/kyverno/ --force-update | Out-Null }
Run { helm upgrade --install kyverno kyverno/kyverno -n kyverno --create-namespace --version 3.9.2 --wait --timeout 10m `
    --set admissionController.replicas=1 --set backgroundController.enabled=false --set cleanupController.enabled=false --set reportsController.enabled=false | Out-Null }
Run { kubectl apply -f (Join-Path $k8s "policies") | Out-Null }

if ($Local) {
    Step "Installing ReapSeek with Helm from the working tree"
    Run { helm upgrade --install reapseek (Join-Path $k8s "chart\reapseek") -n reapseek --set simulator.live=true --wait --timeout 15m }
} else {
    Step "Installing Argo CD"
    Run { helm repo add argo https://argoproj.github.io/argo-helm --force-update | Out-Null }
    Run { helm upgrade --install argocd argo/argo-cd -n argocd --create-namespace --version 10.11.6 --wait --timeout 10m `
        --set dex.enabled=false --set notifications.enabled=false --set applicationSet.replicas=0 --set configs.params."server\.insecure"=true | Out-Null }
    Step "Registering the ReapSeek application (revision $Revision)"
    $app = (Get-Content (Join-Path $k8s "argocd\reapseek.yaml") -Raw) -replace "targetRevision: main", "targetRevision: $Revision"
    $app | kubectl apply -f - | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not register the Argo CD application" }
}

Write-Host ""
Write-Host "Storefront   http://shop.localhost:8080"
Write-Host "Back office  http://office.localhost:8080"
Write-Host "Grafana      http://grafana.localhost:8080"
Write-Host "Mailpit      http://mail.localhost:8080"
if (-not $Local) {
    Write-Host "Argo CD      kubectl -n argocd port-forward svc/argocd-server 8443:80  (user admin, password: kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath='{.data.password}')"
}
