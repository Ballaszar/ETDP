param(
    [string]$ShortcutBaseName = "Windows Optimizer (Safe)",
    [string]$AdminShortcutBaseName = "Windows Optimizer (Admin)",
    [string]$BrowserStopShortcutBaseName = "Stop Browsers (Edge+Chrome)"
)

$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

$appScript = Join-Path $PSScriptRoot "windows_optimizer_app.ps1"
if (!(Test-Path $appScript)) {
    throw "Optimizer script not found: $appScript"
}
$adminLauncherScript = Join-Path $PSScriptRoot "launch_windows_optimizer_admin.ps1"
if (!(Test-Path $adminLauncherScript)) {
    throw "Admin launcher script not found: $adminLauncherScript"
}

$desktopDir = [Environment]::GetFolderPath("Desktop")
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

$powershellExe = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
if (!(Test-Path $powershellExe)) {
    $powershellExe = "powershell.exe"
}

$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$appScript`""
$adminArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$adminLauncherScript`""

function New-OptimizerShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetArguments,
        [string]$Description
    )
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $powershellExe
    $shortcut.Arguments = $TargetArguments
    $shortcut.WorkingDirectory = (Split-Path -Parent $appScript)
    $shortcut.Description = $Description
    $shortcut.IconLocation = "$powershellExe,0"
    $shortcut.Save()
}

$desktopShortcut = Join-Path $desktopDir "$ShortcutBaseName.lnk"
$startMenuShortcut = Join-Path $startMenuDir "$ShortcutBaseName.lnk"
$desktopAdminShortcut = Join-Path $desktopDir "$AdminShortcutBaseName.lnk"
$startMenuAdminShortcut = Join-Path $startMenuDir "$AdminShortcutBaseName.lnk"
$desktopBrowserStopShortcut = Join-Path $desktopDir "$BrowserStopShortcutBaseName.lnk"
$startMenuBrowserStopShortcut = Join-Path $startMenuDir "$BrowserStopShortcutBaseName.lnk"

New-OptimizerShortcut -ShortcutPath $desktopShortcut -TargetArguments $arguments -Description "Launch Windows Optimizer in safe mode."
New-OptimizerShortcut -ShortcutPath $startMenuShortcut -TargetArguments $arguments -Description "Launch Windows Optimizer in safe mode."
New-OptimizerShortcut -ShortcutPath $desktopAdminShortcut -TargetArguments $adminArguments -Description "Launch Windows Optimizer with elevation (Run as administrator)."
New-OptimizerShortcut -ShortcutPath $startMenuAdminShortcut -TargetArguments $adminArguments -Description "Launch Windows Optimizer with elevation (Run as administrator)."
New-OptimizerShortcut -ShortcutPath $desktopBrowserStopShortcut -TargetArguments ($adminArguments + " -StopBrowsers") -Description "Stop all Microsoft Edge and Google Chrome processes now."
New-OptimizerShortcut -ShortcutPath $startMenuBrowserStopShortcut -TargetArguments ($adminArguments + " -StopBrowsers") -Description "Stop all Microsoft Edge and Google Chrome processes now."

Write-Host "Windows Optimizer shortcuts created."
Write-Host "Desktop: $desktopShortcut"
Write-Host "Start Menu: $startMenuShortcut"
Write-Host "Desktop (Admin): $desktopAdminShortcut"
Write-Host "Start Menu (Admin): $startMenuAdminShortcut"
Write-Host "Desktop (Stop Browsers): $desktopBrowserStopShortcut"
Write-Host "Start Menu (Stop Browsers): $startMenuBrowserStopShortcut"
