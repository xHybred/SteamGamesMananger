; Inno Setup script for Steam Games Manager.
; Build the app first:  tools\build-installer.ps1
; Or manually:
;   dotnet publish Sgm.App\Sgm.App.csproj -c Release -r win-x64 --self-contained true ^
;     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false ^
;     -p:DebugType=None -p:DebugSymbols=false -o publish\app
;   iscc installer\installer.iss

#define AppName "Steam Games Manager"
#define AppVersion "1.2.0"
#define AppExe "SteamGamesManager.exe"

; Override with iscc /DSourceDir=..\publish\app to package a different publish folder.
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

[Setup]
AppId={{B4A1E7C2-9D3F-4A6B-8E51-7C2F0D9A4B18}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Hybred Tools
SetupIconFile=..\Sgm.App\Resources\sgm.ico
DefaultDirName={autopf}\Hybred Tools\Steam Games Mananger
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=Steam-Games-Manager-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
VersionInfoVersion=1.2.0.0
VersionInfoDescription={#AppName} setup

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
; Launch as the signed-in user, not elevated, so settings and logs stay in the profile.
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; The app passes /RELAUNCH when it runs this setup to install an update.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RelaunchAfterUpdate

[Code]
function RelaunchAfterUpdate: Boolean;
var
  I: Integer;
begin
  Result := False;
  if not WizardSilent then
    Exit;
  for I := 1 to ParamCount do
  begin
    if Uppercase(ParamStr(I)) = '/RELAUNCH' then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ Settings, logs and thumbnails live in the user profile because the install
  directory under Program Files is read-only at runtime. Offer to remove them
  without blocking a silent uninstall. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox('Also remove your settings, logs and thumbnail cache?',
      mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      DataDir := ExpandConstant('{userappdata}\SteamGamesManager');
      DelTree(DataDir, True, True, True);
      DataDir := ExpandConstant('{localappdata}\SteamGamesManager');
      DelTree(DataDir, True, True, True);
    end;
  end;
end;
