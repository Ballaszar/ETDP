# Windows Optimizer (Safe Mode)

This utility provides **safe** Windows cleanup and optimization actions:

- Scan background processes and flag non-critical candidates.
- Stop selected non-critical process (manual confirmation required).
- Scan startup registry entries.
- Disable selected startup entry (with JSON backup).
- Clean orphaned startup registry entries only.
- Clean temp files (`%TEMP%`, `%WINDIR%\Temp`).
- Trigger memory optimization (working set trim for non-critical processes).

## Launch

- Desktop shortcut:
  - `Windows Optimizer (Safe).lnk`
- Desktop shortcut (elevated):
  - `Windows Optimizer (Admin).lnk`
- Start Menu shortcut:
  - `Windows Optimizer (Safe).lnk`
- Start Menu shortcut (elevated):
  - `Windows Optimizer (Admin).lnk`
- Manual browser-stop shortcut:
  - `Stop Browsers (Edge+Chrome).lnk`
- Direct command:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\scripts\windows_optimizer_app.ps1
  ```

Admin launcher (UAC prompt):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\launch_windows_optimizer_admin.ps1
```

## Headless Scan

Use this for a non-GUI report:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows_optimizer_app.ps1 -HeadlessScan
```

Browser stop action (manual trigger):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\windows_optimizer_app.ps1 -StopBrowsers
```

Elevated browser-stop wrapper:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\launch_windows_optimizer_admin.ps1 -StopBrowsers
```

## Backup Location

Startup-entry backups are written to:

- `%LOCALAPPDATA%\SMiTools\StartupRegistryBackups`

## Safety Notes

- Registry cleanup is intentionally limited to startup entries, especially orphaned values.
- Critical Windows processes are protected from stop/trim actions.
- Always review recommended candidates before stopping or disabling anything.
