[Setup]
AppId={{8E288345-164A-43E6-A4B4-5B7A2D63852A}
AppName=HnVRMemo
AppVersion={#AppVersion}
AppPublisher=hinoerusi
AppPublisherURL=https://github.com/pealsha/hn-vr-memo
DefaultDirName={autopf}\HnVRMemo
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\HandwriteMemo.App\Assets\HnVRMemo.ico
UninstallDisplayIcon={app}\HnVRMemo.exe
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
AppMutex=Local\HandwriteMemo.v1
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.LaunchHnVRMemo=Launch HnVRMemo
japanese.LaunchHnVRMemo=HnVRMemo を起動する
english.LaunchWithSteamVr=Launch automatically with SteamVR
japanese.LaunchWithSteamVr=SteamVR と一緒に自動起動する

[Tasks]
Name: "autostart"; Description: "{cm:LaunchWithSteamVr}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HnVRMemo"; Filename: "{app}\HnVRMemo.exe"
Name: "{autodesktop}\HnVRMemo"; Filename: "{app}\HnVRMemo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HnVRMemo.exe"; Parameters: "--autostart enable --defer"; Tasks: autostart; Flags: runhidden runasoriginaluser skipifdoesntexist
Filename: "{app}\HnVRMemo.exe"; Description: "{cm:LaunchHnVRMemo}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\HnVRMemo.exe"; Parameters: "--autostart disable"; Flags: runhidden skipifdoesntexist; RunOnceId: "DisableSteamVrAutostart"
