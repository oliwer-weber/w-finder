; Quip for Revit: Inno Setup Script (multi-version)
;
; Normally compiled by build-installer.ps1, which builds each Revit version and
; passes /DPackageVersions=<comma list> so only freshly built versions are packaged.
; It can also be compiled by hand:  ISCC.exe QuipSetup.iss
; In that case every supported version whose bin\Release\<YYYY>\Quip.dll exists is packaged.
;
; Layout produced on the user's machine:
;   {app}\2025\Quip.dll (+ dependency DLLs, Quip.deps.json)
;   {app}\2026\Quip.dll ...
;   %AppData%\Autodesk\Revit\Addins\<YYYY>\Quip.addin  -> {app}\<YYYY>\Quip.dll

#define MyAppName "Quip for Revit"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Oliwer Weber"

; ---------------------------------------------------------------------------
; SUPPORTED REVIT VERSIONS: the single source of truth for the whole build.
; build-installer.ps1 reads this exact line too, so it is the ONLY place to edit.
; To add a version (e.g. 2028): append "2028" to the list AND bump the size,
; e.g.  #dim SupportedVersions[4] {"2025", "2026", "2027", "2028"}
; (ISPP requires the size; the compile fails if the size and the list disagree.)
; ---------------------------------------------------------------------------
#dim SupportedVersions[3] {"2025", "2026", "2027"}

#define BuildRoot AddBackslash(SourcePath) + "..\bin\Release"

; Comma separated list of versions to package, passed by build-installer.ps1.
; Empty means "package every supported version that has a build on disk".
#ifndef PackageVersions
  #define PackageVersions ""
#endif

; ---------------------------------------------------------------------------
; Preprocessor pass: work out which supported versions get packaged.
; Packaged[i] is 1 when SupportedVersions[i] goes into this installer.
; ---------------------------------------------------------------------------
#define NumSupported DimOf(SupportedVersions)
#dim Packaged[NumSupported]
#define PackagedCount 0
#define PackagedList ""
#define I 0
#define V ""

; Note: inside a #sub, "#define X" would not update the outer variable, so the
; body uses "#expr X = ..." assignments instead.
#sub DecidePackaged
  #expr V = SupportedVersions[I]
  #if TypeOf(V) != TYPE_STRING
    #error SupportedVersions: the [N] size is larger than the number of years listed.
  #endif
  #expr Packaged[I] = 0
  #if FileExists(BuildRoot + "\" + V + "\Quip.dll")
    #if PackageVersions == "" || Pos("," + V + ",", "," + PackageVersions + ",") > 0
      #expr Packaged[I] = 1
      #expr PackagedCount = PackagedCount + 1
      #expr PackagedList = (PackagedList == "" ? V : PackagedList + ", " + V)
    #endif
  #endif
#endsub
#for {I = 0; I < NumSupported; I++} DecidePackaged

#if PackagedCount == 0
  #error No Revit version builds found in bin\Release\<YYYY>\. Run build-installer.ps1 first.
#endif

#pragma message "Quip installer: packaging Revit versions " + PackagedList

