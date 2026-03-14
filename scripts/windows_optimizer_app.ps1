param(
    [switch]$HeadlessScan,
    [switch]$RunTempCleanup,
    [switch]$RunMemoryOptimize,
    [switch]$RemoveOrphanedStartup,
    [switch]$StopLowRiskUtilities,
    [switch]$StopBrowsers
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeMethods {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
"@

$criticalProcessNames = @(
    "system", "idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
    "fontdrvhost", "sihost", "taskhostw", "spoolsv", "audiodg", "searchindexer", "securityhealthservice",
    "explorer", "memory compression",
    "msmpeng", "powershell", "pwsh", "conhost"
)

$startupLocations = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
)

function Is-CriticalProcessName {
    param([string]$Name)
    $normalized = ($Name | Out-String).Trim().ToLowerInvariant()
    return $criticalProcessNames -contains $normalized
}

function Resolve-CommandPath {
    param([string]$Command)
    $raw = ($Command | Out-String).Trim()
    if (-not $raw) { return "" }
    if ($raw.StartsWith('"')) {
        $parts = $raw.Split('"')
        if ($parts.Length -ge 2) { return $parts[1] }
    }
    return ($raw.Split(" ")[0]).Trim()
}

function Get-ProcessInventory {
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($proc in (Get-Process | Sort-Object WorkingSet64 -Descending)) {
        if ($proc.Id -eq $PID) { continue }
        $name = [string]$proc.ProcessName
        $isCritical = Is-CriticalProcessName -Name $name
        $memoryMb = [math]::Round(($proc.WorkingSet64 / 1MB), 1)
        $cpu = 0.0
        try {
            if ($proc.CPU) { $cpu = [math]::Round([double]$proc.CPU, 1) }
        } catch {
            $cpu = 0.0
        }
        $recommended = (-not $isCritical) -and ($memoryMb -ge 80)
        $reason = "Review"
        if ($isCritical) {
            $reason = "Critical process - protected"
        } elseif ($recommended) {
            $reason = "Candidate to stop if not needed"
        }

        $rows.Add([pscustomobject]@{
            Name = $name
            Id = [int]$proc.Id
            MemoryMB = $memoryMb
            CPU = $cpu
            Responding = [bool]$proc.Responding
            Recommended = [bool]$recommended
            Reason = $reason
        })
    }

    return $rows
}

function Get-StartupEntries {
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($path in $startupLocations) {
        if (-not (Test-Path $path)) { continue }
        try {
            $props = Get-ItemProperty -Path $path -ErrorAction Stop
            foreach ($property in $props.PSObject.Properties) {
                if ($property.Name -in @("PSPath", "PSParentPath", "PSChildName", "PSDrive", "PSProvider")) {
                    continue
                }
                if ($property.Name -eq "(default)") {
                    continue
                }
                $command = [string]$property.Value
                if (-not ($command | Out-String).Trim()) {
                    continue
                }
                $target = Resolve-CommandPath -Command $command
                $exists = $false
                if ($target) {
                    try { $exists = Test-Path $target } catch { $exists = $false }
                }
                $status = if ($exists) { "OK" } else { "Orphaned target" }
                $entries.Add([pscustomobject]@{
                    Location = $path
                    Name = [string]$property.Name
                    Command = $command
                    TargetPath = $target
                    Exists = [bool]$exists
                    Status = $status
                })
            }
        } catch {
        }
    }
    return $entries
}

function Save-StartupBackup {
    param([pscustomobject]$Entry)
    $backupRoot = Join-Path $env:LOCALAPPDATA "SMiTools\StartupRegistryBackups"
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $safeName = ($Entry.Name -replace "[^a-zA-Z0-9\-_\.]", "_")
    $file = Join-Path $backupRoot "$stamp-$safeName.json"
    $Entry | ConvertTo-Json -Depth 6 | Set-Content -Path $file -Encoding ascii
    return $file
}

function Disable-StartupEntry {
    param([pscustomobject]$Entry)
    $backupFile = Save-StartupBackup -Entry $Entry
    Remove-ItemProperty -Path $Entry.Location -Name $Entry.Name -ErrorAction Stop
    return $backupFile
}

function Clear-TempFiles {
    $result = [ordered]@{
        deleted_items = 0
        failed_items = 0
    }
    $paths = @($env:TEMP, "$env:WINDIR\Temp")
    foreach ($path in $paths) {
        if (-not (Test-Path $path)) { continue }
        foreach ($item in (Get-ChildItem -Path $path -Force -ErrorAction SilentlyContinue)) {
            try {
                Remove-Item -Path $item.FullName -Recurse -Force -ErrorAction Stop
                $result.deleted_items++
            } catch {
                $result.failed_items++
            }
        }
    }
    return [pscustomobject]$result
}

function Optimize-MemoryWorkingSet {
    $trimmed = 0
    $failed = 0
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    [System.GC]::Collect()

    foreach ($proc in Get-Process) {
        if ($proc.Id -eq $PID) { continue }
        if (Is-CriticalProcessName -Name $proc.ProcessName) { continue }
        try {
            $ok = [NativeMethods]::SetProcessWorkingSetSize($proc.Handle, [IntPtr](-1), [IntPtr](-1))
            if ($ok) { $trimmed++ } else { $failed++ }
        } catch {
            $failed++
        }
    }
    return [pscustomobject]@{
        trimmed_processes = $trimmed
        failed_processes = $failed
    }
}

function Remove-OrphanedStartupEntries {
    $rows = Get-StartupEntries
    $orphans = @($rows | Where-Object { -not $_.Exists })
    $removed = New-Object System.Collections.Generic.List[object]
    $failed = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $orphans) {
        try {
            $backup = Disable-StartupEntry -Entry $entry
            $removed.Add([pscustomobject]@{
                location = $entry.Location
                name = $entry.Name
                backup = $backup
            }) | Out-Null
        } catch {
            $failed.Add([pscustomobject]@{
                location = $entry.Location
                name = $entry.Name
                error = $_.Exception.Message
            }) | Out-Null
        }
    }
    return [pscustomobject]@{
        total_orphaned = $orphans.Count
        removed = @($removed.ToArray())
        failed = @($failed.ToArray())
    }
}

