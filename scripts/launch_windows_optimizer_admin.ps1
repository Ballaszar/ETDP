param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs
)

$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

$appScript = Join-Path $PSScriptRoot "windows_optimizer_app.ps1"
if (!(Test-Path $appScript)) {
    throw "Optimizer script not found: $appScript"
}

$psExe = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
if (!(Test-Path $psExe)) {
    $psExe = "powershell.exe"
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$argLine = "-NoProfile -ExecutionPolicy Bypass -File `"$appScript`""
if ($AppArgs -and $AppArgs.Count -gt 0) {
    $argLine += " " + ($AppArgs -join " ")
}

if (-not $isAdmin) {
    Start-Process -FilePath $psExe -ArgumentList $argLine -Verb RunAs | Out-Null
    exit 0
}

& $psExe -NoProfile -ExecutionPolicy Bypass -File $appScript @AppArgs
exit $LASTEXITCODE