[Setup]
; AppId must never change: it is what lets 1.1.0 upgrade the 1.0.0 (2025 only) install in place.
AppId={{E4A7B3C1-5D2F-4E8A-9C6B-1F3D7A2E5B40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\Quip
DefaultGroupName=Quip
OutputDir=output
OutputBaseFilename=QuipSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
; Revit only exists for 64-bit Windows.
ArchitecturesAllowed=x64compatible
; Re-use the task choices of a previous 1.1.0+ install as the starting point.
; InitializeWizard still unticks versions whose Revit is not detected.
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
#sub EmitTask
  #if Packaged[I]
Name: "revit{#SupportedVersions[I]}"; Description: "Revit {#SupportedVersions[I]}"; GroupDescription: "Load Quip in these Revit versions:"
  #endif
#endsub
#for {I = 0; I < NumSupported; I++} EmitTask

[InstallDelete]
; Old flat layout from the 1.0.0 (Revit 2025 only) installer. Listed explicitly
; rather than {app}\*.dll so a custom install folder never loses unrelated files.
Type: files; Name: "{app}\Quip.dll"
Type: files; Name: "{app}\Quip.deps.json"
Type: files; Name: "{app}\Quip.runtimeconfig.json"
Type: files; Name: "{app}\ClosedXML.dll"
Type: files; Name: "{app}\ClosedXML.Parser.dll"
Type: files; Name: "{app}\DocumentFormat.OpenXml.dll"
Type: files; Name: "{app}\DocumentFormat.OpenXml.Framework.dll"
Type: files; Name: "{app}\ExcelNumberFormat.dll"
Type: files; Name: "{app}\RBush.dll"
Type: files; Name: "{app}\SixLabors.Fonts.dll"
Type: files; Name: "{app}\System.IO.Packaging.dll"
; Wipe every per-version folder before copying, so stale dependency DLLs from an
; older build never linger and unticked versions are cleaned out.
#sub EmitInstallDelete
Type: filesandordirs; Name: "{app}\{#SupportedVersions[I]}"
#endsub
#for {I = 0; I < NumSupported; I++} EmitInstallDelete

[Files]
; Everything Revit needs to load Quip for one version: all DLLs plus the deps and
; runtimeconfig JSON files. No .pdb files. Only installed for ticked versions.
#sub EmitFiles
  #if Packaged[I]
Source: "{#BuildRoot}\{#SupportedVersions[I]}\*.dll"; DestDir: "{app}\{#SupportedVersions[I]}"; Tasks: revit{#SupportedVersions[I]}; Flags: ignoreversion
Source: "{#BuildRoot}\{#SupportedVersions[I]}\Quip.deps.json"; DestDir: "{app}\{#SupportedVersions[I]}"; Tasks: revit{#SupportedVersions[I]}; Flags: ignoreversion
Source: "{#BuildRoot}\{#SupportedVersions[I]}\*.runtimeconfig.json"; DestDir: "{app}\{#SupportedVersions[I]}"; Tasks: revit{#SupportedVersions[I]}; Flags: ignoreversion skipifsourcedoesntexist
  #endif
#endsub
#for {I = 0; I < NumSupported; I++} EmitFiles

[UninstallDelete]
; Remove the manifest and program folder for every supported version, whether
; or not it was ticked, so nothing is left pointing at deleted DLLs.
#sub EmitUninstallDelete
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\{#SupportedVersions[I]}\Quip.addin"
Type: filesandordirs; Name: "{app}\{#SupportedVersions[I]}"
#endsub
#for {I = 0; I < NumSupported; I++} EmitUninstallDelete

[Code]
// ---------------------------------------------------------------------------
// Version lists generated by the preprocessor from SupportedVersions above.
// ---------------------------------------------------------------------------
function GetSupportedVersions(): TArrayOfString;
begin
  SetArrayLength(Result, {#NumSupported});
#sub EmitSupportedCode
  Result[{#I}] := '{#SupportedVersions[I]}';
#endsub
#for {I = 0; I < NumSupported; I++} EmitSupportedCode
end;

// True when this installer contains files (and a task checkbox) for Version.
function IsPackaged(const Version: String): Boolean;
begin
  Result := False;
#sub EmitPackagedCode
  #if Packaged[I]
  if Version = '{#SupportedVersions[I]}' then Result := True;
  #endif
#endsub
#for {I = 0; I < NumSupported; I++} EmitPackagedCode
end;

// ---------------------------------------------------------------------------
// Revit detection
// ---------------------------------------------------------------------------

// A Revit version counts as installed if either:
//  - Revit.exe exists in the default install folder, or
//  - the Autodesk registry key HKLM\SOFTWARE\Autodesk\Revit\<YYYY> exists
//    (covers Revit installed to a non-default drive or folder).
// This only decides the default tick; the user can still tick any version.
function IsRevitInstalled(const Version: String): Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{commonpf64}\Autodesk\Revit ' + Version + '\Revit.exe')) or
    RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\Revit\' + Version);
end;

function IsRevitRunning(): Boolean;
var
  ResultCode: Integer;
  Output: AnsiString;
  TempFile: String;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\revitcheck.txt');
  // Run tasklist, filter for Revit.exe, write output to temp file
  if Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq Revit.exe" /NH > "' + TempFile + '"', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TempFile, Output) then
    begin
      // If Revit.exe appears in the output, it's running
      if Pos('Revit.exe', String(Output)) > 0 then
        Result := True;
    end;
    DeleteFile(TempFile);
  end;
end;

// ---------------------------------------------------------------------------
// Setup events
// ---------------------------------------------------------------------------

function InitializeSetup(): Boolean;
begin
  Result := True;
  if IsRevitRunning() then
  begin
    MsgBox('Revit is currently running.' + #13#10 + #13#10 +
           'Please save your work and close Revit before installing Quip.' + #13#10 +
           'The installer will now exit.',
           mbError, MB_OK);
    Result := False;
  end;
end;

// Every task starts ticked. Untick the versions whose Revit is not on this machine.
// Runs after any previous-install task choices have been restored, so a version
// that is no longer installed is unticked even if it was ticked last time.
procedure InitializeWizard();
var
  Versions: TArrayOfString;
  i: Integer;
begin
  Versions := GetSupportedVersions();
  for i := 0 to GetArrayLength(Versions) - 1 do
  begin
    if IsPackaged(Versions[i]) and not IsRevitInstalled(Versions[i]) then
      WizardSelectTasks('!revit' + Versions[i]);
  end;
end;

// Warn if the user unticked everything: Quip would install but never load.
// Skipped on silent installs (NextButtonClick runs there too, with no user to ask).
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectTasks) and (not WizardSilent()) and (WizardSelectedTasks(False) = '') then
  begin
    Result := MsgBox('No Revit version is selected, so Quip will not load in Revit.' + #13#10 + #13#10 +
                     'Continue anyway?', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

function GetAddinPath(const Version: String): String;
begin
  Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Version + '\Quip.addin');
end;

procedure WriteAddinFile(const Version: String);
var
  AddinDir: String;
  DllPath: String;
  Lines: TArrayOfString;
begin
  AddinDir := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Version);
  DllPath := ExpandConstant('{app}\' + Version + '\Quip.dll');

  // Create the Addins\<YYYY> directory if it doesn't exist
  if not DirExists(AddinDir) then
    ForceDirectories(AddinDir);

  SetArrayLength(Lines, 11);
  Lines[0]  := '<?xml version="1.0" encoding="utf-8"?>';
  Lines[1]  := '<RevitAddIns>';
  Lines[2]  := '  <AddIn Type="Application">';
  Lines[3]  := '    <Name>Quip</Name>';
  Lines[4]  := '    <Assembly>' + DllPath + '</Assembly>';
  Lines[5]  := '    <AddInId>e4a7b3c1-5d2f-4e8a-9c6b-1f3d7a2e5b40</AddInId>';
  Lines[6]  := '    <FullClassName>w_finder.App</FullClassName>';
  Lines[7]  := '    <VendorId>OliwerWeber</VendorId>';
  Lines[8]  := '    <VendorDescription>Oliwer Weber</VendorDescription>';
  Lines[9]  := '  </AddIn>';
  Lines[10] := '</RevitAddIns>';

  // Written as real UTF-8 (matching the XML header) so install paths with
  // characters like a-ring or o-umlaut in the Windows user name survive.
  if not SaveStringsToUTF8File(GetAddinPath(Version), Lines, False) then
    Log('Failed to write ' + GetAddinPath(Version));
end;

procedure RemoveAddinFile(const Version: String);
var
  AddinPath: String;
begin
  AddinPath := GetAddinPath(Version);
  if FileExists(AddinPath) then
    DeleteFile(AddinPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Versions: TArrayOfString;
  i: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Versions := GetSupportedVersions();
    for i := 0 to GetArrayLength(Versions) - 1 do
    begin
      // Only ask about tasks that exist in this installer; WizardIsTaskSelected
      // on an unknown task name is not something to rely on.
      if IsPackaged(Versions[i]) and WizardIsTaskSelected('revit' + Versions[i]) then
        WriteAddinFile(Versions[i])
      else
        // Unticked, or not in this build: its {app}\<YYYY> folder was wiped by
        // [InstallDelete], so a leftover manifest would point at nothing.
        RemoveAddinFile(Versions[i]);
    end;
  end;
end;

// ---------------------------------------------------------------------------
// Uninstall events
// ---------------------------------------------------------------------------

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if IsRevitRunning() then
  begin
    MsgBox('Revit is currently running.' + #13#10 + #13#10 +
           'Please close Revit before uninstalling Quip.',
           mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Versions: TArrayOfString;
  i: Integer;
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove the .addin manifest for every supported version. [UninstallDelete]
    // does the same; this also covers manifests written by the 1.0.0 installer.
    Versions := GetSupportedVersions();
    for i := 0 to GetArrayLength(Versions) - 1 do
      RemoveAddinFile(Versions[i]);

    // Ask about removing user settings and favorites
    SettingsDir := ExpandConstant('{userappdata}\Quip');
    if DirExists(SettingsDir) then
    begin
      if MsgBox('Do you want to remove your Quip settings, favorites, and recent items?',
                 mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(SettingsDir, True, True, True);
      end;
    end;
  end;
end;
