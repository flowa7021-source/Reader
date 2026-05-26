; Foliant — Inno Setup script.
; Multi-tier (Basic / Standard / Full) — спринт S13.
; Передаваемые параметры:
;   /DAppVersion=0.1.0   — версия (обычно git tag, напр. v0.1.0)
;   /DTier=Basic         — Basic | Standard | Full (набор OCR-моделей)
;
; ВНИМАНИЕ: скрипт не собирается и не проверяется на Linux. Валидировать на Windows-стенде с
; установленным Inno Setup 6 (ISCC.exe). Перед сборкой должен существовать каталог publish/
; (см. `dotnet publish` в .github/workflows/release.yml) и, опционально, native/paddleocr/
; с моделями (tools/fetch-natives.ps1). Модели подключены с `skipifsourcedoesntexist`, поэтому
; инсталлятор соберётся и без них (OCR не заработает, пока модели не доставлены).

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
#define IconRelPath "..\installer-assets\foliant.ico"

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
; Иконку подключаем только если файл присутствует — иначе ISCC падает на отсутствующем ресурсе.
#if FileExists(IconRelPath)
SetupIconFile={#IconRelPath}
#endif
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
; 1) Self-contained публикация WPF-приложения (Foliant.exe + managed/native рантайм:
;    PDFiumCore, OpenCvSharp4.runtime.win, PaddleInference win64.mkl, SQLite и т.д.).
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2) Лицензии: основная + третьих лиц (рядом с exe для пункта меню About/legal).
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

; 3) Оффлайн-модели PaddleOCR. Движок ищет их в {app}\native\paddleocr\{det,cls,rec\<script>}
;    (см. PaddleOcrEngine: AppContext.BaseDirectory\native\paddleocr). Общие det+cls и
;    латиница+кириллица — во всех tier'ах; CJK/арабский — только Full.
;    skipifsourcedoesntexist: позволяет собрать инсталлятор, пока модели ещё не доставлены.
Source: "..\..\native\paddleocr\det\*";          DestDir: "{app}\native\paddleocr\det";          Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\cls\*";          DestDir: "{app}\native\paddleocr\cls";          Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\rec\latin\*";    DestDir: "{app}\native\paddleocr\rec\latin";    Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\rec\cyrillic\*"; DestDir: "{app}\native\paddleocr\rec\cyrillic"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
#if Tier == "Full"
Source: "..\..\native\paddleocr\rec\chinese\*";  DestDir: "{app}\native\paddleocr\rec\chinese";  Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\rec\japan\*";    DestDir: "{app}\native\paddleocr\rec\japan";    Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\rec\korean\*";   DestDir: "{app}\native\paddleocr\rec\korean";   Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\..\native\paddleocr\rec\arabic\*";   DestDir: "{app}\native\paddleocr\rec\arabic";   Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ Пользовательские данные (%APPDATA%\Foliant — настройки/лицензия/триал/закладки;
  %LOCALAPPDATA%\Foliant — кэш/логи/аннотации/автосейв) по умолчанию НЕ удаляем.
  При интерактивном удалении спрашиваем явно; в silent-режиме всегда сохраняем. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Roaming, Local: String;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;
  if UninstallSilent then
    exit;

  if MsgBox('Удалить пользовательские данные Foliant (настройки, лицензию, кэш, аннотации, закладки)?',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    Roaming := ExpandConstant('{userappdata}\Foliant');
    Local := ExpandConstant('{localappdata}\Foliant');
    DelTree(Roaming, True, True, True);
    DelTree(Local, True, True, True);
  end;
end;