function Stop-LowRiskUtilityProcesses {
    $targets = @("MSI.CentralServer", "LEDKeeper2")
    $stopped = New-Object System.Collections.Generic.List[string]
    $notFound = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]
    foreach ($target in $targets) {
        $procs = @(Get-Process -Name $target -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) {
            $notFound.Add($target) | Out-Null
            continue
        }
        foreach ($proc in $procs) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                $stopped.Add("$target#$($proc.Id)") | Out-Null
            } catch {
                $failed.Add("$target#$($proc.Id): $($_.Exception.Message)") | Out-Null
            }
        }
    }
    return [pscustomobject]@{
        stopped = @($stopped.ToArray())
        not_found = @($notFound.ToArray())
        failed = @($failed.ToArray())
    }
}

function Stop-BrowserProcesses {
    $targets = @("msedge", "chrome")
    $stopped = New-Object System.Collections.Generic.List[string]
    $notFound = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]

    foreach ($target in $targets) {
        $procs = @(Get-Process -Name $target -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) {
            $notFound.Add($target) | Out-Null
            continue
        }
        foreach ($proc in $procs) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                $stopped.Add("$target#$($proc.Id)") | Out-Null
            } catch {
                $failed.Add("$target#$($proc.Id): $($_.Exception.Message)") | Out-Null
            }
        }
    }

    return [pscustomobject]@{
        stopped = @($stopped.ToArray())
        not_found = @($notFound.ToArray())
        failed = @($failed.ToArray())
    }
}

if ($HeadlessScan) {
    $procRows = Get-ProcessInventory
    $startupRows = Get-StartupEntries
    $output = [pscustomobject]@{
        scan_time = (Get-Date).ToString("o")
        process_total = $procRows.Count
        process_recommended = @($procRows | Where-Object { $_.Recommended }).Count
        startup_total = $startupRows.Count
        startup_orphaned = @($startupRows | Where-Object { -not $_.Exists }).Count
        top_memory_candidates = @(
            $procRows |
                Where-Object { $_.Recommended } |
                Sort-Object MemoryMB -Descending |
                Select-Object -First 10
        )
        orphaned_startup_entries = @(
            $startupRows |
                Where-Object { -not $_.Exists } |
                Select-Object Location, Name, Command, TargetPath
        )
    }
    $output | ConvertTo-Json -Depth 8
    exit 0
}

