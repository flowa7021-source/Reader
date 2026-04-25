; Foliant — Inno Setup script (placeholder для Phase 0).
; Финальный multi-tier (Basic / Standard / Full) — спринт S13.
; Передаваемые параметры:
;   /DAppVersion=0.1.0   — версия из git tag
;   /DTier=Basic         — Basic | Standard | Full

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#ifndef Tier
  #define Tier "Basic"
#endif

#define AppName "Foliant"
#define AppPublisher "Foliant contributors"
#define AppURL "https://github.com/flowa7021-source/Reader"
#define AppExeName "Foliant.exe"

[Setup]
AppId={{A0F11ANT-0001-0000-0000-000000000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=Output
OutputBaseFilename=Foliant-Setup-{#AppVersion}-{#Tier}
SetupIconFile=..\installer-assets\foliant.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19044
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Phase 0 placeholder. После S13 здесь будут:
;   - Self-contained published binaries (publish/*)
;   - Native libraries (native/pdfium, native/tesseract, ...)
;   - Tessdata models по выбранному tier'у
;   - Лицензии третьих лиц (Licenses/)
; Source: "..\..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; ВАЖНО: %LOCALAPPDATA%\Foliant\ и %APPDATA%\Foliant\ НЕ удаляем по умолчанию.
; Спрашиваем у пользователя через [Code] секцию (добавим в S13).
