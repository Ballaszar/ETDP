param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipFrontendBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$frontendRoot = Join-Path $projectRoot "frontend"
$publishRoot = Join-Path $projectRoot ("artifacts\\native\\backend-" + $Runtime)
$frontendDist = Join-Path $frontendRoot "dist"

function Resolve-Tool {
    param(
        [string]$CommandName,
        [string[]]$Candidates
    )

    try {
        $resolved = & where.exe $CommandName 2>$null | Select-Object -First 1
        if (($resolved | Out-String).Trim()) {
            return (($resolved | Out-String).Trim())
        }
    } catch {
    }

    foreach ($candidate in $Candidates) {
        if (($candidate | Out-String).Trim() -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    throw "Required tool '$CommandName' was not found."
}

function Copy-DirectoryMirror {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Source folder not found: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & robocopy $Source $Destination /MIR /R:2 /W:1 /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
    $exitCode = $LASTEXITCODE
    if ($exitCode -gt 7) {
        throw "Robocopy failed for '$Source' -> '$Destination' (exit code $exitCode)."
    }
}

$dotnetExe = Resolve-Tool -CommandName "dotnet" -Candidates @(
    (Join-Path $env:ProgramFiles "dotnet\\dotnet.exe")
)
$npmExe = Resolve-Tool -CommandName "npm.cmd" -Candidates @(
    (Join-Path $env:ProgramFiles "nodejs\\npm.cmd")
)

if (-not (Test-Path $frontendRoot)) {
    throw "Frontend folder not found: $frontendRoot"
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

Push-Location $projectRoot
try {
    if (-not $SkipFrontendBuild) {
        Push-Location $frontendRoot
        try {
            & $npmExe run build
        } finally {
            Pop-Location
        }
    }

    $args = @(
        "publish",
        "ETDP.csproj",
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-o", $publishRoot,
        "-p:PublishSingleFile=false",
        "-p:PublishReadyToRun=true",
        "-p:RestoreIgnoreFailedSources=true"
    )

    if ($NoRestore) {
        $args += "--no-restore"
    }

    & $dotnetExe @args

    if (Test-Path $frontendDist) {
        $publishWebRoot = Join-Path $publishRoot "wwwroot"
        Copy-DirectoryMirror -Source $frontendDist -Destination $publishWebRoot
    }
} finally {
    Pop-Location
}

Write-Host "Native backend publish completed."
Write-Host "Publish output: $publishRoot"
