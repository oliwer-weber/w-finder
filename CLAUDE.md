# CLAUDE.md — w_finder Revit 2025 Plugin

## Developer Context
- **Developer skill level:** Learning C# — needs guidance on syntax, patterns, and Revit API usage.
- **IDE:** Codium (AI-assisted). No Visual Studio. Build/test via `dotnet build` from CLI.
- **Workflow:** AI writes all code. Developer builds from terminal, tests in Revit, reports back. Iterate.
- Prefer simple, readable code over clever abstractions. This is a learning project too.
- When making changes, be explicit about which file is being modified and why.
- Explain non-obvious C# or Revit API patterns briefly so the developer can learn.
- If unsure about a Revit API detail, say so — the developer will test and report back.

## Environment & Paths
- **Revit 2025 install:** `C:\Program Files\Autodesk\Revit 2025\`
- **Revit API DLLs (reference in .csproj):**
  - `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`
  - `C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll`
  - `C:\Program Files\Autodesk\Revit 2025\AdWindows.dll` (for dockable pane / ribbon UI)
- **Project location:** `C:\Users\oliwer.weber\Documents\Coding\CustomRevitPlugins\w_finder\`
- **Target framework:** .NET 8.0 (Revit 2025 uses .NET 8)
- **Revit addins manifest folder:** `%AppData%\Autodesk\Revit\Addins\2025\`

## Project Structure

```
w_finder/
├── CLAUDE.md
├── w_finder.csproj
├── w_finder.addin
├── App.cs                    # IExternalApplication — registers ribbon button + dockable pane
├── FinderCommand.cs          # IExternalCommand — toggle dockable pane visibility
├── FinderDockablePane.cs     # IDockablePaneProvider — WPF host for the dockable pane
├── Views/
│   └── FinderPaneView.xaml   # WPF UI (search box, results list, favorites section)
│       └── FinderPaneView.xaml.cs
├── ViewModels/
│   └── FinderPaneViewModel.cs  # MVVM view model — search logic, favorites, selection
├── Models/
│   └── BrowserItem.cs        # Data model for project browser items
├── Services/
│   ├── BrowserItemCollector.cs # Collects all project browser items from the Revit model
│   ├── FuzzyMatcher.cs         # Fuzzy search / subsequence matching with scoring
│   └── FavoritesStore.cs       # Persistence of favorites per project (local + cloud)
├── Helpers/
│   ├── RevitBackgroundTask.cs  # ExternalEvent handler for safe Revit API calls from WPF
│   └── ThemeService.cs         # Light/dark mode state + persistence
└── Views/
    ├── LightTheme.xaml         # Light mode color resources
    └── DarkTheme.xaml          # Dark mode color resources
```

## .csproj Key Settings
- `<TargetFramework>net8.0-windows</TargetFramework>`
- `<UseWPF>true</UseWPF>`
- `<EnableDynamicLoading>true</EnableDynamicLoading>`
- All Revit DLL references: `<Private>false</Private>` (do NOT copy to output)
- Output to a `bin/` folder; the `.addin` file points here

## .addin Manifest
- Type: `Application` (since we use IExternalApplication for startup registration)
- Points to the built DLL path
- Unique AddInId GUID (generate once, keep stable)

## Plugin Specification

### Overview
**w_finder** is a dockable pane plugin that replaces/extends the Revit Project Browser with a fast fuzzy-search interface. Users can instantly find and navigate to any item that would appear in the project browser, without dealing with the browser's folder/sorting hierarchy.

### 1. Fuzzy Search (primary feature)
- Single search box at the top of the pane.
- Searches across **all project browser item categories**: views (floor plans, ceiling plans, 3D views, elevations, sections, drafting views, legends), sheets, schedules, families/types, groups, Revit links, assemblies — everything the project browser shows.
- Fuzzy matching (substring + tolerance for typos/partial matches). Rank results by relevance.
- Results displayed as a flat list (no tree/hierarchy). Each result shows:
  - Item name
  - Item category/type as a subtle secondary label (e.g., "Floor Plan", "Sheet", "Family")
  - A star/favorite toggle icon
- Search should be fast and feel instant — debounce input, run off-thread where possible.

### 2. Click Behavior
- **Single click** on a result → selects the item in Revit (equivalent to single-clicking in the project browser). The Properties palette should update to show the selected item's properties.
- **Double click** on a result → opens/activates the item. For views and sheets this means navigating to that view. For other items, perform whatever the default "open" action would be.

### 3. Favorites
- Each search result has a star icon to toggle favorite status.
- Favorites appear in a pinned section **above** the search results (always visible, collapsible).
- Favorites persist **per Revit project file** (use Extensible Storage on the ProjectInfo element, or a sidecar JSON file next to the .rvt — whichever is simpler to implement initially).
- Favorites also support single-click = select, double-click = open.

### 4. Dockable Pane
- Registered as a proper Revit dockable pane (IDockablePaneProvider).
- User can dock it anywhere (left, right, float, tab alongside project browser, etc.).
- Toggled on/off via a ribbon button in a custom "w_finder" tab (or an "Add-Ins" tab panel).
- Pane state (open/closed, dock position) managed by Revit automatically.

## Technical Constraints & Patterns
- **Thread safety:** All Revit API calls must happen on the Revit main thread. Use `ExternalEvent` + `IExternalEventHandler` pattern for any action triggered from WPF (clicks, etc.).
- **MVVM:** Keep WPF UI and Revit API logic separated. The ViewModel should not reference Revit API directly; use services/handlers.
- **Performance:** Cache the browser item list. Refresh on `DocumentChanged` or `ViewActivated` events (or a manual refresh button). Don't re-scan the entire model on every keystroke.
- **Revit 2025 API:** Use `UIDocument.RequestViewChange()` for navigating to views. Use `Selection.SetElementIds()` for selecting items. Use `DockablePaneProvider` for pane registration.

## Build & Test
```powershell
# Build:
dotnet build

