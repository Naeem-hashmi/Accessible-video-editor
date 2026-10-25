; Inno Setup script for ClipScribe.
; The Inno Setup wizard uses standard Windows dialogs, so NVDA, JAWS and Narrator read every page.
; Build: iscc /DSourceDir=<publish folder> /DAppVersion=0.1.0 AccessibleVideoEditor.iss

#define AppName "ClipScribe"
#define AppExe "ClipScribe.exe"
#ifndef AppVersion
  #define AppVersion "0.4.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\app"
#endif

[Setup]
AppId={{6F1C2B9E-5A4D-4C1E-9B7A-3E2D1C0B9A87}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ClipScribe contributors
AppPublisherURL=https://github.com/Naeem-hashmi/Accessible-video-editor
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\build\installer
OutputBaseFilename=ClipScribe-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "associate"; Description: "Open .avproj project files with {#AppName}"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Screen-reader-first video editor"
Name: "{autoprograms}\{#AppName} user guide"; Filename: "{app}\docs\USER_GUIDE.html"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.avproj"; ValueType: string; ValueName: ""; ValueData: "AccessibleVideoEditor.Project"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\AccessibleVideoEditor.Project"; ValueType: string; ValueName: ""; ValueData: "ClipScribe project"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\AccessibleVideoEditor.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\AccessibleVideoEditor.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent
