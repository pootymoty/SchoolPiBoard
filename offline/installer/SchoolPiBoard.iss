; Установщик «Доска Пи» (Inno Setup 6).
;
; Приложение публикуется self-contained: рантайм .NET лежит внутри exe,
; поэтому установщику нечего докачивать и он работает без интернета.
; Если когда-нибудь перейдём на framework-dependent сборку, сюда добавится
; проверка наличия .NET Desktop Runtime — как это сделать, описано в README.

; AppName — то, что видит пользователь (заголовки, ярлыки, реестр программ).
; AppDirName — технический идентификатор латиницей, только для пути
; установки в Program Files; сам exe тоже называется по нему (AssemblyName
; в SchoolPiBoard.csproj даёт DoskaPi.exe при сборке).
#define AppName "Доска Пи"
#define AppDirName "DoskaPi"
#define AppVersion "2.2.1"
#define AppPublisher "Урвачев Роман Сергеевич"
#define AppUrl "https://school-pi.online"
#define AppSupportEmail "info@school-pi.online"
#define AppExe "DoskaPi.exe"

; Папка с результатом `dotnet publish` (см. build-installer.bat).
#define SourceDir "..\publish"

[Setup]
; AppId не меняется вместе с переименованием — он опознаёт уже
; установленные копии SchoolPiBoard/«Доска Пи» как одну и ту же программу,
; иначе обновление ставилось бы рядом со старой, а не поверх неё.
AppId={{8B0E5F4C-6E4B-4E2A-9A1D-6F2C5D3A7B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
AppContact={#AppSupportEmail}
AppCopyright={#AppPublisher}

; Сведения, которые Windows показывает в свойствах файла установщика.
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Установка {#AppName}
VersionInfoProductName={#AppName}

DefaultDirName={autopf}\{#AppDirName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}

; Обычный мастер: приветствие, соглашение, пояснения, выбор папки, ярлыки.
; Inno 6 по умолчанию пропускает страницу приветствия — возвращаем её,
; без неё установщик выглядит непривычно урезанным.
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=no
AllowNoIcons=yes

; Если приложение запущено, установщик предложит закрыть его сам,
; а не упрётся в занятый файл.
CloseApplications=yes
RestartApplications=yes

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
; Имя без версии: ссылка на скачивание на сайте остаётся одной и той же
; от выпуска к выпуску. Версия видна в свойствах файла и в мастере.
OutputBaseFilename=SchoolPiBoardSetup
SetupIconFile=..\doskapi.ico
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

[InstallDelete]
; Обновление поверх старой установки: до переименования главный файл
; назывался SchoolPiBoard.exe — теперь он лишний.
Type: files; Name: "{app}\SchoolPiBoard.exe"

[UninstallDelete]
; Доски пользователя (%APPDATA%\DoskaPi) намеренно не удаляются:
; переустановка программы не должна стирать работу.
;
; Метки пробного периода (HKCU\Software\DoskaPi и
; %ProgramData%\DoskaPi) тоже остаются — иначе «удалить и поставить
; заново» превращалось бы в бесконечный бесплатный период.
Type: dirifempty; Name: "{app}"

[Code]
{ Вопросы при удалении: оставить или убрать доски и сохранённые картинки.
  По умолчанию (и в тихом режиме) ничего не удаляется. Адреса папок
  программа сама записывает в реестр (HKCU\Software\DoskaPi\Paths). }

function ReadSavedPath(const Name, Fallback: String): String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Software\DoskaPi\Paths', Name, Result)
     or (Result = '') then
    Result := Fallback;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, ImagesDir: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  DataDir := ReadSavedPath('DataFolder', ExpandConstant('{userappdata}\DoskaPi'));
  if FileExists(DataDir + '\boards.json') then
  begin
    if SuppressibleMsgBox(
         'Удалить сохранённые доски?' + #13#10 + #13#10 +
         'Папка: ' + DataDir + #13#10 + #13#10 +
         'Это необратимо. Если нажать «Нет», доски останутся, и после ' +
         'повторной установки программы вы увидите их снова.',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      DeleteFile(DataDir + '\boards.json');
      DeleteFile(DataDir + '\boards.backup.json');
      DeleteFile(DataDir + '\boards.json.tmp');
      DelTree(DataDir + '\archive', True, True, True);
    end;
  end;

  ImagesDir := ReadSavedPath('ExportDirectory', '');
  { Удаляем только папку «Доска Пи» — чужие файлы в «Изображениях» не задеваем. }
  if (ImagesDir <> '') and (ExtractFileName(ImagesDir) = 'Доска Пи') and DirExists(ImagesDir) then
  begin
    if SuppressibleMsgBox(
         'Удалить сохранённые картинки досок?' + #13#10 + #13#10 +
         'Папка: ' + ImagesDir + #13#10 + #13#10 +
         'Будет удалена вся папка вместе с содержимым. Это необратимо.',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ImagesDir, True, True, True);
  end;
end;