# First time only — copy .addin to Revit's addins folder:
copy w_finder.addin "$env:APPDATA\Autodesk\Revit\Addins\2025\"

# Then open Revit 2025, load a project, test.
# Close Revit before rebuilding (it locks the DLL).
```

## Development Phases
1. **Phase 1 — Skeleton:** .csproj, .addin, App.cs with ribbon button, empty dockable pane that opens/closes. Verify it loads in Revit.
2. **Phase 2 — Browser item collection:** BrowserItemCollector gathers all project browser items into a flat list of BrowserItem models.
3. **Phase 3 — Search UI:** WPF search box + results list with fuzzy matching. Single-click selection working.
4. **Phase 4 — Navigation:** Double-click to open/navigate. ExternalEvent plumbing.
5. **Phase 5 — Favorites:** Star toggle, favorites section, persistence.
6. **Phase 6 — Polish:** Performance tuning, icons, keyboard shortcuts, edge cases.

## Current Status
**Phase:** 6 complete — all phases done. Plugin is feature-complete and ready for internal distribution.

### Completed
- **Phase 1 — Skeleton:** Ribbon tab + button, dockable pane registered, placeholder UI. Loads in Revit.
- **Phase 2 — Browser item collection:** BrowserItemCollector gathers views, sheets, schedules, families/types, groups, Revit links, assemblies into flat BrowserItem list.
- **Phase 3 — Search UI:** Editable search box with 150ms debounce, fuzzy matching (subsequence + scoring), results list with name + category labels, single-click selects item in Revit via ExternalEvent.
- **Phase 4 — Navigation:** Double-click opens/navigates to views, sheets, and schedules via `RequestViewChange()`.
- **Phase 5 — Favorites:** Star toggle on each item, collapsible favorites section above results, persistence via sidecar JSON (local models) or AppData with cloud model GUIDs (ACC/BIM 360).
- **Phase 6 — Polish:** Light/dark theme with manual toggle (persists in AppData), Segoe UI font, styled search box with placeholder text, hover/selected states on list items, muted star colors, magnifying glass ribbon icon drawn via WPF, Revit-native look and feel.

### Known Issues / Notes
- `RevitBackgroundTask` only holds one pending action at a time — favorites save bypasses ExternalEvent (pure file I/O) to avoid conflicts with selection events.
- MSB3277 warnings during build are normal (Revit DLL version conflicts with .NET 8 runtime).
- Must close Revit before rebuilding (DLL locks the DLL).
- Cloud model favorites stored at `%AppData%\w_finder\favorites\{projectGuid}_{modelGuid}.json`.
- Local model favorites stored as sidecar file: `MyProject.rvt.wfinder-favorites.json`.
- Theme preference stored at `%AppData%\w_finder\theme.txt`.
- Auto-detection of Revit dark mode not reliable — using manual toggle instead.