if ($RunTempCleanup -or $RunMemoryOptimize -or $RemoveOrphanedStartup -or $StopLowRiskUtilities -or $StopBrowsers) {
    $actions = [ordered]@{
        completed_at = (Get-Date).ToString("o")
    }
    if ($RemoveOrphanedStartup) {
        $actions.remove_orphaned_startup = Remove-OrphanedStartupEntries
    }
    if ($RunTempCleanup) {
        $actions.temp_cleanup = Clear-TempFiles
    }
    if ($RunMemoryOptimize) {
        $actions.memory_optimize = Optimize-MemoryWorkingSet
    }
    if ($StopLowRiskUtilities) {
        $actions.stop_low_risk_utilities = Stop-LowRiskUtilityProcesses
    }
    if ($StopBrowsers) {
        $actions.stop_browsers = Stop-BrowserProcesses
    }
    [pscustomobject]$actions | ConvertTo-Json -Depth 8
    exit 0
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Windows Optimizer (Safe Mode)"
$form.Size = New-Object System.Drawing.Size(1220, 760)
$form.StartPosition = "CenterScreen"

$lblProc = New-Object System.Windows.Forms.Label
$lblProc.Text = "Background Processes (safe candidates highlighted by 'Recommended')"
$lblProc.Location = New-Object System.Drawing.Point(12, 12)
$lblProc.Size = New-Object System.Drawing.Size(560, 24)
$form.Controls.Add($lblProc)

$lblStartup = New-Object System.Windows.Forms.Label
$lblStartup.Text = "Startup / Registry Run Entries (orphaned entries can be cleaned)"
$lblStartup.Location = New-Object System.Drawing.Point(610, 12)
$lblStartup.Size = New-Object System.Drawing.Size(560, 24)
$form.Controls.Add($lblStartup)

$gridProc = New-Object System.Windows.Forms.DataGridView
$gridProc.Location = New-Object System.Drawing.Point(12, 36)
$gridProc.Size = New-Object System.Drawing.Size(580, 420)
$gridProc.ReadOnly = $true
$gridProc.SelectionMode = "FullRowSelect"
$gridProc.MultiSelect = $false
$gridProc.AutoSizeColumnsMode = "Fill"
$form.Controls.Add($gridProc)

$gridStartup = New-Object System.Windows.Forms.DataGridView
$gridStartup.Location = New-Object System.Drawing.Point(610, 36)
$gridStartup.Size = New-Object System.Drawing.Size(580, 420)
$gridStartup.ReadOnly = $true
$gridStartup.SelectionMode = "FullRowSelect"
$gridStartup.MultiSelect = $false
$gridStartup.AutoSizeColumnsMode = "Fill"
$form.Controls.Add($gridStartup)

$btnScan = New-Object System.Windows.Forms.Button
$btnScan.Text = "Scan"
$btnScan.Location = New-Object System.Drawing.Point(12, 468)
$btnScan.Size = New-Object System.Drawing.Size(140, 32)
$form.Controls.Add($btnScan)

$btnStop = New-Object System.Windows.Forms.Button
$btnStop.Text = "Stop Selected Process"
$btnStop.Location = New-Object System.Drawing.Point(158, 468)
$btnStop.Size = New-Object System.Drawing.Size(180, 32)
$form.Controls.Add($btnStop)

$btnDisableStartup = New-Object System.Windows.Forms.Button
$btnDisableStartup.Text = "Disable Selected Startup"
$btnDisableStartup.Location = New-Object System.Drawing.Point(344, 468)
$btnDisableStartup.Size = New-Object System.Drawing.Size(180, 32)
$form.Controls.Add($btnDisableStartup)

$btnCleanOrphans = New-Object System.Windows.Forms.Button
$btnCleanOrphans.Text = "Clean Orphaned Startup"
$btnCleanOrphans.Location = New-Object System.Drawing.Point(530, 468)
$btnCleanOrphans.Size = New-Object System.Drawing.Size(180, 32)
$form.Controls.Add($btnCleanOrphans)

$btnTemp = New-Object System.Windows.Forms.Button
$btnTemp.Text = "Clean Temp Files"
$btnTemp.Location = New-Object System.Drawing.Point(716, 468)
$btnTemp.Size = New-Object System.Drawing.Size(150, 32)
$form.Controls.Add($btnTemp)

$btnMemory = New-Object System.Windows.Forms.Button
$btnMemory.Text = "Optimize Memory"
$btnMemory.Location = New-Object System.Drawing.Point(872, 468)
$btnMemory.Size = New-Object System.Drawing.Size(150, 32)
$form.Controls.Add($btnMemory)

$btnExport = New-Object System.Windows.Forms.Button
$btnExport.Text = "Export Scan Report"
$btnExport.Location = New-Object System.Drawing.Point(1028, 468)
$btnExport.Size = New-Object System.Drawing.Size(162, 32)
$form.Controls.Add($btnExport)

$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Location = New-Object System.Drawing.Point(12, 510)
$logBox.Size = New-Object System.Drawing.Size(1178, 205)
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = "Vertical"
$form.Controls.Add($logBox)

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    $logBox.AppendText($line + [Environment]::NewLine)
}

