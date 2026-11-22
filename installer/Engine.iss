; Private engine embedded by the native WPF bootstrapper. Compile only; the
; build script never executes this EXE. No wildcard payload inclusion is used.
#if VER != 0x06070300
  #error Inno Setup 6.7.3 is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef PayloadFiles
  #error PayloadFiles is required
#endif
#ifndef PayloadCount
  #error PayloadCount is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppFileVersion
  #error AppFileVersion is required
#endif
#ifndef AppSupportURL
  #error AppSupportURL is required
#endif
#ifndef AgreementVersion
  #error AgreementVersion is required
#endif

[Setup]
AppId={{D82EA01A-D414-4393-BE4D-7AD3371671C0}
AppName=ZZZwallpaper
AppVersion={#AppVersion}
AppVerName=ZZZwallpaper {#AppDisplayVersion}
AppPublisher=人鱼子十
AppSupportURL={#AppSupportURL}
VersionInfoVersion={#AppFileVersion}
VersionInfoCompany=人鱼子十
VersionInfoDescription=ZZZwallpaper 安装引擎
DefaultDirName={localappdata}\Programs\ZZZwallpaper
DefaultGroupName=ZZZwallpaper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=ZZZwallpaper-Engine
SetupIconFile={#PayloadDir}\Assets\Branding\App.ico
UninstallDisplayIcon={app}\ZZZwallpaper.Launcher.exe
UninstallDisplayName=ZZZwallpaper
LicenseFile={#PayloadDir}\legal\UserAgreement.txt
WizardStyle=modern dark
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=no
CloseApplications=no
RestartApplications=no
UsePreviousTasks=no
UsePreviousAppDir=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
AllowCancelDuringInstall=yes
AllowRootDirectory=no
AllowUNCPath=no
SetupMutex=Local\ZZZwallpaper.Engine

[Languages]
Name: "zhCN"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl,Messages.zh-CN.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\Widgets\ZZZwallpaper.WarehouseMedia.dll"
Type: files; Name: "{app}\Assets\Particles\polychrome.png"
Type: files; Name: "{app}\Assets\Particles\master-tape.png"
Type: files; Name: "{app}\Assets\Particles\encrypted-master-tape.png"
Type: files; Name: "{app}\Assets\Particles\denny.png"
Type: files; Name: "{app}\Assets\Particles\gear-coin.png"
Type: files; Name: "{app}\Assets\Particles\boopon.png"
Type: files; Name: "{app}\Assets\Particles\ember-emblem.png"
Type: dirifempty; Name: "{app}\Assets\Particles"
Type: files; Name: "{app}\ExpansionPacks\base\ZZZwallpaper.WarehouseMedia.dll"

[Files]
#include PayloadFiles

[Icons]
Name: "{userprograms}\ZZZwallpaper"; Filename: "{app}\ZZZwallpaper.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ZZZwallpaper.Launcher.exe"; AppUserModelID: "ZZZwallpaper.Desktop"
Name: "{userdesktop}\ZZZwallpaper"; Filename: "{app}\ZZZwallpaper.Launcher.exe"; WorkingDir: "{app}"; IconFilename: "{app}\ZZZwallpaper.Launcher.exe"; AppUserModelID: "ZZZwallpaper.Desktop"; Tasks: desktopicon

[Code]
var
  StatusFile, CancelFile, ErrorCode, CurrentFile, CurrentStage: String;
  LastPercent, CompletedFiles: Integer;
  CancelRequested: Boolean;

function MoveFileEx(ExistingFile, NewFile: String; Flags: Cardinal): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';

procedure WriteStatus(Percent: Integer);
var
  Contents: String;
begin
  LastPercent := Percent;
  if StatusFile = '' then Exit;
  Contents := '[State]' + #13#10 + 'basisPoints=' + IntToStr(Percent) + #13#10 +
    'error=' + ErrorCode + #13#10 + 'stage=' + CurrentStage + #13#10 +
    'file=' + CurrentFile + #13#10 + 'completed=' + IntToStr(CompletedFiles) + #13#10 + 'total={#PayloadCount}' + #13#10;
  if SaveStringToFile(StatusFile + '.tmp', Utf8Encode(Contents), False) then
    MoveFileEx(StatusFile + '.tmp', StatusFile, 9);
end;

procedure BeforePayloadFile(Name: String);
begin
  CurrentFile := Name;
  CurrentStage := 'files';
  WriteStatus(LastPercent);
end;

procedure AfterPayloadFile();
begin
  CompletedFiles := CompletedFiles + 1;
  WriteStatus(Round(CompletedFiles / {#PayloadCount} * 10000));
end;

function InitializeSetup(): Boolean;
begin
  StatusFile := ExpandConstant('{param:STATUSFILE|}');
  CancelFile := ExpandConstant('{param:CANCELFILE|}');
  ErrorCode := '';
  LastPercent := 0;
  CompletedFiles := 0;
  CurrentFile := '';
  CurrentStage := 'files';
  Result := WizardSilent and (ExpandConstant('{param:ZZZBOOTSTRAP|}') = '1') and
    (ExpandConstant('{param:AGREEMENT|}') = '{#AgreementVersion}') and (StatusFile <> '') and (CancelFile <> '');
  if not Result then ErrorCode := 'agreement';
  WriteStatus(0);
  if FileExists(CancelFile) then Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingMS, ExistingLS: Cardinal;
  FindRec: TFindRec;
  Occupied: Boolean;
  AppMutex: String;
  TargetVersion: Int64;
begin
  Result := '';
  if FileExists(CancelFile) then begin Result := '安装已取消。'; Exit; end;
  AppMutex := ExpandConstant('{param:APPMUTEX|}');
  if (AppMutex = '') or CheckForMutexes(AppMutex) then
  begin ErrorCode := 'running'; Result := '请先退出 ZZZwallpaper。'; end;
  Occupied := False;
  if FindFirst(ExpandConstant('{app}\*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then Occupied := True;
      until not FindNext(FindRec);
    finally FindClose(FindRec); end;
  end;
  if Occupied and not (FileExists(ExpandConstant('{app}\install-identity.json')) and FileExists(ExpandConstant('{app}\unins000.exe'))) then
  begin ErrorCode := 'occupied'; Result := '请选择空文件夹或本软件的安装目录。'; end;
  if StrToVersion('{#AppFileVersion}', TargetVersion) and GetVersionNumbers(ExpandConstant('{app}\ZZZwallpaper.dll'), ExistingMS, ExistingLS) then
    if ComparePackedVersion(PackVersionNumbers(ExistingMS, ExistingLS), TargetVersion) > 0 then
    begin ErrorCode := 'newer'; Result := '此位置已安装更高版本。'; end;
  WriteStatus(0);
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  Cancel := True;
  Confirm := False;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if MaxProgress > 0 then
  begin
    Percent := Round(CompletedFiles / {#PayloadCount} * 10000);
    if Percent <> LastPercent then WriteStatus(Percent);
  end;
  if not CancelRequested and FileExists(CancelFile) then
  begin
    CancelRequested := True;
    WizardForm.Close; // Normal engine cancellation and rollback; never kill it.
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CurrentFile := '';
    CurrentStage := 'finishing';
    WriteStatus(10000);
  end;
  if CurStep = ssDone then
  begin
    // Keep the engine's normal registration and removal log; change only the
    // interactive entry point after successful installation has committed.
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D82EA01A-D414-4393-BE4D-7AD3371671C0}_is1',
      'UninstallString', '"' + ExpandConstant('{app}\ZZZwallpaper.Uninstall.exe') + '" --uninstall');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D82EA01A-D414-4393-BE4D-7AD3371671C0}_is1', 'QuietUninstallString');
    CurrentFile := '';
    CurrentStage := 'done';
    WriteStatus(10000);
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  StopGuard: String;
begin
  if ExpandConstant('{param:ZZZUNINSTALL|}') <> '1' then
  begin
    // Also route direct unins000.exe launches through the themed confirmation.
    Exec(ExpandConstant('{app}\ZZZwallpaper.Uninstall.exe'), '--uninstall', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    Result := False;
    Exit;
  end;
  StopGuard := ExpandConstant('{param:STOPGUARD|}');
  Result := UninstallSilent and (StopGuard <> '') and CheckForMutexes(StopGuard);
end;

procedure RemoveOwnedStartupEntry(const ValueName: String);
var
  Command, Expected: String;
begin
  Expected := '"' + ExpandConstant('{app}\ZZZwallpaper.Launcher.exe') + '" --startup';
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', ValueName, Command) then
    if CompareText(Trim(Command), Expected) = 0 then
    begin
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', ValueName);
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', ValueName);
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveOwnedStartupEntry('ZZZwallpaper');
    RemoveOwnedStartupEntry('LumaWall');
    RemoveOwnedStartupEntry('ZZZwallpaper.Modular');
    RemoveOwnedStartupEntry('LumaWall.Modular');
  end;
  if CurUninstallStep = usDone then
    SaveStringToFile(ExpandConstant('{param:DONEFILE|}'), 'done', False);
  // No recursive deletion and no access to the user's LumaWall data directory.
end;
