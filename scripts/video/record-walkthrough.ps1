param(
    [string]$FrontendUrl = "http://localhost:5173",
    [string]$ApiProbeUrl = "http://localhost:5299/api/Qualification",
    [switch]$KeepServers,
    [switch]$Mp4,
    [string]$Scenario = "",
    [switch]$Narrate,
    [double]$Pace = 1.0,
    [string]$Voice = "Microsoft Zira",
    [int]$VoiceRate = -2,
    [int]$StartupTimeoutSec = 300,
    [int]$ApiProbeTimeoutSec = 180
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$frontendDir = Join-Path $repoRoot "frontend"
$logDir = Join-Path $repoRoot "artifacts\video\logs"

New-Item -Path $logDir -ItemType Directory -Force | Out-Null

function Test-PortOpen {
    param([int]$Port)

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne(400)) { return $false }
        $client.EndConnect($async)
        return $true
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Wait-HttpReady {
    param(
        [string]$Url,
        [int]$TimeoutSec = 120
    )

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $TimeoutSec) {
        try {
            $null = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 5 -UseBasicParsing
            return $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    return $false
}

$startedBackend = $null
$startedFrontend = $null

try {
    if (-not (Test-PortOpen -Port 5299)) {
        Write-Host "Starting backend on http://localhost:5299 ..."
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backendOut = Join-Path $logDir "backend-$stamp.out.log"
        $backendErr = Join-Path $logDir "backend-$stamp.err.log"
        $startedBackend = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList @("run", "--launch-profile", "http") `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $backendOut `
            -RedirectStandardError $backendErr `
            -PassThru
    } else {
        Write-Host "Backend already running on port 5299."
    }

    if (-not (Test-PortOpen -Port 5173)) {
        Write-Host "Starting frontend on http://localhost:5173 ..."
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $frontendOut = Join-Path $logDir "frontend-$stamp.out.log"
        $frontendErr = Join-Path $logDir "frontend-$stamp.err.log"
        $startedFrontend = Start-Process `
            -FilePath "npm.cmd" `
            -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", "5173", "--strictPort") `
            -WorkingDirectory $frontendDir `
            -RedirectStandardOutput $frontendOut `
            -RedirectStandardError $frontendErr `
            -PassThru
    } else {
        Write-Host "Frontend already running on port 5173."
    }

    if (-not (Wait-HttpReady -Url $FrontendUrl -TimeoutSec $StartupTimeoutSec)) {
        throw "Frontend did not become ready at $FrontendUrl."
    }

    if (-not (Wait-HttpReady -Url $ApiProbeUrl -TimeoutSec $ApiProbeTimeoutSec)) {
        Write-Warning "API probe did not respond at $ApiProbeUrl. Recording will still run."
    }

    $nodeArgs = @("scripts/video/record-walkthrough.mjs", "--url", $FrontendUrl)
    if ($Scenario) {
        $nodeArgs += @("--scenario", $Scenario)
    }
    if ($Mp4) {
        $nodeArgs += "--mp4"
    }
    if ($Narrate) {
        $nodeArgs += "--narrate"
        $nodeArgs += @("--voice", $Voice)
        $nodeArgs += @("--voice-rate", [string]$VoiceRate)
    }
    if ($Pace -gt 0) {
        $nodeArgs += @("--pace", [string]$Pace)
    }

    Write-Host "Starting walkthrough recorder ..."
    & node @nodeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Recorder exited with code $LASTEXITCODE."
    }

    Write-Host "Walkthrough recording finished."
} finally {
    if ($KeepServers) {
        Write-Host "KeepServers enabled. Leaving backend/frontend running."
    } else {
        if ($startedFrontend -and -not $startedFrontend.HasExited) {
            try { Stop-Process -Id $startedFrontend.Id -Force } catch {}
        }

        if ($startedBackend -and -not $startedBackend.HasExited) {
            try { Stop-Process -Id $startedBackend.Id -Force } catch {}
        }
    }
}
