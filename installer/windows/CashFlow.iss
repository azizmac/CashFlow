; Установщик CashFlow для Windows (Inno Setup 6.1+).
; Сборка: tools/publish-desktop.ps1 публикует приложение в installer/windows/publish и вызывает ISCC на этот скрипт.
; Приложение ставится в Program Files, данные (server.json, ключ шифрования, кластер PostgreSQL) — в %LOCALAPPDATA%.
; Компонент «Локальная база PostgreSQL» скачивает официальные бинарники PostgreSQL с сайта EDB и распаковывает их в {app}\pgsql —
; тогда приложение работает без Docker: кластер создаётся при первом запуске в папке данных.

#define AppName "CashFlow"
#define AppPublisher "CashFlow AI"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif
#define PgVersion "16.4-1"
#define PgZipUrl "https://get.enterprisedb.com/postgresql/postgresql-" + PgVersion + "-windows-x64-binaries.zip"

[Setup]
AppId={{7C2E8B1A-4F6D-4C1B-9E2A-3C0A5F1E0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=output
OutputBaseFilename=CashFlow-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\CashFlow.Maui.exe

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Приложение и локальная база PostgreSQL (рекомендуется)"
Name: "appOnly"; Description: "Только приложение (PostgreSQL уже есть или Docker)"
Name: "custom"; Description: "Выборочно"; Flags: iscustom

[Components]
Name: "app"; Description: "Приложение CashFlow"; Types: full appOnly custom; Flags: fixed
Name: "pg"; Description: "Локальная база PostgreSQL {#PgVersion} (скачивается при установке, ~330 МБ)"; Types: full custom

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\CashFlow.Maui.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\CashFlow.Maui.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Run]
Filename: "{app}\CashFlow.Maui.exe"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\pgsql"

[Code]
var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and WizardIsComponentSelected('pg') then begin
    DownloadPage.Clear;
    DownloadPage.Add('{#PgZipUrl}', 'postgresql.zip', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if DownloadPage.AbortedByUser then
          Log('Загрузка PostgreSQL отменена пользователем')
        else
          SuppressibleMsgBox('Не удалось скачать PostgreSQL: ' + AddPeriod(GetExceptionMessage) + #13#10 +
            'Приложение установится без локальной базы; укажите строку подключения на экране входа или поставьте PostgreSQL отдельно.', mbError, MB_OK, IDOK);
        Result := True;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Zip, Target: String;
begin
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('pg') then begin
    Zip := ExpandConstant('{tmp}\postgresql.zip');
    Target := ExpandConstant('{app}');
    if FileExists(Zip) then begin
      // В архиве EDB бинарники лежат в папке pgsql: распаковываем прямо в папку приложения, получается pgsql\bin
      Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath ' + #39 + Zip + #39 + ' -DestinationPath ' + #39 + Target + #39 + ' -Force"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        SuppressibleMsgBox('Не удалось распаковать PostgreSQL (код ' + IntToStr(ResultCode) + ').', mbError, MB_OK, IDOK);
    end;
  end;
end;
