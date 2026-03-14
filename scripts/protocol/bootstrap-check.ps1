param(
    [string]$BackendBase = "http://localhost:5299",
    [int]$TimeoutSec = 15,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
$base = $BackendBase.Trim().TrimEnd("/")

function Invoke-GetJson {
    param([string]$Path)

    $uri = "$base$Path"
    try {
        $resp = Invoke-WebRequest -Uri $uri -Method Get -TimeoutSec $TimeoutSec -UseBasicParsing
        $data = $null
        if (-not [string]::IsNullOrWhiteSpace($resp.Content)) {
            $data = $resp.Content | ConvertFrom-Json
        }

        return [pscustomobject]@{
            path = $Path
            uri = $uri
            ok = $true
            status = [int]$resp.StatusCode
            error = $null
            data = $data
        }
    }
    catch {
        $status = $null
        try { $status = [int]$_.Exception.Response.StatusCode.value__ } catch {}

        return [pscustomobject]@{
            path = $Path
            uri = $uri
            ok = $false
            status = $status
            error = $_.Exception.Message
            data = $null
        }
    }
}

$qualification = Invoke-GetJson -Path "/api/Qualification"
$quality = Invoke-GetJson -Path "/api/Quality/checks"
$pools = Invoke-GetJson -Path "/api/Content/knowledge-pools"

$failedChecks = @()
$checkCount = 0
if ($quality.ok -and $quality.data -and $quality.data.checks) {
    $checks = @($quality.data.checks)
    $checkCount = $checks.Count
    $failedChecks = @($checks | Where-Object { -not $_.pass })
}

$totalMaterials = 0
$poolSummary = @()
if ($pools.ok -and $pools.data) {
    if ($null -ne $pools.data.totalMaterials) {
        $totalMaterials = [int]$pools.data.totalMaterials
    }
    if ($pools.data.pools) {
        $poolSummary = @($pools.data.pools | ForEach-Object {
            [pscustomobject]@{
                pool = $_.pool
                count = [int]$_.count
            }
        })
    }
}

$endpointFailures = @($qualification, $quality, $pools | Where-Object { -not $_.ok })
$readiness = "READY"
$nextActions = New-Object System.Collections.Generic.List[string]

if ($endpointFailures.Count -gt 0) {
    $readiness = "BLOCKED"
    $nextActions.Add("Start backend API and re-run this check.")
}
elseif ($checkCount -eq 0) {
    $readiness = "ATTENTION"
    $nextActions.Add("Quality checks endpoint returned no checks; verify backend state.")
}
elseif ($failedChecks.Count -gt 0 -or $totalMaterials -le 0) {
    $readiness = "ATTENTION"
}

if ($failedChecks.Count -gt 0) {
    $nextActions.Add("Resolve failed quality gates before production exports.")
}
if ($totalMaterials -le 0) {
    $nextActions.Add("Ingest source materials before running paragraph search.")
}
if ($nextActions.Count -eq 0) {
    $nextActions.Add("System is ready for protocol sequence execution.")
}

$report = [pscustomobject]@{
    timestampUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    backendBase = $base
    readiness = $readiness
    endpoints = @(
        [pscustomobject]@{ path = $qualification.path; ok = $qualification.ok; status = $qualification.status; error = $qualification.error },
        [pscustomobject]@{ path = $quality.path; ok = $quality.ok; status = $quality.status; error = $quality.error },
        [pscustomobject]@{ path = $pools.path; ok = $pools.ok; status = $pools.status; error = $pools.error }
    )
    quality = [pscustomobject]@{
        totalChecks = $checkCount
        failedCount = $failedChecks.Count
        failedKeys = @($failedChecks | ForEach-Object { $_.key })
    }
    knowledge = [pscustomobject]@{
        totalMaterials = $totalMaterials
        pools = $poolSummary
    }
    nextActions = $nextActions
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 8
}
else {
    Write-Host "Bootstrap Readiness: $($report.readiness)"
    Write-Host "Backend: $($report.backendBase)"
    Write-Host ""
    Write-Host "Endpoints:"
    foreach ($ep in $report.endpoints) {
        $statusText = if ($ep.ok) { "OK ($($ep.status))" } else { "FAIL ($($ep.status)) $($ep.error)" }
        Write-Host (" - {0}: {1}" -f $ep.path, $statusText)
    }
    Write-Host ""
    Write-Host ("Quality checks: {0} total, {1} failed" -f $report.quality.totalChecks, $report.quality.failedCount)
    if ($report.quality.failedKeys.Count -gt 0) {
        Write-Host ("Failed gates: " + ($report.quality.failedKeys -join ", "))
    }
    Write-Host ("Knowledge materials: {0}" -f $report.knowledge.totalMaterials)
    if ($report.knowledge.pools.Count -gt 0) {
        Write-Host "Pool counts:"
        foreach ($pool in $report.knowledge.pools) {
            Write-Host (" - {0}: {1}" -f $pool.pool, $pool.count)
        }
    }
    Write-Host ""
    Write-Host "Next actions:"
    foreach ($action in $report.nextActions) {
        Write-Host (" - {0}" -f $action)
    }
}

if ($report.readiness -eq "BLOCKED") {
    exit 2
}
if ($report.readiness -eq "ATTENTION") {
    exit 1
}
exit 0
