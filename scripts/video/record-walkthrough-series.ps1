param(
    [string]$FrontendUrl = "http://localhost:5173",
    [string]$ApiProbeUrl = "http://localhost:5299/api/Qualification",
    [double]$Pace = 2.3,
    [switch]$Narrate,
    [string]$Voice = "Microsoft Zira",
    [int]$VoiceRate = -2,
    [switch]$Mp4,
    [int]$StartupTimeoutSec = 300,
    [int]$ApiProbeTimeoutSec = 180
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runner = Join-Path $scriptDir "record-walkthrough.ps1"

if (-not (Test-Path $runner)) {
    throw "Runner not found: $runner"
}

$scenarios = @(
    (Join-Path $scriptDir "walkthrough.part1.foundation.json"),
    (Join-Path $scriptDir "walkthrough.part2.curriculum-content.json"),
    (Join-Path $scriptDir "walkthrough.part3.operations-security.json")
)

foreach ($scenario in $scenarios) {
    if (-not (Test-Path $scenario)) {
        throw "Scenario file not found: $scenario"
    }

    Write-Host "Recording scenario: $scenario"
    $args = @(
        "-File", $runner,
        "-FrontendUrl", $FrontendUrl,
        "-ApiProbeUrl", $ApiProbeUrl,
        "-Scenario", $scenario,
        "-Pace", $Pace,
        "-Voice", $Voice,
        "-VoiceRate", $VoiceRate,
        "-StartupTimeoutSec", $StartupTimeoutSec,
        "-ApiProbeTimeoutSec", $ApiProbeTimeoutSec
    )
    if ($Narrate) { $args += "-Narrate" }
    if ($Mp4) { $args += "-Mp4" }

    & powershell -ExecutionPolicy Bypass @args
    if ($LASTEXITCODE -ne 0) {
        throw "Scenario recording failed: $scenario"
    }
}

Write-Host "Series recording complete."
