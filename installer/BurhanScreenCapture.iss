; Burhan Screen Capture Pro — Inno Setup 6 installer script
; Build:  ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\publish\app installer\BurhanScreenCapture.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\app"
#endif

#define AppName "Burhan Screen Capture Pro"
#define AppExe "BurhanScreenCapture.exe"
#define Publisher "Burhan Nexus"
#define Website "https://burhannexus.com"

[Setup]
AppId={{6F1C2D4B-8B5E-4C6A-9E2A-B5C1A7D3E901}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#Website}
AppSupportURL={#Website}
AppUpdatesURL={#Website}
AppCopyright=Copyright (C) 2026 Burhan Nexus
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\Burhan Nexus\Burhan Screen Capture Pro
DefaultGroupName=Burhan Nexus
DisableProgramGroupPage=yes
LicenseFile=..\docs\EULA.txt
OutputDir=..\artifacts
OutputBaseFilename=BurhanScreenCapturePro-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=images\wizard.bmp,images\wizard@2x.bmp
WizardSmallImageFile=images\wizard-small.bmp,images\wizard-small@2x.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
AppMutex=Local\BurhanNexus.ScreenCapturePro
; Code signing (optional): configure a SignTool in the Inno Setup IDE or pass /S on the command line, then uncomment:
; SignTool=burhansign
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Start {#AppName} when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeeded
Source: "..\docs\USER_GUIDE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\EULA.txt"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Capture. Create. Communicate."
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BurhanScreenCapturePro"; ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BurhanScreenCapturePro"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ Runtime…"; Check: VCRedistNeeded; Flags: waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; Program-generated caches only. User screenshots, recordings, settings and license are kept.
Type: filesandordirs; Name: "{localappdata}\Burhan Nexus\Screen Capture Pro\thumbs"
Type: filesandordirs; Name: "{localappdata}\Burhan Nexus\Screen Capture Pro\temp"
Type: filesandordirs; Name: "{localappdata}\Burhan Nexus\Screen Capture Pro\logs"

[Code]
function VCRedistNeeded(): Boolean;
var
  installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', installed) then
    Result := installed <> 1;
end;
