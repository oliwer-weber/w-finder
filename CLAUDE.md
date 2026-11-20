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
│   ├── FinderPaneView.xaml       # WPF UI (search box, mode badges, grouped results, favorites, action bar, hint bar)
│   ├── FinderPaneView.xaml.cs    # Code-behind core (partial class): setup, pill/theme, mouse, closing
│   ├── FinderPaneView.Keyboard.cs        # THE keyboard handler, shared by every screen
│   ├── FinderPaneView.Activation.cs      # Enter / double-click: open view, place family, run command, launch quip
│   ├── FinderPaneView.QuickActions.cs    # Rename, delete, duplicate, export, edit family, shortcuts
│   ├── FinderPaneView.SelectionFilter.cs # !sf (built on the report engine)
│   ├── FinderPaneView.Inspector.cs       # !pi
│   ├── FinderPaneView.ParamFilter.cs     # !pf
│   ├── FinderPaneView.Reports.cs         # !dwg, !lp + shared report action plumbing
│   ├── FinderPaneView.UnusedTypes.cs     # !ut, !us
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
- Custom plugin commands ("quips") registered in `ShebangService.Shebangs` array.
- **One-shot quips** (`pu`, `pin`, `gv`) run in `ShebangService.Execute`, which returns `(Ok, Message)`. Success closes the pane; failure keeps it open and shows the message.
- **Interactive quips** take over the pane (`PaneSurface`) and are launched from `FinderPaneView.Activation.cs > LaunchQuip`:
  - `sf` Selection Filter, `dwg`, `lp`, `ut`, `us`: pages of rows on the shared **report engine** (`ReportPage` / `ReportRow`).
  - `pi` Property Inspector and `pf` Parameter Filter: their own surfaces.
- Shebangs use negative ElementIds to avoid collisions with Revit elements.

## Keyboard grammar (identical on every screen)

Handled in ONE place: `FinderPaneView.Keyboard.cs`. Do not add per-quip key handlers; add a case to the shared handler's helper for the surface instead.

| Key | Meaning |
|-----|---------|
| Up / Down | Move the cursor |
| Enter (or double-click) | The row's main action: open, run, drill in, edit, pick |
| Ctrl+Enter | Finish a multi-step quip: apply filter, delete ticked, run (`ReportPage.PrimaryActionKey`) |
| Right | Drill in. Never executes anything |
| Left | Back one level (only when the caret is at the start of the box) |
| Backspace on empty box | Back one level; at the top of a prefix mode it removes the mode |
| Space | Tick, on rows with a tick box. Types a space everywhere else |
| Tab | Search: action bar (cycles pills when open). `pf`: next field |
| Ctrl+A / Ctrl+D | Tick all / none on report pages with tick boxes |
| Esc | Step back ONE layer: edit/rename box, action bar, drill level, quip. Closes the pane when nothing is left (`FinderPaneViewModel.StepBack`) |

## Closing rule

- `FinderCommand.ClosePane(uiApp)` is the only way the pane closes (resets all transient state, saves last state, hides). Never call `pane.Hide()` directly.
- Anything that hands control back to Revit closes on success: open view, place family, run command, one-shot quip, `sf` apply, `pf` run, select-in-model, edit family.
- Repeatable housekeeping stays open with a toast: rename, delete, duplicate, export, inspector edits, report pages.
- Failures always keep the pane open (or reopen it via `ReopenWithError`) with the error visible.

## Quick Actions
- **Tab** on a highlighted browser item opens the action bar (pill row at bottom of pane).
- Available actions depend on item kind (resolved by `QuickActionResolver`):
  - Views: Rename (F2), Delete (Del), Duplicate (D), Dup+Detail (Shift+D), Dependent (Ctrl+D)
  - Sheets: Rename, Delete, Duplicate, Dup+Detail
  - Schedules: Rename, Delete, Duplicate
  - Families/Groups/Links: Rename, Delete
  - Commands: Set Shortcut (S), Remove Shortcut (R). Shebangs: no actions
- Left/Right or Tab to navigate pills, Enter or click to execute, Escape to close the action bar. Pill hotkeys are matched against each pill's `Shortcut` text.

## Technical Patterns
- **Thread safety:** All Revit API calls go through `RevitBackgroundTask.Raise(Action<UIApplication>)`, a singleton ExternalEvent with a queue. Each action is wrapped in try/catch: failures are logged (`QuipLog`) and shown as a toast.
- **MVVM:** ViewModel has no Revit API references. Code-behind subscribes to ViewModel events (`ItemSelectionRequested`, `QuickActionRequested`, `ReportActionRequested`...) and bridges to Revit via ExternalEvent.
- **PaneSurface:** `FinderPaneViewModel.Surface` (`Search`, `Inspector`, `ParamFilter`, `Report`) says which screen owns the pane. Exactly one at a time; `ExitSurface()` leaves a quip, `ResetTransientState()` returns everything to a plain search (run on close and on open).
- **Report engine:** footer hints are generated from what a page can do (`ReportFooterHint`), so pages never hand-write key hints.
- **BrowserItemKind enum:** Tags every collected item so modes can filter by kind.
- **Family placement:** `PostRequestForElementTypePlacement` (non-blocking), with `PromptForFamilyInstancePlacement` as fallback.
- **Copy:** no em-dashes in UI text.

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
