function Initialize-LocalEnv {
    $path = Join-Path $PSScriptRoot ".env"
    $required = [ordered]@{
        POSTGRES_PASSWORD = { [guid]::NewGuid().ToString('N') }
        ADMIN_EMAIL       = { "admin@reapseek.local" }
        ADMIN_PASSWORD    = { [guid]::NewGuid().ToString('N').Substring(0, 20) }
        SANDBOX_SECRET_KEY     = { "sk_test_" + [guid]::NewGuid().ToString('N') }
        SANDBOX_WEBHOOK_SECRET = { "whsec_" + [guid]::NewGuid().ToString('N') }
        REDIS_PASSWORD         = { [guid]::NewGuid().ToString('N') }
    }
    $existing = if (Test-Path $path) { [IO.File]::ReadAllText($path) } else { "" }
    $missing = @($required.Keys | Where-Object { $existing -cnotmatch "(?m)^\s*$_\s*=" })
    if ($missing.Count -gt 0) {
        $add = ($missing | ForEach-Object { "$_=$(& $required[$_])" }) -join "`n"
        if ($existing.Length -gt 0 -and -not $existing.EndsWith("`n")) { $add = "`n" + $add }
        [IO.File]::AppendAllText($path, $add + "`n", (New-Object Text.UTF8Encoding($false)))
        Write-Host "Added to ${path}: $($missing -join ', ')"
    }
    $values = @{}
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -cmatch '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') { $values[$matches[1]] = $matches[2].Trim() }
    }
    return $values
}

function Wait-SellerSnapshot([long[]]$Ids, [string]$Status = "active", [int]$Seconds = 20) {
    $sql = "SELECT count(*) FROM catalog.seller_snapshots WHERE status = '$Status' AND id IN ($($Ids -join ','))"
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
        $count = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
        $ErrorActionPreference = $prev
        if ("$count".Trim() -eq "$($Ids.Count)") { return $true }
        Start-Sleep -Milliseconds 500
    } until ((Get-Date) -gt $deadline)
    return $false
}

function Wait-Ledger([string]$OrderNumber, [string[]]$Kinds, [int]$Seconds = 20) {
    $sql = "SELECT count(DISTINCT kind) FROM finance.seller_ledger WHERE order_number = '$OrderNumber' AND kind IN ('$($Kinds -join "','")')"
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
        $count = $sql | docker.exe exec -i reapseek-postgres psql -U reapseek -d reapseek -At 2>&1
        $ErrorActionPreference = $prev
        if ("$count".Trim() -eq "$($Kinds.Count)") { return $true }
        Start-Sleep -Milliseconds 500
    } until ((Get-Date) -gt $deadline)
    return $false
}
