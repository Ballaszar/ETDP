param(
    [switch]$SideloadDesktop,
    [switch]$SideloadWeb,
    [switch]$ServerOnly,
    [switch]$SkipCerts
)

$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

function Resolve-NpmCommand {
    $workspaceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $candidates = @(
        (Join-Path $workspaceRoot "tools\node-v24.14.0-win-x64\npm.cmd"),
        (Join-Path $workspaceRoot "tools\node-v24.14.0-win-x64\node_modules\npm\bin\npm.cmd"),
        "C:\Program Files\nodejs\node_modules\npm.cmd",
        
	"C:\Program Files (x86)\nodejs\npm.cmd",
        "C:\ProgramData\chocolatey\bin\npm.cmd"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    return $null
}

function Invoke-Npm {
    param(
        [string[]]$Arguments
    )

    & $script:NpmCmd @Arguments
    exit $LASTEXITCODE
}

function Resolve-NodeCommand {
    $npmDirectory = Split-Path -Parent $script:NpmCmd
    $nodeCandidate = Join-Path $npmDirectory "node.exe"
    if (Test-Path $nodeCandidate) {
        return $nodeCandidate
    }

    $command = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    return $null
}

function Ensure-EdgeWebViewLoopback {
    param(
        [string]$NodeCmd
    )

    $devSettingsCli = Join-Path $PSScriptRoot "node_modules\office-addin-dev-settings\cli.js"
    if (!(Test-Path $devSettingsCli) -or -not $NodeCmd) {
        return
    }

    Write-Host "Ensuring Edge WebView loopback is enabled..."
    & $NodeCmd $devSettingsCli appcontainer EdgeWebView --loopback -y
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Unable to pre-enable Edge WebView loopback automatically. Sideloading may still prompt."
    }
}

$NpmCmd = Resolve-NpmCommand
if (-not $NpmCmd) {
    throw "npm was not found. Install Node.js 18+ first or keep the bundled Node runtime in F:\ETDP\tools."
}

$env:PATH = "$(Split-Path -Parent $NpmCmd);$env:PATH"
Write-Host "Using npm: $NpmCmd"

$NodeCmd = Resolve-NodeCommand
if (-not $NodeCmd) {
    throw "node.exe was not found next to npm. Install Node.js 18+ first or keep the bundled Node runtime in F:\ETDP\tools."
}

if (-not (Test-Path ".\node_modules")) {
    Write-Host "Installing npm packages..."
    & $NpmCmd install
}

if (-not $SkipCerts) {
    Write-Host "Ensuring localhost development certificate is trusted..."
    & $NpmCmd run certs
}

if (($env:OS -eq "Windows_NT") -and ($SideloadDesktop -or $SideloadWeb)) {
    Ensure-EdgeWebViewLoopback -NodeCmd $NodeCmd
}

if ($SideloadDesktop) {
    Write-Host "Starting sideload session for Word desktop..."
    & $NpmCmd run sideload:desktop
    exit $LASTEXITCODE
}

if ($SideloadWeb) {
    Write-Host "Starting sideload session for Word on the web..."
    & $NpmCmd run sideload:web
    exit $LASTEXITCODE
}

if ($ServerOnly) {
    Write-Host "Starting HTTPS add-in server (https://localhost:3000)..."
    & $NpmCmd start
    exit $LASTEXITCODE
}

Write-Host "Starting HTTPS add-in server (https://localhost:3000)..."
Start-Process -FilePath "powershell.exe" `
    -ArgumentList @(
        "-NoProfile",
        "-WindowStyle", "Hidden",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "start_word_apa_assistant.ps1"),
        "-ServerOnly",
        "-SkipCerts"
    ) `
    -WindowStyle Hidden | Out-Null

Write-Host "Done."
Write-Host "Manifest: $PSScriptRoot\manifest.xml"
Write-Host "Taskpane URL: https://localhost:3000/taskpane.html"
