# CLAUDE.md — Quip Revit 2025 Plugin

## Developer Context
- **Skill level:** Learning C# — needs guidance on syntax, patterns, and Revit API usage.
- **IDE:** Codium (AI-assisted). No Visual Studio. Build/test via `dotnet build` from CLI.
- **Workflow:** AI writes all code. Developer builds from terminal, tests in Revit, reports back. Iterate.
- Prefer simple, readable code over clever abstractions.
- When making changes, be explicit about which file is being modified and why.
- Explain non-obvious C# or Revit API patterns briefly so the developer can learn.
- If unsure about a Revit API detail, say so — the developer will test and report back.

## Environment & Paths
- **Supported Revit versions:** 2025, 2026, 2027. One project, version picked with `-p:RevitVersion=<YYYY>` (default 2025).
- **Revit install:** `C:\Program Files\Autodesk\Revit <YYYY>\` (HintPaths follow `RevitVersion`)
- **Revit API DLLs:** `RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll` (all `<Private>false</Private>`)
- **Target framework:** `net8.0-windows` for 2025/2026, `net10.0-windows` for 2027, with `<UseWPF>true</UseWPF>` and `<EnableDynamicLoading>true</EnableDynamicLoading>`
- **Compile symbols:** `REVIT2025`/`REVIT2026`/`REVIT2027`, plus `REVIT2026_OR_GREATER`, `REVIT2027_OR_GREATER`
- **Output:** `bin\<Config>\<YYYY>\`; obj is split per version via `Directory.Build.props`
- **.NET 10 SDK:** user-local at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (PATH `dotnet` has SDK 8 only, which can't build 2027)
- **Addins manifest:** `%AppData%\Autodesk\Revit\Addins\<YYYY>\`
- **Adding a Revit version:** add the year to `#dim SupportedVersions[N]` in `installer\QuipSetup.iss` (bump N), and extend the TFM/DefineConstants conditions in `w_finder.csproj` if it changes .NET.

## Project Structure
```
w_finder/
├── App.cs                        # IExternalApplication — ribbon button + dockable pane registration
├── FinderCommand.cs              # IExternalCommand — toggles pane visibility, refreshes item cache
├── FinderDockablePane.cs         # IDockablePaneProvider — WPF host
├── ResetPaneCommand.cs           # Emergency reset for lost pane position
├── Models/
│   ├── BrowserItem.cs            # Data model + BrowserItemKind enum (..., Command, Shebang)
│   └── QuickAction.cs            # QuickActionKind enum + action model (Rename, Delete, Duplicate, etc.)
├── ViewModels/
│   └── FinderPaneViewModel.cs    # MVVM — search logic, mode detection, favorites, selection, action bar
├── Views/
│   ├── FinderPaneView.xaml       # WPF UI (search box, mode badges, grouped results, favorites, action bar)
│   ├── FinderPaneView.xaml.cs    # Code-behind — keyboard nav, Revit API actions via ExternalEvent
│   ├── EqualityConverter.cs      # IMultiValueConverter for action bar highlight
│   ├── LightTheme.xaml           # Light mode resources
│   └── DarkTheme.xaml            # Dark mode resources
├── Services/
│   ├── BrowserItemCollector.cs   # Collects all project browser items from the Revit model
│   ├── CommandCollector.cs       # Builds BrowserItems from Revit's PostableCommand enum for Command Mode
│   ├── ShebangService.cs         # Registry + executor for shebang commands (! prefix)
│   ├── QuickActionResolver.cs    # Determines available quick actions per item kind
│   ├── FuzzyMatcher.cs           # Subsequence matching with scoring (consecutive, boundary, prefix bonuses)
│   └── FavoritesStore.cs         # Per-project favorites persistence (sidecar JSON / AppData for cloud)
├── Helpers/
│   ├── RevitBackgroundTask.cs    # Singleton ExternalEvent handler for safe Revit API calls from WPF
│   └── ThemeService.cs           # Light/dark mode state + persistence
├── w_finder.csproj
└── w_finder.addin
```

## Modes

All modes are detected by a single-character prefix in the search box. Mode detection lives in `FinderPaneViewModel` as computed properties.

| Prefix | Mode | Badge color | Filters to |
|--------|------|-------------|------------|
| _(none)_ | Browser | Blue | All items (views, sheets, schedules, families, groups, links, assemblies) |
| `>` | Family | Green/Purple | `BrowserItemKind.FamilyType` only. Flags: `-c <cat>` filter, `-e` edit mode |
| `:` | Command | Orange | `BrowserItemKind.Command` (all Revit PostableCommands) |
| `!` | Shebang | Green | `BrowserItemKind.Shebang` (custom plugin commands) |

### Family Mode flags
- **`-c <word>`** — category substring filter (case-insensitive): `> -c Doors`, `> -c win sofa`
- **`-e`** — edit mode: opens family for editing instead of placing. Purple "EDIT" badge.

### Shebang Mode (`!` prefix)
- Custom plugin commands registered in `ShebangService.Shebangs` array.
- Currently one shebang: `!pu` — Toggle Project Units between imperial (feet/fractional inches, 1/16" accuracy) and SI (mm, m², m³).
- Shebangs use negative ElementIds to avoid collisions with Revit elements.

## Quick Actions
- **Tab** on a highlighted browser item opens the action bar (pill row at bottom of pane).
- Available actions depend on item kind (resolved by `QuickActionResolver`):
  - Views: Rename (F2), Delete (Del), Duplicate (D), Dup+Detail (Shift+D), Dependent (Ctrl+D)
  - Sheets: Rename, Delete, Duplicate, Dup+Detail
  - Schedules: Rename, Delete, Duplicate
  - Families/Groups/Links: Rename, Delete
  - Commands/Shebangs: no actions
- Left/Right to navigate pills, Enter to execute, Escape to close action bar.

## Technical Patterns
- **Thread safety:** All Revit API calls go through `RevitBackgroundTask.Raise(Action<UIApplication>)` — singleton ExternalEvent that queues one action at a time.
- **MVVM:** ViewModel has no Revit API references. Code-behind subscribes to ViewModel events (`ItemSelectionRequested`, `ItemOpenRequested`, `QuickActionRequested`) and bridges to Revit via ExternalEvent.
- **BrowserItemKind enum:** Tags every collected item so modes can filter by kind.
- **Keyboard nav:** Up/Down to navigate, Enter to open/execute, Tab to open action bar, Escape to close action bar or hide pane, Ctrl+F to toggle favorite.

## Build & Test
```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"   # has the .NET 10 SDK
& $dotnet build -p:RevitVersion=2027      # -> bin\Debug\2027\Quip.dll (2025/2026 work too)
.\build-installer.ps1                     # Release build of every version + installer\output\QuipSetup.exe
# Dev manifest: %AppData%\Autodesk\Revit\Addins\<YYYY>\Quip.addin pointing at bin\Debug\<YYYY>\Quip.dll
# Close Revit before rebuilding the version it has loaded (DLL lock).
# MSB3277 warnings are normal (Revit DLL version conflicts).
```

## Known Issues
- `RevitBackgroundTask` holds one pending action — favorites save bypasses ExternalEvent (pure file I/O).
- Auto-detection of Revit dark mode not reliable — using manual toggle.