function Refresh-GridData {
    $procRows = Get-ProcessInventory
    $startupRows = Get-StartupEntries
    $gridProc.DataSource = $null
    $gridStartup.DataSource = $null
    $gridProc.DataSource = @($procRows)
    $gridStartup.DataSource = @($startupRows)
    Write-Log ("Scan complete. Processes={0}, Recommended={1}, StartupEntries={2}, Orphaned={3}" -f `
        $procRows.Count, `
        @($procRows | Where-Object { $_.Recommended }).Count, `
        $startupRows.Count, `
        @($startupRows | Where-Object { -not $_.Exists }).Count)
}

$btnScan.Add_Click({
    Refresh-GridData
})

$btnStop.Add_Click({
    if ($gridProc.SelectedRows.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select a process first.")
        return
    }
    $row = $gridProc.SelectedRows[0].DataBoundItem
    if (Is-CriticalProcessName -Name $row.Name) {
        [System.Windows.Forms.MessageBox]::Show("That process is protected and cannot be stopped.")
        return
    }
    $confirm = [System.Windows.Forms.MessageBox]::Show(
        "Stop process '$($row.Name)' (PID $($row.Id))?",
        "Confirm Stop",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    try {
        Stop-Process -Id ([int]$row.Id) -Force -ErrorAction Stop
        Write-Log ("Stopped process {0} ({1})." -f $row.Name, $row.Id)
    } catch {
        Write-Log ("Failed to stop process {0} ({1}): {2}" -f $row.Name, $row.Id, $_.Exception.Message)
    }
    Refresh-GridData
})

$btnDisableStartup.Add_Click({
    if ($gridStartup.SelectedRows.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select a startup entry first.")
        return
    }
    $row = $gridStartup.SelectedRows[0].DataBoundItem
    $confirm = [System.Windows.Forms.MessageBox]::Show(
        "Disable startup entry '$($row.Name)' from '$($row.Location)'?",
        "Confirm Disable",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    try {
        $backup = Disable-StartupEntry -Entry $row
        Write-Log ("Disabled startup entry {0}. Backup={1}" -f $row.Name, $backup)
    } catch {
        Write-Log ("Failed to disable startup entry {0}: {1}" -f $row.Name, $_.Exception.Message)
    }
    Refresh-GridData
})

$btnCleanOrphans.Add_Click({
    $rows = @($gridStartup.DataSource)
    $orphans = @($rows | Where-Object { -not $_.Exists })
    if ($orphans.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("No orphaned startup entries found.")
        return
    }
    $confirm = [System.Windows.Forms.MessageBox]::Show(
        ("Remove {0} orphaned startup entries from registry?" -f $orphans.Count),
        "Confirm Registry Cleanup",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    $removed = 0
    $failed = 0
    foreach ($entry in $orphans) {
        try {
            $backup = Disable-StartupEntry -Entry $entry
            $removed++
            Write-Log ("Removed orphaned startup entry {0}. Backup={1}" -f $entry.Name, $backup)
        } catch {
            $failed++
            Write-Log ("Failed orphan cleanup for {0}: {1}" -f $entry.Name, $_.Exception.Message)
        }
    }
    Write-Log ("Orphan cleanup complete. Removed={0}, Failed={1}" -f $removed, $failed)
    Refresh-GridData
})

$btnTemp.Add_Click({
    try {
        $result = Clear-TempFiles
        Write-Log ("Temp cleanup complete. Deleted={0}, Failed={1}" -f $result.deleted_items, $result.failed_items)
    } catch {
        Write-Log ("Temp cleanup failed: " + $_.Exception.Message)
    }
})

$btnMemory.Add_Click({
    try {
        $result = Optimize-MemoryWorkingSet
        Write-Log ("Memory optimization complete. Trimmed={0}, Failed={1}" -f $result.trimmed_processes, $result.failed_processes)
    } catch {
        Write-Log ("Memory optimization failed: " + $_.Exception.Message)
    }
})

$btnExport.Add_Click({
    try {
        $reportRoot = Join-Path $env:LOCALAPPDATA "SMiTools\OptimizerReports"
        New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $path = Join-Path $reportRoot "windows-optimizer-report-$stamp.json"
        $report = [pscustomobject]@{
            created_at = (Get-Date).ToString("o")
            processes = @($gridProc.DataSource)
            startup_entries = @($gridStartup.DataSource)
        }
        $report | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding ascii
        Write-Log ("Report exported: $path")
    } catch {
        Write-Log ("Export failed: " + $_.Exception.Message)
    }
})

$form.Add_Shown({
    Write-Log "Windows Optimizer loaded in safe mode."
    Write-Log "Registry cleanup is restricted to orphaned startup entries only."
    Refresh-GridData
})

[void]$form.ShowDialog()
