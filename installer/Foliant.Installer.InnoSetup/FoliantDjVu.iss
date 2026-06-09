; Foliant — DjVu Support (отдельный инсталлятор).
; Ставит GPL-2.0 бинари DjVuLibre (ddjvu/djvused + DLL) в каталог уже установленного Foliant
; ({app}\native\djvulibre), где их первым делом находит DjvuToolset. Основной инсталлятор
; (Foliant.iss) остаётся MIT-чистым; вся GPL-поставка изолирована здесь.
;
; Параметры:
;   /DAppVersion=0.1.0   — версия (VERSION без 'v'), для имени файла и AppVersion.
;
; Сборка: перед ISCC должен существовать native\djvulibre\ с бинарями (tools/fetch-natives.ps1,
; который тянет их по пину из tools/third-party/checksums.json). Без бинарей собирать смысла нет —
; release.yml строит этот инсталлятор только если native\djvulibre\ddjvu.exe присутствует.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#define AppName "Foliant DjVu Support"
#define AppPublisher "Foliant contributors"
#define AppURL "https://github.com/flowa7021-source/Reader"
#define IconRelPath "..\installer-assets\foliant.ico"
; AppId основного Foliant (single-brace форма, как в реестре) — для автодетекта каталога установки.
#define FoliantAppId "{A0F11ANT-0001-0000-0000-000000000001}"

; GPL-2.0 текст берём из самой поставки DjVuLibre (upstream COPYING), а не хардкодим: это
; авторитетный текст лицензии для точной версии бинарей. Пока бандл не загружен (каталог
; native\djvulibre отсутствует), для страницы лицензии мастера используем наш source-offer.
#define DjVuCopying "..\..\native\djvulibre\COPYING"
#if FileExists(DjVuCopying)
  #define DjVuLicenseWizard DjVuCopying
#else
  #define DjVuLicenseWizard "licenses\DJVULIBRE-SOURCE-OFFER.txt"
#endif

[Setup]
; Собственный AppId — отдельная запись в «Программы и компоненты», независимое удаление.
AppId={{B0F11ANT-DJVU-0000-0000-000000000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
; Ставим в каталог уже установленного Foliant (автодетект ниже), без отдельной страницы выбора группы.
DefaultDirName={code:GetFoliantDir}
DisableProgramGroupPage=yes
DisableDirPage=no
; На странице лицензии показываем GPL-2.0 (COPYING из поставки DjVuLibre); если бинари ещё не
; загружены — source-offer как запасной вариант. Пользователь видит, на что соглашается.
LicenseFile={#DjVuLicenseWizard}
OutputDir=Output
OutputBaseFilename=Foliant-DjVu-Support-{#AppVersion}
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

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; GPL-2.0 бинари DjVuLibre → {app}\native\djvulibre (DjvuToolset ищет их там в первую очередь).
; Плоская раскладка ddjvu(.exe)/djvused(.exe) + DLL; *.tar (кэш fetch-natives) исключаем.
Source: "..\..\native\djvulibre\*"; DestDir: "{app}\native\djvulibre"; Excludes: "*.tar"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; GPL-2.0 текст лицензии (обязателен при распространении бинарей, §1 GPL): берём COPYING прямо из
; поставки DjVuLibre и кладём как LICENSE-DjVuLibre.txt — точный текст для данной версии. Плюс наш
; written offer (§3(b)) на получение исходников.
Source: "..\..\native\djvulibre\COPYING"; DestDir: "{app}\Licenses"; DestName: "LICENSE-DjVuLibre.txt"; Flags: ignoreversion skipifsourcedoesntexist
Source: "licenses\DJVULIBRE-SOURCE-OFFER.txt"; DestDir: "{app}\Licenses"; Flags: ignoreversion

[Code]
{ Каталог установленного Foliant: читаем InstallLocation из uninstall-ключа основного приложения
  (Inno пишет ключ AppId + "_is1" в 64-битный HKLM). Если Foliant не найден — дефолт {autopf}\Foliant. }
function GetFoliantDir(Param: String): String;
var
  Loc: String;
begin
  if RegQueryStringValue(HKLM64,
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#FoliantAppId}_is1',
       'InstallLocation', Loc) and (Loc <> '') then
    Result := RemoveBackslashUnlessRoot(Loc)
  else
    Result := ExpandConstant('{autopf}\Foliant');
end;

procedure InitializeWizard();
var
  Loc: String;
begin
  { Мягкое предупреждение, если основной Foliant не обнаружен: установку не блокируем
    (пользователь мог поставить Foliant в нестандартный каталог или ставит компоненты в другом
    порядке), но подсказываем выбрать каталог Foliant на следующей странице. }
  if not (RegQueryStringValue(HKLM64,
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#FoliantAppId}_is1',
       'InstallLocation', Loc) and (Loc <> '')) then
  begin
    MsgBox('Установка Foliant не обнаружена. Если Foliant установлен в нестандартный каталог, ' +
           'укажите его на следующем шаге — поддержка DjVu будет добавлена именно туда.',
           mbInformation, MB_OK);
  end;
end;
