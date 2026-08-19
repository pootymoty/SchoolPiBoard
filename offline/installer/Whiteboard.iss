; Установщик Whiteboard (Inno Setup 6).
;
; Приложение публикуется self-contained: рантайм .NET лежит внутри exe,
; поэтому установщику нечего докачивать и он работает без интернета.
; Если когда-нибудь перейдём на framework-dependent сборку, сюда добавится
; проверка наличия .NET Desktop Runtime — как это сделать, описано в README.

#define AppName "Whiteboard"
#define AppVersion "2.2.0"
#define AppPublisher "ЗАГЛУШКА: ФИО самозанятого"
#define AppUrl "https://example.com/whiteboard"
#define AppExe "Whiteboard.exe"

; Папка с результатом `dotnet publish` (см. build-installer.bat).
#define SourceDir "..\publish"

[Setup]
AppId={{8B0E5F4C-6E4B-4E2A-9A1D-6F2C5D3A7B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}

; Пользователь выбирает папку и ярлыки сам — это обычный мастер, а не
; «тихая» установка.
DisableDirPage=no
DisableProgramGroupPage=no
AllowNoIcons=yes

; Установка «для всех» требует прав администратора, «только для меня» — нет.
; Учителю без админских прав так тоже есть куда поставить программу.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Тексты соглашения и пояснений показываются до установки.
LicenseFile=LICENSE.txt
InfoBeforeFile=BEFORE.txt

OutputDir=..\dist
OutputBaseFilename=WhiteboardSetup-{#AppVersion}
SetupIconFile=..\whiteboard.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
russian.FinishedLabel=Программа [name] установлена.%n%nКлюч регистрации можно ввести сейчас или позже — при любом запуске программы. Без ключа доступны 3 бесплатных дня.

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные значки:"

[Files]
; Self-contained публикация: один exe со встроенным рантаймом,
; но берём папку целиком — на случай, если рядом появятся файлы.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Последняя страница мастера: ввести ключ сразу. Если галочку снять,
; ключ спросится при первом запуске программы — сценарий «позже».
Filename: "{app}\{#AppExe}"; Parameters: "--activate"; Description: "Запустить {#AppName} и ввести ключ регистрации"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Доски пользователя (%APPDATA%\WhiteboardApp) намеренно не удаляются:
; переустановка программы не должна стирать работу.
;
; Метки пробного периода (HKCU\Software\WhiteboardApp и
; %ProgramData%\WhiteboardApp) тоже остаются — иначе «удалить и поставить
; заново» превращалось бы в бесконечный бесплатный период.
Type: dirifempty; Name: "{app}"
