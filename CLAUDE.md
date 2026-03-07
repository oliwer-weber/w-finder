# CLAUDE.md — w_finder Revit 2025 Plugin

## Developer Context
- **Skill level:** Learning C# — needs guidance on syntax, patterns, and Revit API usage.
- **IDE:** Codium (AI-assisted). No Visual Studio. Build/test via `dotnet build` from CLI.
- **Workflow:** AI writes all code. Developer builds from terminal, tests in Revit, reports back. Iterate.
- Prefer simple, readable code over clever abstractions.
- When making changes, be explicit about which file is being modified and why.
- Explain non-obvious C# or Revit API patterns briefly so the developer can learn.
- If unsure about a Revit API detail, say so — the developer will test and report back.

## Environment & Paths
- **Revit 2025 install:** `C:\Program Files\Autodesk\Revit 2025\`
- **Revit API DLLs:** `RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll` (all `<Private>false</Private>`)
- **Target framework:** `net8.0-windows` with `<UseWPF>true</UseWPF>` and `<EnableDynamicLoading>true</EnableDynamicLoading>`
- **Addins manifest:** `%AppData%\Autodesk\Revit\Addins\2025\`

## Project Structure
```
w_finder/
├── App.cs                        # IExternalApplication — ribbon button + dockable pane registration
├── FinderCommand.cs              # IExternalCommand — toggles pane visibility, refreshes item cache
├── FinderDockablePane.cs         # IDockablePaneProvider — WPF host
├── Models/
│   └── BrowserItem.cs            # Data model + BrowserItemKind enum (View, Sheet, Schedule, Family, FamilyType, Group, RevitLink, Assembly, Command)
├── ViewModels/
│   └── FinderPaneViewModel.cs    # MVVM — search logic, mode detection, favorites, selection
├── Views/
│   ├── FinderPaneView.xaml       # WPF UI (search box, mode badge, grouped results, favorites)
│   ├── FinderPaneView.xaml.cs    # Code-behind — keyboard nav, Revit API actions via ExternalEvent
│   ├── LightTheme.xaml           # Light mode resources
│   └── DarkTheme.xaml            # Dark mode resources
├── Services/
│   ├── BrowserItemCollector.cs   # Collects all project browser items from the Revit model
│   ├── CommandCollector.cs       # Builds BrowserItems from Revit's PostableCommand enum for Command Mode
│   ├── FuzzyMatcher.cs           # Subsequence matching with scoring (consecutive, boundary, prefix bonuses)
│   └── FavoritesStore.cs         # Per-project favorites persistence (sidecar JSON / AppData for cloud)
├── Helpers/
│   ├── RevitBackgroundTask.cs    # Singleton ExternalEvent handler for safe Revit API calls from WPF
│   └── ThemeService.cs           # Light/dark mode state + persistence
├── w_finder.csproj
└── w_finder.addin
```

## Features

### Search (default mode)
- Fuzzy search across all project browser items: views, sheets, schedules, families/types, groups, Revit links, assemblies.
- 150ms debounce, subsequence matching with relevance scoring.
- **Single click** → selects item in Revit (Properties palette updates). **Double click / Enter** → opens/navigates to views/sheets.
- Keyboard: Up/Down to navigate, Enter to open, Escape to hide pane, Ctrl+F to toggle favorite.

### Family Launcher Mode (`>` prefix)
- Type `>` to enter Family Mode — filters to only placeable family types (FamilySymbol items).
- Results grouped by category with visual headings (WPF GroupStyle).
- **Enter / double-click** → hides pane, activates the FamilySymbol, calls `PromptForFamilyInstancePlacement()` to start Revit placement mode.
- **`-c` flag** for category filtering: `> -c Doors`, `> -c win`, `> -c gen sofa`. Substring match, case-insensitive. Filter word goes right after `-c`, remaining text is the fuzzy search query.
- **`-e` flag** for edit mode: `> -e door`, `> -e -c win`. Opens the family document for editing via `doc.EditFamily()` instead of placing. Non-editable families are silently skipped. Status bar shows purple "EDIT" badge.
- Status bar shows "PLACE" badge (default) or "EDIT" badge (`-e`) and active filter info.
- Favorites section hidden in Family Mode.

### Command Mode (`:` prefix)
- Type `:` to enter Command Mode — searches all Revit PostableCommands.
- Fuzzy search across all available Revit commands (e.g., `:dynamo`, `:project units`, `:purge`).
- **Enter / double-click** → hides pane, executes the command via `RevitCommandId.LookupPostableCommandId()` + `uiApp.PostCommand()`.
- Status bar shows orange "CMD" badge.
- Favorites section hidden in Command Mode.
- Command names are auto-generated from the `PostableCommand` enum with PascalCase → "Pascal Case" conversion.

### Favorites
- Star toggle on each item, collapsible section above results.
- Persists per project: sidecar JSON for local models, `%AppData%\w_finder\favorites\` for cloud models.

### Theming
- Manual light/dark toggle (sun/moon button). Persists at `%AppData%\w_finder\theme.txt`.

## Technical Patterns
- **Thread safety:** All Revit API calls go through `RevitBackgroundTask.Raise(Action<UIApplication>)` — singleton ExternalEvent that queues one action at a time.
- **MVVM:** ViewModel has no Revit API references. Code-behind subscribes to ViewModel events (`ItemSelectionRequested`, `ItemOpenRequested`) and bridges to Revit via ExternalEvent.
- **Family placement:** `FamilySymbol.Activate()` in a Transaction, then `UIDocument.PromptForFamilyInstancePlacement(symbol)`. Catch `OperationCanceledException` (user pressing Escape is normal).
- **Grouped results:** Code-behind toggles `PropertyGroupDescription` on the CollectionView when `IsFamilyMode` changes.
- **Command execution:** `RevitCommandId.LookupPostableCommandId(PostableCommand)` + `uiApp.PostCommand(cmdId)` — runs any built-in Revit command.
- **BrowserItemKind enum:** Tags every collected item so modes can filter by kind without touching the collector.

## Build & Test
```powershell
dotnet build
# First time: copy w_finder.addin to %AppData%\Autodesk\Revit\Addins\2025\
# Close Revit before rebuilding (DLL lock).
# MSB3277 warnings are normal (Revit DLL version conflicts).
```

## Known Issues
- `RevitBackgroundTask` holds one pending action — favorites save bypasses ExternalEvent (pure file I/O).
- Auto-detection of Revit dark mode not reliable — using manual toggle.
