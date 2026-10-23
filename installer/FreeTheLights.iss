; Inno Setup script of the FreeTheLights installer: copies FreeTheLights.dll into SimHub's folder.
; Build it with tools/installer.sh (release.sh does), or: ISCC.exe /DSourceDll=<path to FreeTheLights.dll> FreeTheLights.iss
; The version comes from the DLL, so the installer always matches the plugin it contains.

#ifndef SourceDll
  #define SourceDll AddBackslash(SourcePath) + "..\src\FreeTheLights\bin\Release\FreeTheLights.dll"
#endif
#if !FileExists(SourceDll)
  #error FreeTheLights.dll not found: build the Release configuration first, or pass /DSourceDll=<path>
#endif

#define Major
#define Minor
#define Patch
#define Build
#expr GetVersionComponents(SourceDll, Major, Minor, Patch, Build)
#define AppVersion Str(Major) + "." + Str(Minor) + "." + Str(Patch)

[Setup]
; Never change the AppId: Windows recognises installs and upgrades by it.
AppId={{D5F54DF8-C27D-4880-A53D-2228BF378396}
AppName=FreeTheLights
AppVersion={#AppVersion}
AppVerName=FreeTheLights {#AppVersion}
AppPublisher=FreeTheLights
AppPublisherURL=https://github.com/Grollo13/FreeTheLights
AppSupportURL=https://github.com/Grollo13/FreeTheLights/issues
AppUpdatesURL=https://github.com/Grollo13/FreeTheLights/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=FreeTheLights setup
UninstallDisplayName=FreeTheLights (SimHub plugin)
UninstallDisplayIcon={app}\SimHubWPF.exe
; SimHub's own uninstaller (also Inno Setup) lives in SimHub's folder: keep ours out of it.
UninstallFilesDir={autoappdata}\FreeTheLights\uninstall
; The plugin goes into SimHub's folder, wherever SimHub is installed now.
DefaultDirName={code:DefaultSimHubDir}
UsePreviousAppDir=no
AppendDefaultDirName=no
DirExistsWarning=no
DisableProgramGroupPage=yes
; SimHub's default folder is under Program Files. /CURRENTUSER installs without administrator rights (SimHub in a
; folder of the user's, and the installer's own tests).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
; SimHub is closed by the code below, not by the Restart Manager: on a first install no file of SimHub's is in use.
CloseApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=FreeTheLights-Setup-{#AppVersion}
SetupLogging=yes

[Messages]
SelectDirDesc=Where is SimHub installed?
SelectDirLabel3=FreeTheLights is a SimHub plugin, so it goes into SimHub's folder.
SelectDirBrowseLabel=Setup found SimHub in this folder. To continue, click Next. If SimHub is installed somewhere else, click Browse and choose the folder that contains SimHubWPF.exe.
FinishedLabel=FreeTheLights is installed. Start SimHub and enable the plugin when it asks; it then appears in SimHub's left menu.

[Files]
Source: "{#SourceDll}"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
; Left behind by the plugin's own updates (renamed while SimHub ran) and by development builds.
Type: files; Name: "{app}\FreeTheLights.dll.old"
Type: files; Name: "{app}\FreeTheLights.dll.*.old"
Type: files; Name: "{app}\FreeTheLights.pdb"

[Run]
Filename: "{app}\SimHubWPF.exe"; Description: "Start SimHub"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  SimHubExe = 'SimHubWPF.exe';
  { SimHub's own Inno Setup AppId: its uninstall entry knows where it is installed. }
  SimHubUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{019253FE-5A17-42BE-A6B8-D71A729FA5DE}_is1';
  CloseWaitMs = 20000;

function IsSimHubDir(Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + SimHubExe);
end;

{ SimHub's folder: from SimHub's own registry entry, else its uninstall entry; empty if neither has one. }
function FindSimHubDir: String;
var
  Dir: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\SimHub', 'InstallDirectory', Dir) and IsSimHubDir(Dir) then
    Result := Dir
  else if RegQueryStringValue(HKLM, SimHubUninstallKey, 'InstallLocation', Dir) and IsSimHubDir(Dir) then
    Result := Dir
  else if RegQueryStringValue(HKCU, SimHubUninstallKey, 'InstallLocation', Dir) and IsSimHubDir(Dir) then
    Result := Dir;
  if Result <> '' then Result := RemoveBackslashUnlessRoot(Result);
end;

function DefaultSimHubDir(Param: String): String;
begin
  Result := FindSimHubDir;
  if Result = '' then Result := ExpandConstant('{commonpf32}\SimHub');
end;

{ The ids of the SimHub processes started from Dir, separated by spaces; empty if none. Only this folder's SimHub
  counts: another SimHub installation can keep running. A process whose path can't be read counts too. }
function SimHubProcessesIn(Dir: String): String;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  Path: String;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''' + SimHubExe + '''');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      if VarIsNull(Process.ExecutablePath) then
        Path := ''
      else
        Path := Process.ExecutablePath;
      if (Path = '') or (CompareText(ExtractFileDir(Path), RemoveBackslashUnlessRoot(Dir)) = 0) then
        Result := Result + ' ' + IntToStr(Process.ProcessId);
    end;
  except
    { Without WMI the check is skipped; a DLL in use still makes copying it fail with Setup's retry message. }
    Log('Could not look for a running SimHub: ' + GetExceptionMessage);
  end;
  Result := Trim(Result);
end;

{ Asks SimHub to close, as clicking its close button does, and waits for it. True once it is closed. }
function CloseSimHubIn(Dir: String): Boolean;
var
  Ids, Args: String;
  Space, Code, Waited: Integer;
begin
  Ids := SimHubProcessesIn(Dir) + ' ';
  Args := '';
  while Trim(Ids) <> '' do
  begin
    Ids := Trim(Ids) + ' ';
    Space := Pos(' ', Ids);
    Args := Args + ' /PID ' + Copy(Ids, 1, Space - 1);
    Delete(Ids, 1, Space);
  end;
  if Args <> '' then
    Exec(ExpandConstant('{sys}\taskkill.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);

  Waited := 0;
  while (SimHubProcessesIn(Dir) <> '') and (Waited < CloseWaitMs) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  Result := SimHubProcessesIn(Dir) = '';
end;

{ Makes sure the SimHub in Dir is closed, asking first. False if the user would rather stop. }
function EnsureSimHubClosed(Dir: String; Silent: Boolean): Boolean;
var
  Question: String;
begin
  Result := True;
  Question := 'SimHub is running. It has to be closed first.' + #13#10#13#10 + 'Click OK to close SimHub now, or Cancel to stop.';
  while SimHubProcessesIn(Dir) <> '' do
  begin
    { Never close SimHub unasked: a silent run stops instead. }
    if Silent or (MsgBox(Question, mbConfirmation, MB_OKCANCEL) <> IDOK) then
    begin
      Result := False;
      exit;
    end;
    if not CloseSimHubIn(Dir) then
      Question := 'SimHub is still running. It may be waiting for an answer in its window.' + #13#10#13#10 +
        'Close SimHub, then click OK, or click Cancel to stop.';
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not IsSimHubDir(WizardDirValue) then
  begin
    MsgBox('SimHub is not installed in this folder. Choose the folder that contains ' + SimHubExe + '.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir: String;
begin
  Result := '';
  Dir := ExpandConstant('{app}');
  if not IsSimHubDir(Dir) then
    Result := 'SimHub is not installed in ' + Dir + '. Install SimHub first, or choose the folder that contains ' + SimHubExe + '.'
  else if not EnsureSimHubClosed(Dir, WizardSilent) then
    Result := 'FreeTheLights was not installed because SimHub is running. Close SimHub and run Setup again.';
end;

function InitializeUninstall: Boolean;
begin
  Result := EnsureSimHubClosed(ExpandConstant('{app}'), UninstallSilent);
  if not Result and UninstallSilent then
    Log('FreeTheLights was not removed because SimHub is running.');
end;
