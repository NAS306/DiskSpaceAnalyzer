#ifndef PublishDir
  #error PublishDir is required; use build-installer.ps1
#endif
#ifndef OutputDir
  #define OutputDir ".."
#endif

[Setup]
AppId={{7D827938-6AC4-445C-9035-42AB2885499A}
AppName=DiskSpaceAnalyzer
AppVersion=1.0.4
AppPublisher=DiskSpaceAnalyzer
DefaultDirName={localappdata}\Programs\DiskSpaceAnalyzer
DefaultGroupName=DiskSpaceAnalyzer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=DiskSpaceAnalyzer-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DiskSpaceAnalyzer.exe
CloseApplications=yes
RestartApplications=no
UsePreviousLanguage=yes
LanguageDetectionMethod=uilanguage
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
korean.DesktopShortcut=바탕 화면 바로 가기 만들기
english.LaunchApp=Launch DiskSpaceAnalyzer
korean.LaunchApp=DiskSpaceAnalyzer 실행
english.DependencyTitle=Everything search engine
korean.DependencyTitle=Everything 검색 엔진
english.DependencySubtitle=DiskSpaceAnalyzer uses Everything for fast, real-time file indexing.
korean.DependencySubtitle=빠른 실시간 파일 인덱싱에 Everything을 사용합니다.
english.DependencyInfo=Existing Everything installations are reused. If it is missing, Setup first tries WinGet, then the bundled official Everything installer. Installing its indexing service may show a Windows administrator prompt.%n%nThe first index can take up to three minutes. Everything runs in the background; you do not need to operate it.%n%nUninstalling DiskSpaceAnalyzer keeps Everything, your settings, and existing user data.
korean.DependencyInfo=기존 Everything이 있으면 그대로 사용합니다. 없으면 WinGet으로 설치하고, 사용할 수 없거나 실패하면 포함된 공식 Everything 설치 프로그램으로 설치합니다. 인덱싱 서비스 설치 시 Windows 관리자 권한 확인창이 표시될 수 있습니다.%n%n최초 인덱스 준비에는 최대 3분이 걸릴 수 있습니다. Everything은 백그라운드에서 동작하므로 직접 조작할 필요가 없습니다.%n%nDiskSpaceAnalyzer를 제거해도 Everything, 설정, 기존 사용자 데이터은 보존됩니다.
english.DependencyFound=Detected Everything: %1
korean.DependencyFound=기존 Everything 감지: %1
english.DependencyMissing=Everything was not found. It will be installed when you choose Install.
korean.DependencyMissing=Everything이 없습니다. 설치 버튼을 누르면 함께 설치합니다.
english.InstallingDependency=Installing Everything. Please approve the Windows administrator prompt if shown.
korean.InstallingDependency=Everything 설치 중입니다. Windows 관리자 권한 확인창이 나타나면 허용해 주세요.
english.CheckingIndex=Preparing the Everything index (up to 3 minutes)...
korean.CheckingIndex=Everything 인덱스를 준비하고 있습니다(최대 3분)...
english.DependencyFailed=Everything could not be installed or started. Check administrator approval and try Setup again. Details are in the Setup log.
korean.DependencyFailed=Everything을 설치하거나 시작하지 못했습니다. 관리자 권한 허용 여부를 확인한 뒤 설치를 다시 실행해 주세요. 자세한 내용은 설치 로그에 기록됩니다.
english.QueryFailed=Everything service or indexed file search validation failed. Run Setup again and approve the Windows administrator prompt. Details are in the Setup log and %LOCALAPPDATA%\DiskSpaceAnalyzer\EverythingServiceRepair.log.
korean.QueryFailed=Everything 서비스 또는 실제 파일 검색 검증에 실패했습니다. 설치 프로그램을 다시 실행하고 Windows 관리자 권한 확인창을 허용해 주세요. 자세한 내용은 설치 로그 및 %LOCALAPPDATA%\DiskSpaceAnalyzer\EverythingServiceRepair.log에서 확인할 수 있습니다.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "Everything\Everything.exe,*_BU.*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "vendor\Everything-1.4.1.1032.x64-Setup.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\DiskSpaceAnalyzer"; Filename: "{app}\DiskSpaceAnalyzer.exe"
Name: "{autodesktop}\DiskSpaceAnalyzer"; Filename: "{app}\DiskSpaceAnalyzer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DiskSpaceAnalyzer.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  SettingsKey = 'Software\DiskSpaceAnalyzer';
  AppPathsKey = 'Software\Microsoft\Windows\CurrentVersion\App Paths\Everything.exe';
  NotificationClass = 'EVERYTHING_TASKBAR_NOTIFICATION';
  OfficialSHA256 = 'c42efad041d4c0bb4d4ac97ae7cbe89f153ec1fe078772392e749c7f5d5282d3';
var
  DependencyPage: TWizardPage;
  DependencyStatus: TNewStaticText;
  ProgressPage: TOutputProgressWizardPage;
  EnginePath: String;

function GetWindowThreadProcessId(hWnd: HWND; var ProcessId: Cardinal): Cardinal;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenProcess(Access: Cardinal; InheritHandle: Boolean; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function QueryFullProcessImageNameW(Process: THandle; Flags: Cardinal; Path: String; var Size: Cardinal): Boolean;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function ExistingPath(const Candidate: String): String;
begin
  Result := RemoveQuotes(Trim(Candidate));
  if not FileExists(Result) then Result := '';
end;

function FindEverything: String;
var
  Value: String;
  Pid, Size: Cardinal;
  Process: THandle;
  Window: HWND;
begin
  Result := '';
  { Prefer the running compatible default instance, including a portable installation. }
  Window := FindWindowByClassName(NotificationClass);
  if Window <> 0 then begin
    GetWindowThreadProcessId(Window, Pid);
    Process := OpenProcess($1000, False, Pid);
    if Process <> 0 then begin
      Size := 32768; SetLength(Value, Size);
      if QueryFullProcessImageNameW(Process, 0, Value, Size) then begin
        SetLength(Value, Size); Result := ExistingPath(Value);
      end;
      CloseHandle(Process);
    end;
  end;
  if Result <> '' then exit;
  if RegQueryStringValue(HKCU, SettingsKey, 'EverythingPath', Value) then Result := ExistingPath(Value);
  if Result <> '' then exit;
  if RegQueryStringValue(HKCU64, AppPathsKey, '', Value) then Result := ExistingPath(Value);
  if Result <> '' then exit;
  if RegQueryStringValue(HKLM64, AppPathsKey, '', Value) then Result := ExistingPath(Value);
  if Result <> '' then exit;
  if RegQueryStringValue(HKCU32, AppPathsKey, '', Value) then Result := ExistingPath(Value);
  if Result <> '' then exit;
  if RegQueryStringValue(HKLM32, AppPathsKey, '', Value) then Result := ExistingPath(Value);
  if Result <> '' then exit;
  Result := ExistingPath(ExpandConstant('{pf64}\Everything\Everything.exe'));
  if Result = '' then Result := ExistingPath(ExpandConstant('{pf32}\Everything\Everything.exe'));
end;

function InstallEverything: Boolean;
var
  Winget, Installer: String;
  Code: Integer;
begin
  Result := False;
  ProgressPage.SetText(CustomMessage('InstallingDependency'), '');
  Winget := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
  if FileExists(Winget) then begin
    Log('Attempting WinGet dependency installation.');
    if Exec(Winget, 'install --id voidtools.Everything --exact --source winget --architecture x64 --scope machine --installer-type nullsoft --silent --accept-package-agreements --accept-source-agreements --disable-interactivity', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Log(Format('WinGet result: %d', [Code]));
    EnginePath := FindEverything;
    if EnginePath <> '' then begin Result := True; exit; end;
  end;
  Log('Using bundled, hash-verified official Everything installer.');
  ExtractTemporaryFile('Everything-1.4.1.1032.x64-Setup.exe');
  Installer := ExpandConstant('{tmp}\Everything-1.4.1.1032.x64-Setup.exe');
  if CompareText(GetSHA256OfFile(Installer), OfficialSHA256) <> 0 then
    RaiseException('Everything installer SHA-256 mismatch.');
  if not ShellExec('runas', Installer, '/S', '', SW_HIDE, ewWaitUntilTerminated, Code) then exit;
  Log(Format('Official Everything installer result: %d', [Code]));
  if Code <> 0 then exit;
  EnginePath := FindEverything;
  Result := EnginePath <> '';
end;

procedure InitializeWizard;
var
  Info: TNewStaticText;
begin
  DependencyPage := CreateCustomPage(wpSelectDir, CustomMessage('DependencyTitle'), CustomMessage('DependencySubtitle'));
  Info := TNewStaticText.Create(DependencyPage);
  Info.Parent := DependencyPage.Surface;
  Info.SetBounds(0, 0, DependencyPage.SurfaceWidth, ScaleY(160));
  Info.AutoSize := False; Info.WordWrap := True;
  Info.Caption := CustomMessage('DependencyInfo');
  DependencyStatus := TNewStaticText.Create(DependencyPage);
  DependencyStatus.Parent := DependencyPage.Surface;
  DependencyStatus.SetBounds(0, ScaleY(165), DependencyPage.SurfaceWidth, ScaleY(60));
  DependencyStatus.AutoSize := False; DependencyStatus.WordWrap := True;
  ProgressPage := CreateOutputProgressPage(CustomMessage('DependencyTitle'), CustomMessage('CheckingIndex'));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DependencyPage.ID then begin
    EnginePath := FindEverything;
    if EnginePath <> '' then DependencyStatus.Caption := FmtMessage(CustomMessage('DependencyFound'), [EnginePath])
    else DependencyStatus.Caption := CustomMessage('DependencyMissing');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ProgressPage.Show;
  try
    EnginePath := FindEverything;
    if (EnginePath = '') and not InstallEverything then begin Result := CustomMessage('DependencyFailed'); exit; end;
    Log('Reusing Everything: ' + EnginePath);
    { Service repair and index validation share the application's startup path.
      A loaded but empty database must never skip service provisioning. }
  finally
    ProgressPage.Hide;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  LanguageCode: String;
  ReportText: AnsiString;
begin
  if CurStep = ssPostInstall then begin
    if ActiveLanguage = 'korean' then LanguageCode := 'ko' else LanguageCode := 'en';
    RegWriteStringValue(HKCU, SettingsKey, 'Language', LanguageCode);
    RegWriteStringValue(HKCU, SettingsKey, 'EverythingPath', EnginePath);
    ProgressPage.Show;
    try
      ProgressPage.SetText(CustomMessage('CheckingIndex'), '');
      if not Exec(ExpandConstant('{app}\DiskSpaceAnalyzer.exe'), '--check-everything --output "' + ExpandConstant('{tmp}\everything-check.txt') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
        RaiseException(CustomMessage('QueryFailed'));
      if LoadStringFromFile(ExpandConstant('{tmp}\everything-check.txt'), ReportText) then Log(String(ReportText));
      if Code <> 0 then RaiseException(CustomMessage('QueryFailed'));
      Log('Verified Everything IPC search through the installed application.');
    finally
      ProgressPage.Hide;
    end;
  end;
end;

procedure RemoveOwnedExplorerEntry(const Key: String);
var
  Command, Expected: String;
begin
  if RegQueryStringValue(HKCU, Key + '\command', '', Command) then begin
    Expected := '"' + ExpandConstant('{app}\DiskSpaceAnalyzer.exe') + '" --root ';
    if Pos(Lowercase(Expected), Lowercase(Command)) = 1 then
      RegDeleteKeyIncludingSubkeys(HKCU, Key);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    RemoveOwnedExplorerEntry('Software\Classes\Directory\shell\DiskSpaceAnalyzer.Analyze');
    RemoveOwnedExplorerEntry('Software\Classes\Directory\Background\shell\DiskSpaceAnalyzer.Analyze');
    { Keep Everything, user preferences, and all LocalAppData user data. }
  end;
end;
