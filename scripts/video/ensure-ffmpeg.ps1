param(
    [string]$InstallRoot = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
if (-not $InstallRoot) {
    $InstallRoot = Join-Path $repoRoot "tools\ffmpeg"
}

$ffmpegExe = Join-Path $InstallRoot "bin\ffmpeg.exe"
if (Test-Path $ffmpegExe) {
    Write-Output $ffmpegExe
    exit 0
}

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

$tmpRoot = Join-Path $env:TEMP "etdp-ffmpeg"
$zipPath = Join-Path $tmpRoot "ffmpeg-release-essentials.zip"
$extractPath = Join-Path $tmpRoot "extract"
New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null

$url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

if (Test-Path $extractPath) {
    Remove-Item -Recurse -Force $extractPath
}
New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

$dist = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
if (-not $dist) {
    throw "Could not extract ffmpeg archive."
}

$binSource = Join-Path $dist.FullName "bin"
if (-not (Test-Path (Join-Path $binSource "ffmpeg.exe"))) {
    throw "ffmpeg.exe not found in extracted archive."
}

Copy-Item -Path $binSource -Destination (Join-Path $InstallRoot "bin") -Recurse -Force

if (-not (Test-Path $ffmpegExe)) {
    throw "ffmpeg install completed but binary was not found at $ffmpegExe"
}

Write-Output $ffmpegExe
