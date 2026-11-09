; Burhan Nexus License Key Generator — ADMIN TOOL installer (do not distribute to customers)
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\generator"
#endif
#define AppName "Burhan Nexus License Key Generator"
#define AppExe "BurhanNexusLicenseKeyGenerator.exe"

[Setup]
AppId={{9C3B1E77-4A2F-4D8B-A6E1-2F5D7C9B0A13}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Burhan Nexus
AppCopyright=Copyright (C) 2026 Burhan Nexus
DefaultDirName={autopf}\Burhan Nexus\License Key Generator
DefaultGroupName=Burhan Nexus
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=BurhanNexusLicenseKeyGenerator-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
WizardImageFile=images\wizard.bmp,images\wizard@2x.bmp
WizardSmallImageFile=images\wizard-small.bmp,images\wizard-small@2x.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The license database and encrypted signing key in %AppData%\Burhan Nexus\License Key Generator are never deleted.
