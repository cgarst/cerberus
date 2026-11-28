#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "CerberusService"
#define MyAppPublisher "Cerberus"
#define MyAppExeName "CerberusService.exe"

[Setup]
AppId={{B9E6D8B4-6B5A-4E2F-9E2E-7C5A8A3D9101}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\build
OutputBaseFilename=CerberusServiceSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\CerberusServiceSingleInstanceMutex
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} automatically at Windows logon"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\{#MyAppName}"; Permissions: users-modify
Name: "{commonappdata}\{#MyAppName}\logs"; Permissions: users-modify

[Files]
Source: "..\build\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\config.example.json"; DestDir: "{commonappdata}\{#MyAppName}"; DestName: "config.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{commonappdata}\{#MyAppName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{commonappdata}\{#MyAppName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--configure --autostart --config ""{commonappdata}\{#MyAppName}\config.json"""; Flags: runhidden waituntilterminated; Tasks: autostart; StatusMsg: "Configuring firewall, URL ACL, and startup task..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--configure --config ""{commonappdata}\{#MyAppName}\config.json"""; Flags: runhidden waituntilterminated; Tasks: not autostart; StatusMsg: "Configuring firewall and URL ACL..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{commonappdata}\{#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{commonappdata}\{#MyAppName}"; Flags: nowait; Check: ShouldRelaunchAfterSilentUpdate

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unconfigure --config ""{commonappdata}\{#MyAppName}\config.json"""; Flags: runhidden waituntilterminated; RunOnceId: "CleanupCerberusService"

[Code]
function HasDotNet10DesktopRuntime(): Boolean;
var
  FindRec: TFindRec;
  SearchPath: String;
begin
  Result := False;
  SearchPath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*');
  if FindFirst(SearchPath, FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not HasDotNet10DesktopRuntime() then
  begin
    if MsgBox(
      '{#MyAppName} requires the .NET 10 Desktop Runtime (x64), which was not detected on this PC.' + #13#10 + #13#10 +
      'Would you like to open the .NET 10 download page now?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;

function ShouldRelaunchAfterSilentUpdate(): Boolean;
begin
  Result := WizardSilent() and (Pos('/CERBERUSAUTOUPDATE', UpperCase(GetCmdTail())) > 0);
end;

procedure StopExistingAgent();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM CerberusService.exe /IM RoomAgent.exe /IM PCRemoteAgent.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopExistingAgent();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopExistingAgent();
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\{#MyAppName}');
    if DirExists(DataDir) and not UninstallSilent() then
    begin
      if MsgBox('Do you also want to delete saved configuration and logs in ' + DataDir + '?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
