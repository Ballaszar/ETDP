#ifndef MyAppName
  #define MyAppName "ETDP"
#endif

#ifndef MyAppPublisher
  #define MyAppPublisher "ETDP"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "2026.3.17"
#endif

#ifndef BundleSourceDir
  #define BundleSourceDir "E:\ETDP\artifacts\portable\stack-win-x64"
#endif

#ifndef SetupIconFile
  #define SetupIconFile "E:\ETDP\ETDP\installer\assets\etdp.ico"
#endif

[Setup]
AppId={{5D09E953-2D9D-469E-A0AB-593CF53AF1E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\ETDP
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=.
OutputBaseFilename=ETDP-Setup
SetupIconFile={#SetupIconFile}
UninstallDisplayIcon={app}\assets\etdp.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#BundleSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Launch ETDP"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\start_etdp_only.ps1"" -Mode Runtime -OpenBrowser"; WorkingDir: "{app}"; IconFilename: "{app}\assets\etdp.ico"; Comment: "Launch ETDP"
Name: "{group}\ETDP Status"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\status_etdp_only.ps1"""; WorkingDir: "{app}"; IconFilename: "{app}\assets\etdp.ico"; Comment: "Check ETDP runtime status"
Name: "{group}\Stop ETDP"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\stop_etdp_only.ps1"""; WorkingDir: "{app}"; IconFilename: "{app}\assets\etdp.ico"; Comment: "Stop the ETDP runtime"
Name: "{autodesktop}\Launch ETDP"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\start_etdp_only.ps1"" -Mode Runtime -OpenBrowser"; WorkingDir: "{app}"; IconFilename: "{app}\assets\etdp.ico"; Tasks: desktopicon; Comment: "Launch ETDP"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\start_etdp_only.ps1"" -Mode Runtime -OpenBrowser"; WorkingDir: "{app}"; Description: "Launch ETDP now"; Flags: nowait postinstall skipifsilent
