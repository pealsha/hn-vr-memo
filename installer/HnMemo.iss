[Setup]
AppId={{8E288345-164A-43E6-A4B4-5B7A2D63852A}
AppName=HnMemo
AppVersion={#AppVersion}
AppPublisher=hinoerusi
AppPublisherURL=https://github.com/pealsha/hn-vr-memo
DefaultDirName={autopf}\HnMemo
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\HandwriteMemo.App\Assets\HnMemo.ico
UninstallDisplayIcon={app}\HnMemo.exe
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
english.LaunchHnMemo=Launch HnMemo
japanese.LaunchHnMemo=HnMemo を起動する

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HnMemo"; Filename: "{app}\HnMemo.exe"
Name: "{autodesktop}\HnMemo"; Filename: "{app}\HnMemo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HnMemo.exe"; Description: "{cm:LaunchHnMemo}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\HnMemo.exe"; Parameters: "--autostart disable"; Flags: runhidden skipifdoesntexist; RunOnceId: "DisableSteamVrAutostart"
