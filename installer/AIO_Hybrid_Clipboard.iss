; Inno Setup 6 script for AIO Hybrid Clipboard
;
; Build order (see README "How to Build"):
;   1. msbuild AIO_SearchEngine (Release|x64)
;   2. dotnet publish the WPF app (Release, win-x64, self-contained)
;   3. copy AIO_SearchEngine.dll into the publish folder
;   4. compile this script with ISCC.exe
;
; The app is installed per-user (no admin rights / UAC prompt). This is
; required: the app writes its AIO_Cache folder next to the executable,
; which would fail under Program Files.

#define MyAppName "AIO Hybrid Clipboard"
; Overridable from the command line: iscc /DMyAppVersion=1.5.0 ...
#ifndef MyAppVersion
  #define MyAppVersion "1.6.0"
#endif
#define MyAppPublisher "Samet Kasmer"
#define MyAppURL "https://github.com/Layellie/AIO-Hybrid-Clipboard"
#define MyAppExeName "AIO_Hybrid_Clipboard.exe"
#define PublishDir "..\AIO Clipboard & Search\bin\x64\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{4E83288A-D120-47DC-9A51-CC5FF0247546}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=AIO_Hybrid_Clipboard_Setup_v{#MyAppVersion}
SetupIconFile=..\AIO Clipboard & Search\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Runtime screenshot/session cache created next to the exe.
Type: filesandordirs; Name: "{app}\AIO_Cache"
