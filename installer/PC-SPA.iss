#ifndef SourceRoot
  #define SourceRoot "..\artifacts\publish\win-x64"
#endif

#define AppVersion "1.0.0"

[Setup]
AppId={{CB5828DE-8E0C-45AE-B3F3-4ADCC907B40B}
AppName=PC-SPA
AppVersion={#AppVersion}
AppVerName=PC-SPA {#AppVersion}
AppPublisher=PC-SPA
AppPublisherURL=https://getpcspa.com
AppSupportURL=https://getpcspa.com/support
DefaultDirName={autopf}\PC-SPA
DefaultGroupName=PC-SPA
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=PC-SPA-1.0.0-win-x64-setup
SetupIconFile=..\src\SystemPerformanceAccelerator.Desktop\Assets\Branding\PC-SPA-Taskbar.ico
UninstallDisplayIcon={app}\PC-SPA.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
ChangesAssociations=no
VersionInfoVersion=1.0.0.0
VersionInfoProductVersion=1.0.0.0
VersionInfoCompany=PC-SPA
VersionInfoDescription=PC-SPA Windows installer
VersionInfoCopyright=Copyright (C) 2026 PC-SPA

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PC-SPA"; Filename: "{app}\PC-SPA.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PC-SPA"; Filename: "{app}\PC-SPA.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\PC-SPA.exe"; Description: "Launch PC-SPA"; Verb: "runas"; Flags: nowait postinstall skipifsilent shellexec
