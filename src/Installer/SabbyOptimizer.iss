; Sabby Optimizer 0.23.23 installer definition (Inno Setup 7/6)
#ifndef AppVersion
  #define AppVersion "0.23.23"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef AppIcon
  #define AppIcon "..\SabbyOptimizer\Resources\AppIcon.ico"
#endif

[Setup]
AppId={{FBFFCC5A-2D54-487E-BE00-5E63338E580D}
AppName=Sabby Optimizer
AppVersion={#AppVersion}
AppVerName=Sabby Optimizer {#AppVersion}
AppPublisher=K13G
DefaultDirName={autopf}\Sabby Optimizer
DefaultGroupName=Sabby Optimizer
UninstallDisplayIcon={app}\SabbyOptimizer.exe
OutputDir={#OutputDir}
OutputBaseFilename=SabbyOptimizer-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\SabbyOptimizer.MainInstance
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Sabby Optimizer
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=Sabby Optimizer installer
SetupLogging=yes
#if FileExists(AppIcon)
SetupIconFile={#AppIcon}
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sabby Optimizer"; Filename: "{app}\SabbyOptimizer.exe"
Name: "{autodesktop}\Sabby Optimizer"; Filename: "{app}\SabbyOptimizer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
; SabbyOptimizer.exe has requireAdministrator in its application manifest.
; Use ShellExecute + the runas verb for BOTH normal installs and silent app-driven updates.
; This avoids CreateProcess error 740 ("The requested operation requires elevation").
Filename: "{app}\SabbyOptimizer.exe"; Description: "Launch Sabby Optimizer"; Verb: "runas"; Flags: shellexec nowait postinstall skipifsilent; Check: not IsSabbyUpdate
Filename: "{app}\SabbyOptimizer.exe"; Parameters: "--post-update"; Verb: "runas"; Flags: shellexec nowait; Check: IsSabbyUpdate

[Code]
function IsSabbyUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:SABBYUPDATE|0}') = '1';
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { User settings/data are intentionally preserved under Local AppData. }
  end;
end;
