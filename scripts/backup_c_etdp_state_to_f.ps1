param(
    [string]$SourceRoot = "C:\ETDP",
    [string]$BackupRoot = "F:\ETDP_Backups",
    [switch]$IncludeCodexKb = $true
)

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host ("[backup] " + $Message)
}

function Invoke-RobocopyDir {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path $Source)) {
        return [pscustomobject]@{
            Source      = $Source
            Destination = $Destination
            Type        = "directory"
            Status      = "missing"
            ExitCode    = $null
        }
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & robocopy $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /XJ /NFL /NDL /NP /NJH /NJS | Out-Null
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        Source      = $Source
        Destination = $Destination
        Type        = "directory"
        Status      = if ($exitCode -le 7) { "copied" } else { "failed" }
        ExitCode    = $exitCode
    }
}

function Copy-FileSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path $Source)) {
        return [pscustomobject]@{
            Source      = $Source
            Destination = $Destination
            Type        = "file"
            Status      = "missing"
            ExitCode    = $null
        }
    }

    $destParent = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrWhiteSpace($destParent)) {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    Copy-Item -Path $Source -Destination $Destination -Force
    return [pscustomobject]@{
        Source      = $Source
        Destination = $Destination
        Type        = "file"
        Status      = "copied"
        ExitCode    = 0
    }
}

if (-not (Test-Path $SourceRoot)) {
    throw "Source root not found: $SourceRoot"
}
if (-not (Test-Path $BackupRoot)) {
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupSessionRoot = Join-Path $BackupRoot ("C_ETDP_state_backup_" + $timestamp)
New-Item -ItemType Directory -Path $backupSessionRoot -Force | Out-Null

Write-Info "Source root: $SourceRoot"
Write-Info "Backup root: $backupSessionRoot"

$results = New-Object System.Collections.Generic.List[object]
$stackDirs = Get-ChildItem $SourceRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "Stack*" }

foreach ($stack in $stackDirs) {
    Write-Info ("Processing " + $stack.Name)
    $destBase = Join-Path $backupSessionRoot $stack.Name

    $dirSpecs = @(
        "ETDP\Imports",
        "ETDP\Exports",
        "ETDP\Requests",
        "ETDP\SystemData",
        "VocationalLLM\data",
        "SystemData\Runtime"
    )

    foreach ($rel in $dirSpecs) {
        $src = Join-Path $stack.FullName $rel
        $dst = Join-Path $destBase $rel
        $results.Add((Invoke-RobocopyDir -Source $src -Destination $dst))
    }

    $fileSpecs = @(
        "ETDP\etdp.db",
        "ETDP\etdp.db-wal",
        "ETDP\etdp.db-shm",
        "VocationalLLM\config\settings.yaml",
        "VocationalLLM\data\smi_admin_login.env",
        "VocationalLLM\data\smi_runtime_mode.txt",
        "VocationalLLM\data\vocational_llm.db"
    )

    foreach ($rel in $fileSpecs) {
        $src = Join-Path $stack.FullName $rel
        $dst = Join-Path $destBase $rel
        $results.Add((Copy-FileSafe -Source $src -Destination $dst))
    }
}

if ($IncludeCodexKb) {
    $kbSource = Join-Path $SourceRoot ".codex-kb"
    $kbDest = Join-Path $backupSessionRoot ".codex-kb"
    $results.Add((Invoke-RobocopyDir -Source $kbSource -Destination $kbDest))
}

$copied = @($results | Where-Object { $_.Status -eq "copied" }).Count
$missing = @($results | Where-Object { $_.Status -eq "missing" }).Count
$failed = @($results | Where-Object { $_.Status -eq "failed" }).Count

$manifest = [pscustomobject]@{
    created_at_utc = (Get-Date).ToUniversalTime().ToString("o")
    source_root = $SourceRoot
    backup_root = $backupSessionRoot
    stack_count = @($stackDirs).Count
    summary = [pscustomobject]@{
        copied = $copied
        missing = $missing
        failed = $failed
    }
    items = $results
}

$manifestPath = Join-Path $backupSessionRoot "backup-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Info ("Copied: " + $copied)
Write-Info ("Missing: " + $missing)
Write-Info ("Failed: " + $failed)
Write-Info ("Manifest: " + $manifestPath)

if ($failed -gt 0) {
    throw "Backup completed with failures. Review $manifestPath"
}

Write-Info "Backup completed successfully."
