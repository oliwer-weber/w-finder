; Quip for Revit 2025 — Inno Setup Script
; Compile with: ISCC.exe QuipSetup.iss

#define MyAppName "Quip for Revit 2025"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Oliwer Weber"
#define MyAppURL ""
#define BuildOutput "..\bin\Release\net8.0-windows"

[Setup]
AppId={{E4A7B3C1-5D2F-4E8A-9C6B-1F3D7A2E5B40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Main plugin assembly and runtime config
Source: "{#BuildOutput}\Quip.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Quip.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Quip.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; NuGet dependencies
Source: "{#BuildOutput}\ClosedXML.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\ClosedXML.Parser.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DocumentFormat.OpenXml.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DocumentFormat.OpenXml.Framework.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\ExcelNumberFormat.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\RBush.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\SixLabors.Fonts.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.IO.Packaging.dll"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\Quip.addin"

[Code]
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

procedure GenerateAddinFile();
var
  AddinPath: String;
  AddinDir: String;
  AddinContent: String;
  DllPath: String;
begin
  AddinDir := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2025');
  AddinPath := AddinDir + '\Quip.addin';
  DllPath := ExpandConstant('{app}\Quip.dll');

  // Create the Addins\2025 directory if it doesn't exist
  if not DirExists(AddinDir) then
    ForceDirectories(AddinDir);

  AddinContent :=
    '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<RevitAddIns>' + #13#10 +
    '  <AddIn Type="Application">' + #13#10 +
    '    <Name>Quip</Name>' + #13#10 +
    '    <Assembly>' + DllPath + '</Assembly>' + #13#10 +
    '    <AddInId>e4a7b3c1-5d2f-4e8a-9c6b-1f3d7a2e5b40</AddInId>' + #13#10 +
    '    <FullClassName>w_finder.App</FullClassName>' + #13#10 +
    '    <VendorId>OliwerWeber</VendorId>' + #13#10 +
    '    <VendorDescription>Oliwer Weber</VendorDescription>' + #13#10 +
    '  </AddIn>' + #13#10 +
    '</RevitAddIns>' + #13#10;

  SaveStringToFile(AddinPath, AddinContent, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    GenerateAddinFile();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AddinPath: String;
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove the .addin manifest
    AddinPath := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2025\Quip.addin');
    if FileExists(AddinPath) then
      DeleteFile(AddinPath);

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
