using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using w_finder.Models;
using w_finder.Services;
using QuickAction = w_finder.Models.QuickAction;

namespace w_finder.ViewModels;

/// <summary>
/// Active mode enum — replaces the old prefix-based booleans.
/// </summary>
public enum ActiveMode
{
    Browser,
    Place,
    Command,
    Shebang
}

/// <summary>
/// What the pane is currently showing. "Search" is the normal list (in any
/// of the ActiveMode prefixes); the others are quips that take over the pane.
/// Exactly one is active at a time, which is what makes Escape, close and
/// reset behave the same everywhere.
/// </summary>
public enum PaneSurface
{
    Search,
    Inspector,
    ParamFilter,
    Report
}

/// <summary>
/// Two-stage family navigation state.
/// </summary>
public enum FamilyNavigationStage
{
    FamilyLevel,
    TypeLevel
}

/// <summary>
/// The four input fields of the Parameter Filter quip; tracks which one
/// the typeahead is currently driving.
/// </summary>
public enum ParamFilterField
{
    Scope,
    Parameter,
    Rule,
    Value
}

/// <summary>
/// ObservableCollection that supports bulk replacement with a single Reset notification.
/// Avoids per-item UI updates when repopulating large lists.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces all items with the given list, firing a single CollectionChanged Reset.
    /// </summary>
    public void ReplaceAll(IList<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
    }
}

/// <summary>
/// ViewModel for the finder pane. Holds the full item cache, search text, and filtered results.
/// </summary>
public class FinderPaneViewModel : INotifyPropertyChanged
{
    private List<BrowserItem> _allItems = new();
    private List<BrowserItem> _commandItems = new();
    private List<BrowserItem> _shebangItems = new();
    private HashSet<long> _favoriteIds = new();
    private readonly DispatcherTimer _debounceTimer;
    private (string query, string? categoryFilter)? _cachedFamilyInput;

    public FinderPaneViewModel()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RefreshResults();
        };
    }

    // ── Mode Pill (consumed prefix) ─────────────────────────────────

    private ActiveMode _activeMode = ActiveMode.Browser;
    public ActiveMode ActiveMode
    {
        get => _activeMode;
        set
        {
            if (_activeMode == value) return;
            _activeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBrowserMode));
            OnPropertyChanged(nameof(IsPlaceFamilyMode));
            OnPropertyChanged(nameof(IsFamilyMode));
            OnPropertyChanged(nameof(IsCommandMode));
            OnPropertyChanged(nameof(IsShebangMode));
            OnPropertyChanged(nameof(ShowFavorites));
            OnPropertyChanged(nameof(IsEmptyState));
            OnPropertyChanged(nameof(ModePillText));
            OnPropertyChanged(nameof(PlaceholderText));
            OnPropertyChanged(nameof(HasModePill));
        }
    }

    /// <summary>
    /// The display text for the mode pill chip inside the search box.
    /// </summary>
    public string ModePillText => IsInReportMode
        ? ReportPillText
        : IsInParameterFilterMode
        ? "PARAMS"
        : IsInInspectorMode
        ? "INSPECT"
        : ActiveMode switch
        {
            ActiveMode.Place => "FAMILY",
            ActiveMode.Command => "PALETTE",
            ActiveMode.Shebang => "QUIPS",
            _ => "BROWSER"
        };

    /// <summary>
    /// True when a mode pill should be shown — i.e. a prefix scope is active, or the
    /// pane is in a special overlay state. Hidden in the default unified Browser surface
    /// so the search bar is uncluttered when no scope is narrowed.
    /// </summary>
    public bool HasModePill =>
        Surface != PaneSurface.Search
        || ActiveMode != ActiveMode.Browser;

    // ── Surface (which screen owns the pane) ────────────────────────

    private PaneSurface _surface = PaneSurface.Search;
    public PaneSurface Surface
    {
        get => _surface;
        private set
        {
            if (_surface == value) return;
            _surface = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInReportMode));
            OnPropertyChanged(nameof(IsInInspectorMode));
            OnPropertyChanged(nameof(IsInParameterFilterMode));
            OnPropertyChanged(nameof(ModePillText));
            OnPropertyChanged(nameof(HasModePill));
            OnPropertyChanged(nameof(PlaceholderText));
            OnPropertyChanged(nameof(ShowFavorites));
            OnPropertyChanged(nameof(IsEmptyState));
            NotifyFooterHint();
        }
    }

    public bool IsInReportMode => Surface == PaneSurface.Report;
    public bool IsInInspectorMode => Surface == PaneSurface.Inspector;
    public bool IsInParameterFilterMode => Surface == PaneSurface.ParamFilter;

    /// <summary>
    /// The single hint line at the bottom of the pane while a quip is open.
    /// Empty on the search surface (the action bar lives there instead).
    /// </summary>
    public string FooterHint => Surface switch
    {
        PaneSurface.Report => ReportFooterHint,
        PaneSurface.Inspector => InspectorFooterHint,
        PaneSurface.ParamFilter => ParamFilterFooterHint,
        _ => string.Empty
    };

    public bool HasFooterHint => Surface != PaneSurface.Search;

    private void NotifyFooterHint()
    {
        OnPropertyChanged(nameof(FooterHint));
        OnPropertyChanged(nameof(HasFooterHint));
    }

    /// <summary>
    /// Leaves whatever quip is open and returns to the search list.
    /// </summary>
    public void ExitSurface()
    {
        if (Surface == PaneSurface.Search) return;

        ClearSurfaceState();
        Surface = PaneSurface.Search;

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        _debounceTimer.Stop();
        RefreshResults();
    }

    /// <summary>Drops the data of every quip. Does not change <see cref="Surface"/>.</summary>
    private void ClearSurfaceState()
    {
        // Report
        _reportStack.Clear();
        _reportPage = null;
        _reportDrillProvider = null;
        _reportRowMap.Clear();

        // Inspector
        IsInspectorEditMode = false;
        EditingInspectorParam = null;
        _inspectorParams = new();
        _inspectorParamMap.Clear();
        _inspectorNavStack.Clear();
        InspectorElementIds = new();
        InspectorIsMixedCategory = false;
        InspectorDrilledCategory = null;
        InspectorBreadcrumb = "INSPECT";

        // Parameter filter
        _paramFilterAllParams = new();
        _paramFilterAllValues = new();
        _paramFilterParameterName = null;
        _paramFilterRuleValue = null;
        _paramFilterValueText = string.Empty;
        _paramFilterFocus = ParamFilterField.Scope;
    }

    /// <summary>
    /// Escape. Undoes exactly one layer: an open edit/rename box, the action
    /// bar, one drill level, then the quip itself. Returns false when there is
    /// nothing left to step out of, which tells the view to close the pane.
    /// </summary>
    public bool StepBack()
    {
        if (IsInspectorEditMode) { CancelInspectorEdit(); return true; }
        if (IsInlineRenameVisible) { CloseInlineRename(); return true; }
        if (IsActionBarVisible) { CloseActionBar(); return true; }
        if (IsChipNavigationActive) { ExitChipNavigation(); return true; }

        switch (Surface)
        {
            case PaneSurface.Report:
                if (!ReportGoBack()) ExitSurface();
                return true;

            case PaneSurface.Inspector:
                if (InspectorDrilledCategory != null) InspectorGoBack();
                else ExitSurface();
                return true;

            case PaneSurface.ParamFilter:
                // The four fields are one form, not four layers.
                ExitSurface();
                return true;
        }

        if (FamilyNavigationStage == FamilyNavigationStage.TypeLevel)
        {
            NavigateBackToFamilies();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Left arrow / Backspace on an empty box: go up one drill level.
    /// Unlike <see cref="StepBack"/> this never leaves a quip or closes the
    /// pane. Returns false at the top level.
    /// </summary>
    public bool GoBackLevel()
    {
        switch (Surface)
        {
            case PaneSurface.Report:
                return ReportGoBack();

            case PaneSurface.Inspector:
                if (InspectorDrilledCategory == null) return false;
                InspectorGoBack();
                return true;

            case PaneSurface.ParamFilter:
                if (ParamFilterFocus == ParamFilterField.Scope) return false;
                ParamFilterMoveFocus(-1);
                return true;
        }

        if (FamilyNavigationStage == FamilyNavigationStage.TypeLevel)
        {
            NavigateBackToFamilies();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Puts the pane back on a plain search list: no quip, no rename box, no
    /// action bar, no family drill-down. Called whenever the pane closes and
    /// again when it opens, so it can never reopen stuck inside something.
    /// </summary>
    /// <param name="refresh">
    /// Pass false when the caller is about to reload and refresh the list
    /// itself (opening the pane), to avoid searching everything twice.
    /// </param>
    public void ResetTransientState(bool refresh = true)
    {
        CloseInlineRename();
        CloseActionBar();
        ExitChipNavigation();

        if (Surface != PaneSurface.Search)
        {
            ClearSurfaceState();
            Surface = PaneSurface.Search;
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
        }

        if (FamilyNavigationStage != FamilyNavigationStage.FamilyLevel)
        {
            SelectedFamilyForDrilldown = null;
            FamilyNavigationStage = FamilyNavigationStage.FamilyLevel;
            _searchText = _searchBeforeDrill;
            _cachedFamilyInput = null;
            OnPropertyChanged(nameof(SearchText));
        }

        _debounceTimer.Stop();
        if (refresh) RefreshResults();
    }

    /// <summary>
    /// Dynamic placeholder text for the search box, based on the active mode.
    /// </summary>
    public string PlaceholderText => IsInReportMode
        ? "Filter rows\u2026"
        : IsInParameterFilterMode
        ? ParamFilterFocus switch
        {
            ParamFilterField.Scope => "Pick scope\u2026",
            ParamFilterField.Parameter => "Type parameter name\u2026",
            ParamFilterField.Rule => "Pick rule\u2026",
            ParamFilterField.Value => "Type or pick value\u2026",
            _ => "Parameter filter\u2026"
        }
        : IsInInspectorMode
        ? "Filter parameters\u2026"
        : FamilyNavigationStage == FamilyNavigationStage.TypeLevel
        ? "Filter types\u2026"
        : ActiveMode switch
        {
            ActiveMode.Place => "Search families to place or edit\u2026",
            ActiveMode.Command => "Search Revit commands\u2026",
            ActiveMode.Shebang => "Search quips to run\u2026",
            _ => "Search views, sheets, and more\u2026"
        };

    /// <summary>
    /// Returns all possible pill text strings (for measuring uniform pill width at startup).
    /// </summary>
    public static string[] AllPillTexts => new[]
    {
        "BROWSER", "FAMILY", "PALETTE", "QUIPS", "FILTER", "INSPECT", "PARAMS", "CAD", "PATTERNS", "TYPES"
    };

    /// <summary>
    /// Called by the view when the user types a prefix character.
    /// The prefix is consumed — it does not appear in SearchText.
    /// </summary>
    public void ActivateMode(ActiveMode mode)
    {
        ActiveMode = mode;
        // Clear search text since prefix is consumed
        _searchText = string.Empty;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        _debounceTimer.Stop();
        RefreshResults();
    }

    /// <summary>
    /// Called when the user backspaces on empty text while a pill is active.
    /// Returns to Browser mode.
    /// </summary>
    public void DeactivateMode()
    {
        _familyNavigationStage = FamilyNavigationStage.FamilyLevel;
        _selectedFamilyForDrilldown = null;
        ActiveMode = ActiveMode.Browser;
        _searchText = string.Empty;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(FamilyNavigationStage));
        _debounceTimer.Stop();
        RefreshResults();
    }

    // ── Search Text ─────────────────────────────────────────────────

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            _cachedFamilyInput = null;
            ExitChipNavigation();

            // The action bar belongs to the row that was highlighted; once the
            // list is about to change under it, it would act on a stale row.
            if (IsActionBarVisible) CloseActionBar();

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmptyState));
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private string _statusText = "Open a project and click the Quip button.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private BrowserItem? _selectedItem;
    public BrowserItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            OnPropertyChanged();
            OnItemSelected();
        }
    }

    private BrowserItem? _highlightedItem;
    public BrowserItem? HighlightedItem
    {
        get => _highlightedItem;
        set { _highlightedItem = value; OnPropertyChanged(); }
    }

    private bool _hasFavorites;
    public bool HasFavorites
    {
        get => _hasFavorites;
        set { _hasFavorites = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowFavorites)); }
    }

    // ── Mode detection (backwards-compatible properties) ────────────

    public bool IsFamilyMode => ActiveMode == ActiveMode.Place;
    public bool IsCommandMode => ActiveMode == ActiveMode.Command;
    public bool IsShebangMode => ActiveMode == ActiveMode.Shebang;
    public bool IsPlaceFamilyMode => ActiveMode == ActiveMode.Place;
    public bool IsBrowserMode => ActiveMode == ActiveMode.Browser;

    /// <summary>
    /// When true, Family Mode only shows types that have placed instances in the project.
    /// </summary>
    public bool FilterPlacedTypes { get; set; }
    public bool ShowFavorites => HasFavorites && IsBrowserMode && Surface == PaneSurface.Search;

    // ── Empty state ─────────────────────────────────────────────────

    public bool IsEmptyState => Surface == PaneSurface.Search
        && string.IsNullOrWhiteSpace(_searchText)
        && FamilyNavigationStage == FamilyNavigationStage.FamilyLevel;

    /// <summary>
    /// Available categories for Place mode empty state chips.
    /// </summary>
    public ObservableCollection<string> AvailableCategories { get; } = new();

    // ── Chip Navigation ──────────────────────────────────────────────

    private bool _isChipNavigationActive;
    public bool IsChipNavigationActive
    {
        get => _isChipNavigationActive;
        set { _isChipNavigationActive = value; OnPropertyChanged(); }
    }

    private int _focusedChipIndex = -1;
    public int FocusedChipIndex
    {
        get => _focusedChipIndex;
        set { _focusedChipIndex = value; OnPropertyChanged(); }
    }

    private bool _areCategoryChipsExpanded;
    public bool AreCategoryChipsExpanded
    {
        get => _areCategoryChipsExpanded;
        set { _areCategoryChipsExpanded = value; OnPropertyChanged(); }
    }

    public int VisibleChipCount { get; set; }

    public void EnterChipNavigation()
    {
        if (VisibleChipCount <= 0) return;
        IsChipNavigationActive = true;
        FocusedChipIndex = 0;
        HighlightedItem = null;
    }

    public void ExitChipNavigation()
    {
        IsChipNavigationActive = false;
        FocusedChipIndex = -1;
    }

    /// <summary>
    /// Moves chip focus left or right. Returns false if at boundary.
    /// </summary>
    public bool MoveChipFocus(int direction)
    {
        int newIndex = FocusedChipIndex + direction;
        if (newIndex < 0 || newIndex >= VisibleChipCount) return false;
        FocusedChipIndex = newIndex;
        return true;
    }

    public void ExpandCategoryChips()
    {
        AreCategoryChipsExpanded = true;
    }

    // ── Two-Stage Family Navigation ─────────────────────────────────

    private FamilyNavigationStage _familyNavigationStage = FamilyNavigationStage.FamilyLevel;
    public FamilyNavigationStage FamilyNavigationStage
    {
        get => _familyNavigationStage;
        set
        {
            _familyNavigationStage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmptyState));
            OnPropertyChanged(nameof(PlaceholderText));
        }
    }

    /// <summary>What was typed before drilling into a family, restored on the way back.</summary>
    private string _searchBeforeDrill = string.Empty;

    private string? _selectedFamilyForDrilldown;
    public string? SelectedFamilyForDrilldown
    {
        get => _selectedFamilyForDrilldown;
        set { _selectedFamilyForDrilldown = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Drills down into a family, showing its types in Stage 2.
    /// </summary>
    public void DrillIntoFamily(BrowserItem familyItem)
    {
        if (familyItem.FamilyName == null) return;
        _searchBeforeDrill = _searchText;
        SelectedFamilyForDrilldown = familyItem.FamilyName;
        FamilyNavigationStage = FamilyNavigationStage.TypeLevel;
        // Empty box: the family name is already in the header and back row,
        // and an empty box means typing filters the types and Left /
        // Backspace go straight back, the same as inside any quip.
        _searchText = string.Empty;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        _debounceTimer.Stop();
        RefreshResults();
    }

    /// <summary>
    /// Returns from Stage 2 (type level) back to Stage 1 (family level).
    /// </summary>
    public void NavigateBackToFamilies()
    {
        SelectedFamilyForDrilldown = null;
        FamilyNavigationStage = FamilyNavigationStage.FamilyLevel;
        // Back to the list you drilled from, not to an empty box.
        _searchText = _searchBeforeDrill;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        RefreshResults();
    }

    // ── In-Pane Report Mode ─────────────────────────────────────────
    //
    // A generic, read-only "screen stack" rendered in the results list. Used by
    // the !dwg and !lp quips so those never have to open a TaskDialog. Pages are
    // plain data (ReportPage/ReportRow); anything Revit-specific is built by the
    // caller and handed in.

    /// <summary>Pill text for the current report, e.g. "CAD" or "PATTERNS".</summary>
    private string _reportPillText = "REPORT";
    public string ReportPillText
    {
        get => _reportPillText;
        private set { _reportPillText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModePillText)); }
    }

    /// <summary>
    /// Which accent colour family the pill uses for this report: "Shebang"
    /// (purple, the default) or "Filter" (gold, used by the Selection Filter).
    /// </summary>
    private string _reportAccent = "Shebang";
    public string ReportAccent
    {
        get => _reportAccent;
        private set { _reportAccent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Pages behind the current one, each with the row the cursor was on, so
    /// going back lands where you came from instead of at the top.
    /// </summary>
    private readonly Stack<(ReportPage Page, int CursorIndex)> _reportStack = new();
    private ReportPage? _reportPage;

    /// <summary>Maps synthetic result IDs back to the row they came from.</summary>
    private readonly Dictionary<long, ReportRow> _reportRowMap = new();

    /// <summary>
    /// Builds a child page for a drillable row. Supplied when entering the mode
    /// so page construction stays with whoever owns the data.
    /// </summary>
    private Func<ReportRow, ReportPage?>? _reportDrillProvider;

    /// <summary>
    /// Built from what the page can actually do, so every report describes its
    /// keys in the same words and never advertises a key that does nothing.
    /// </summary>
    public string ReportFooterHint
    {
        get
        {
            var page = _reportPage;
            if (!IsInReportMode || page == null) return string.Empty;

            var parts = new List<string>();

            if (page.Rows.Any(r => r.IsCheckable))
                parts.Add("Space: tick · Ctrl+A/D: all/none");
            if (page.Rows.Any(r => r.DrillKey != null))
                parts.Add("Enter/→: open");
            else if (page.Rows.Any(r => r.ActionKey != null || r.RevitElementId != 0))
                parts.Add("Enter: run");
            if (page.PrimaryActionKey != null)
                parts.Add($"Ctrl+Enter: {page.PrimaryActionLabel}");

            parts.Add(_reportStack.Count > 0 ? "Esc/←: back" : "Esc: close quip");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>True when Escape should pop a page instead of leaving the mode.</summary>
    public bool ReportCanGoBack => _reportStack.Count > 0;

    /// <summary>The page currently on screen. Null when not in report mode.</summary>
    public ReportPage? CurrentReportPage => _reportPage;

    /// <summary>
    /// Drops the back history. Used after an action changes the model, when the
    /// pages behind describe a state that no longer exists.
    /// </summary>
    public void ClearReportStack() => _reportStack.Clear();

    /// <summary>Fired when Enter lands on a row carrying an ActionKey.</summary>
    public event Action<ReportRow>? ReportActionRequested;

    /// <summary>Fired when Enter lands on a row carrying a Revit element ID.</summary>
    public event Action<long>? ReportElementActivated;

    /// <summary>
    /// Opens the report surface on <paramref name="root"/>.
    /// </summary>
    public void EnterReportMode(ReportPage root, string pillText,
                                Func<ReportRow, ReportPage?>? drillProvider = null,
                                string accent = "Shebang")
    {
        ClearSurfaceState();
        _reportPage = root;
        _reportDrillProvider = drillProvider;
        ReportPillText = pillText;
        ReportAccent = accent;

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        HighlightedItem = null;

        Surface = PaneSurface.Report;
        RefreshReportDisplay();
    }

    /// <summary>Swaps the current page in place. Used to show an action's result.</summary>
    public void ReplaceReportPage(ReportPage page) => ShowReportPage(page, -1);

    /// <summary>
    /// Puts <paramref name="page"/> on screen with the cursor on
    /// <paramref name="cursorIndex"/> (or on the first real row when negative).
    /// </summary>
    private void ShowReportPage(ReportPage page, int cursorIndex)
    {
        // A new page means the old cursor position is meaningless.
        HighlightedItem = null;
        _reportPage = page;
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        RefreshReportDisplay();

        if (cursorIndex >= 0 && cursorIndex < Results.Count)
            HighlightedItem = Results[cursorIndex];
    }

    /// <summary>Pushes a child page, keeping the current one for Escape.</summary>
    public void PushReportPage(ReportPage page)
    {
        if (_reportPage != null)
        {
            int cursor = HighlightedItem != null ? Results.IndexOf(HighlightedItem) : -1;
            // The cursor index is only valid against the unfiltered page.
            if (!string.IsNullOrWhiteSpace(_searchText)) cursor = -1;
            _reportStack.Push((_reportPage, cursor));
        }
        ShowReportPage(page, -1);
    }

    /// <summary>Pops back one page. Returns false when already at the root.</summary>
    public bool ReportGoBack()
    {
        if (_reportStack.Count == 0) return false;
        var (page, cursor) = _reportStack.Pop();
        ShowReportPage(page, cursor);
        return true;
    }

    /// <summary>The report row behind the highlighted list item, if any.</summary>
    private ReportRow? HighlightedReportRow()
    {
        if (HighlightedItem == null) return null;
        _reportRowMap.TryGetValue(HighlightedItem.ElementId, out var row);
        return row;
    }

    /// <summary>
    /// Enter (or double-click) on the highlighted report row: the row's main
    /// action. That is go back, open, run, select in model, or, for a plain
    /// tick row, tick it. Returns true if the row did something.
    /// </summary>
    public bool ActivateHighlightedReportRow()
    {
        if (HighlightedItem == null) return false;

        if (HighlightedItem.IsBackRow) return ReportGoBack();

        var row = HighlightedReportRow();
        if (row == null) return false;

        if (row.DrillKey != null) return DrillHighlightedReportRow();

        if (row.ActionKey != null)
        {
            ReportActionRequested?.Invoke(row);
            return true;
        }

        if (row.RevitElementId != 0)
        {
            ReportElementActivated?.Invoke(row.RevitElementId);
            return true;
        }

        return ToggleHighlightedReportRow();
    }

    /// <summary>
    /// Right arrow: open the highlighted row's child page. Deliberately does
    /// nothing on action rows, so an arrow key can never delete anything.
    /// </summary>
    public bool DrillHighlightedReportRow()
    {
        var row = HighlightedReportRow();
        if (row?.DrillKey == null) return false;

        var child = _reportDrillProvider?.Invoke(row);
        if (child == null) return false;

        PushReportPage(child);
        return true;
    }

    /// <summary>
    /// Ctrl+Enter: run the page's primary action (apply, delete selected...)
    /// from wherever the cursor is. Returns false when the page has none.
    /// </summary>
    public bool ActivateReportPrimaryAction()
    {
        var key = _reportPage?.PrimaryActionKey;
        if (key == null) return false;

        var row = _reportPage!.Rows.FirstOrDefault(r => r.ActionKey == key);
        if (row == null) return false;

        ReportActionRequested?.Invoke(row);
        return true;
    }

    /// <summary>True when the current page has at least one tick row.</summary>
    public bool ReportHasCheckableRows => _reportPage?.Rows.Any(r => r.IsCheckable) ?? false;

    /// <summary>
    /// Toggles the checkbox on the highlighted row. Returns false when the row
    /// isn't checkable, so the caller can let the keypress through.
    /// </summary>
    public bool ToggleHighlightedReportRow()
    {
        var row = HighlightedReportRow();
        if (row == null || !row.IsCheckable) return false;

        if (row.OnToggle != null) row.OnToggle(row);
        else row.IsChecked = !(row.IsChecked ?? false);

        RefreshReportDisplay();
        return true;
    }

    /// <summary>
    /// Checks or unchecks every checkable row on the current page — including
    /// ones the search box is currently filtering out, so the count never lies.
    /// </summary>
    public void SetAllReportChecks(bool isChecked)
    {
        if (_reportPage == null) return;

        if (_reportPage.SetAllChecks != null)
        {
            _reportPage.SetAllChecks(isChecked);
        }
        else
        {
            foreach (var row in _reportPage.Rows.Where(r => r.IsCheckable))
                row.IsChecked = isChecked;
        }

        RefreshReportDisplay();
    }

    /// <summary>
    /// Re-renders the current page. For callers that changed state the page
    /// reads but didn't go through a checkbox toggle.
    /// </summary>
    public void RefreshCurrentReportPage() => RefreshReportDisplay();

    /// <summary>Projects the current page (filtered by the search box) into Results.</summary>
    private void RefreshReportDisplay()
    {
        _reportRowMap.Clear();

        int prevHighlightIndex = HighlightedItem != null ? Results.IndexOf(HighlightedItem) : -1;

        var page = _reportPage;
        if (page == null)
        {
            Results.ReplaceAll(new List<BrowserItem>());
            HighlightedItem = null;
            return;
        }

        // Let the page refresh any row that depends on checkbox state.
        page.Recalculate?.Invoke(page);

        var rows = page.Rows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.ToLowerInvariant();
            rows = rows.Where(r =>
                r.Text.ToLowerInvariant().Contains(query)
                || (r.Label?.ToLowerInvariant().Contains(query) ?? false)
                || (r.Value?.ToLowerInvariant().Contains(query) ?? false));
        }

        var rowList = rows.ToList();

        var items = new List<BrowserItem>();
        long nextId = -5000;

        if (page.BackLabel != null)
        {
            items.Add(new BrowserItem
            {
                Name = $"← {page.BackLabel}",
                Category = "",
                ElementId = nextId--,
                Kind = BrowserItemKind.Shebang, // hides the favourite star
                IsBackRow = true,
                // Share the first row's section so the back row doesn't spawn
                // an empty header above it.
                GroupKey = rowList.Count > 0 ? rowList[0].GroupKey : "",
            });
        }

        foreach (var row in rowList)
        {
            var id = nextId--;
            items.Add(new BrowserItem
            {
                Name = row.Text,
                Category = row.Value ?? "",
                ElementId = id,
                Kind = BrowserItemKind.Shebang,
                DetailLabel = row.Label,
                GroupKey = row.GroupKey,
                // Same Segoe MDL2 glyphs the selection filter uses.
                CheckGlyph = row.IsCheckable
                    ? row.IsChecked switch
                    {
                        true => "\uE73A",   // ticked
                        false => "\uE739",  // empty
                        null => "\uE73C",   // partially ticked
                    }
                    : null,
            });
            _reportRowMap[id] = row;
        }

        Results.ReplaceAll(items);

        StatusText = page.Breadcrumb;

        // Hold the cursor in place across a re-render (checkbox toggles), and
        // otherwise land on the first real row rather than the back row.
        if (prevHighlightIndex >= 0 && prevHighlightIndex < items.Count)
            HighlightedItem = items[prevHighlightIndex];
        else
            HighlightedItem =
                items.FirstOrDefault(i => page.InitialCursorRow != null
                    && _reportRowMap.TryGetValue(i.ElementId, out var r)
                    && ReferenceEquals(r, page.InitialCursorRow))
                ?? items.FirstOrDefault(i => !i.IsBackRow)
                ?? items.FirstOrDefault();

        OnPropertyChanged(nameof(ReportCanGoBack));
        NotifyFooterHint();
    }

    // ── Property Inspector Mode ─────────────────────────────────────

    private List<InspectorParameterItem> _inspectorParams = new();
    private Dictionary<long, InspectorParameterItem> _inspectorParamMap = new();
    private Stack<(List<InspectorParameterItem> items, string breadcrumb, bool isMixed)> _inspectorNavStack = new();

    /// <summary>Element IDs of the inspected elements (for write-back and refresh).</summary>
    public List<long> InspectorElementIds { get; set; } = new();

    /// <summary>Total number of inspected elements.</summary>
    public int InspectorElementCount { get; set; }

    /// <summary>True when in mixed-category mode showing the category picker.</summary>
    public bool InspectorIsMixedCategory { get; set; }

    /// <summary>Category name currently drilled into (for refresh after edit).</summary>
    public string? InspectorDrilledCategory { get; set; }

    private string _inspectorBreadcrumb = "INSPECT";
    public string InspectorBreadcrumb
    {
        get => _inspectorBreadcrumb;
        set { _inspectorBreadcrumb = value; OnPropertyChanged(); }
    }

    public string InspectorFooterHint
    {
        get
        {
            if (!IsInInspectorMode) return string.Empty;
            if (IsInspectorEditMode)
                return "Enter: save \u00b7 Esc: cancel \u00b7 Tab: next \u00b7 Shift+Tab: prev";
            if (InspectorIsMixedCategory)
                return "Enter/\u2192: open \u00b7 Esc: close quip";
            return InspectorDrilledCategory != null
                ? "Enter: edit \u00b7 Space: toggle yes/no \u00b7 Esc/\u2190: back"
                : "Enter: edit \u00b7 Space: toggle yes/no \u00b7 Esc: close quip";
        }
    }

    // ── Inspector Edit Mode ──────────────────────────────────────

    private bool _isInspectorEditMode;
    public bool IsInspectorEditMode
    {
        get => _isInspectorEditMode;
        set
        {
            if (_isInspectorEditMode == value) return;
            _isInspectorEditMode = value;
            OnPropertyChanged();
            NotifyFooterHint();
        }
    }

    private string _inspectorEditValue = string.Empty;
    public string InspectorEditValue
    {
        get => _inspectorEditValue;
        set { _inspectorEditValue = value; OnPropertyChanged(); }
    }

    private InspectorParameterItem? _editingInspectorParam;
    public InspectorParameterItem? EditingInspectorParam
    {
        get => _editingInspectorParam;
        set { _editingInspectorParam = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Fired when an inspector edit is confirmed. Code-behind writes to Revit.
    /// Args: (paramItem, newValue)
    /// </summary>
    public event Action<InspectorParameterItem, string>? InspectorEditConfirmed;

    /// <summary>
    /// Enters inspector mode with the given parameter list.
    /// </summary>
    public void EnterInspectorMode(List<InspectorParameterItem> items, string breadcrumb,
                                   bool isMixedCategory, List<long> elementIds)
    {
        ClearSurfaceState();
        _inspectorParams = items;
        InspectorElementIds = elementIds;
        InspectorElementCount = elementIds.Count;
        InspectorIsMixedCategory = isMixedCategory;
        InspectorBreadcrumb = $"INSPECT \u00b7 {breadcrumb}";

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        HighlightedItem = null;

        Surface = PaneSurface.Inspector;
        RefreshInspectorDisplay();
        NotifyFooterHint();
    }

    /// <summary>
    /// Drills into a category in mixed-category mode.
    /// </summary>
    public void InspectorDrillIn(InspectorParameterItem catItem)
    {
        if (!catItem.IsCategoryRow) return;

        // Push current state onto nav stack
        _inspectorNavStack.Push((_inspectorParams, InspectorBreadcrumb, InspectorIsMixedCategory));
        InspectorDrilledCategory = catItem.CategoryName;

        // The code-behind will call back with the category's parameters
        // For now, signal that we need a category drill-in
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
    }

    /// <summary>
    /// Updates the inspector with drilled-in category parameters.
    /// Called from code-behind after ParameterCollector.CollectForCategory().
    /// </summary>
    public void InspectorShowCategoryParams(List<InspectorParameterItem> items, string breadcrumb)
    {
        _inspectorParams = items;
        InspectorIsMixedCategory = false;
        InspectorBreadcrumb = $"INSPECT \u00b7 {breadcrumb}";
        HighlightedItem = null;
        RefreshInspectorDisplay();
        NotifyFooterHint();
    }

    /// <summary>
    /// Goes back from a category drill-in to the category list.
    /// </summary>
    public void InspectorGoBack()
    {
        if (_inspectorNavStack.Count == 0) return;

        var (items, breadcrumb, isMixed) = _inspectorNavStack.Pop();
        _inspectorParams = items;
        InspectorIsMixedCategory = isMixed;
        InspectorDrilledCategory = null;
        InspectorBreadcrumb = breadcrumb;

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        HighlightedItem = null;
        RefreshInspectorDisplay();
        NotifyFooterHint();
    }

    /// <summary>
    /// Enters edit mode for the given parameter.
    /// </summary>
    public void EnterInspectorEdit(InspectorParameterItem param)
    {
        if (param.IsReadOnly || param.IsDivider || param.IsCategoryRow) return;

        // ElementId params not editable in V1
        if (param.StorageType == 4) // StorageType.ElementId
        {
            ShowInlineError("This parameter points at another element and can't be edited here yet");
            return;
        }

        EditingInspectorParam = param;
        InspectorEditValue = param.IsVaries ? "" : param.DisplayValue;
        IsInspectorEditMode = true;
    }

    /// <summary>
    /// Confirms the current inspector edit.
    /// </summary>
    public void ConfirmInspectorEdit()
    {
        if (EditingInspectorParam == null) return;
        InspectorEditConfirmed?.Invoke(EditingInspectorParam, InspectorEditValue);
        IsInspectorEditMode = false;
        EditingInspectorParam = null;
    }

    /// <summary>
    /// Cancels the current inspector edit.
    /// </summary>
    public void CancelInspectorEdit()
    {
        IsInspectorEditMode = false;
        EditingInspectorParam = null;
        InspectorEditValue = string.Empty;
    }

    /// <summary>
    /// Commits edit and moves to next/previous editable parameter.
    /// direction: +1 for next, -1 for previous.
    /// </summary>
    public void CommitAndMoveInspectorEdit(int direction)
    {
        if (EditingInspectorParam == null) return;

        // Commit current edit
        InspectorEditConfirmed?.Invoke(EditingInspectorParam, InspectorEditValue);

        // Find next editable parameter
        var currentId = HighlightedItem?.ElementId ?? -1;
        var unified = Results.ToList();
        int currentIdx = -1;
        for (int i = 0; i < unified.Count; i++)
        {
            if (unified[i].ElementId == currentId)
            {
                currentIdx = i;
                break;
            }
        }

        // Search in direction for next editable param
        IsInspectorEditMode = false;
        EditingInspectorParam = null;

        if (currentIdx < 0) return;

        for (int i = currentIdx + direction; i >= 0 && i < unified.Count; i += direction)
        {
            var item = unified[i];
            if (_inspectorParamMap.TryGetValue(item.ElementId, out var nextParam))
            {
                if (!nextParam.IsReadOnly && !nextParam.IsDivider && !nextParam.IsCategoryRow
                    && nextParam.StorageType != 4) // not ElementId
                {
                    HighlightedItem = item;
                    EnterInspectorEdit(nextParam);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Replaces the current inspector params (after edit refresh) and preserves highlight.
    /// </summary>
    public void RefreshInspectorParams(List<InspectorParameterItem> newParams)
    {
        // Remember what parameter was highlighted
        string? highlightedParamName = null;
        if (HighlightedItem != null && _inspectorParamMap.TryGetValue(HighlightedItem.ElementId, out var prev))
            highlightedParamName = prev.Name;

        _inspectorParams = newParams;
        RefreshInspectorDisplay();

        // Restore highlight by parameter name
        if (highlightedParamName != null)
        {
            foreach (var item in Results)
            {
                if (_inspectorParamMap.TryGetValue(item.ElementId, out var param) && param.Name == highlightedParamName)
                {
                    HighlightedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Builds BrowserItem wrappers for the current inspector state and populates Results.
    /// </summary>
    private void RefreshInspectorDisplay()
    {
        int prevHighlightIndex = HighlightedItem != null ? Results.IndexOf(HighlightedItem) : -1;

        _inspectorParamMap.Clear();

        var sourceParams = _inspectorParams;

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.ToLowerInvariant();
            sourceParams = sourceParams.Where(p =>
                p.Name.ToLowerInvariant().Contains(query)
                || p.DisplayValue.ToLowerInvariant().Contains(query)
                || (p.CategoryName != null && p.CategoryName.ToLowerInvariant().Contains(query))
            ).ToList();
        }

        var items = new List<BrowserItem>();
        long nextId = -3000;

        foreach (var param in sourceParams)
        {
            var itemId = nextId--;
            string displayName;
            string displayCategory;

            if (param.IsDivider)
            {
                displayName = param.Name;
                displayCategory = "";
            }
            else if (param.IsCategoryRow)
            {
                displayName = param.Name;
                displayCategory = param.DisplayValue;
            }
            else
            {
                displayName = param.Name;
                displayCategory = param.DisplayValue;
            }

            var item = new BrowserItem
            {
                Name = displayName,
                Category = displayCategory,
                ElementId = itemId,
                Kind = BrowserItemKind.Shebang, // hides star button
                GroupKey = param.IsCategoryRow
                    ? "CATEGORIES"
                    : param.IsDivider
                        ? "" // dividers don't group
                        : param.GroupName.ToUpperInvariant()
            };

            items.Add(item);
            _inspectorParamMap[itemId] = param;
        }

        Results.ReplaceAll(items);

        // Status text
        if (InspectorIsMixedCategory)
            StatusText = $"{InspectorBreadcrumb}";
        else
            StatusText = $"{InspectorBreadcrumb} \u00b7 {sourceParams.Count(p => !p.IsDivider)} parameters";

        // Restore highlight
        if (prevHighlightIndex >= 0 && prevHighlightIndex < Results.Count)
            HighlightedItem = Results[prevHighlightIndex];
        else if (Results.Count > 0)
            HighlightedItem = Results[0];
        else
            HighlightedItem = null;
    }

    /// <summary>
    /// Gets the InspectorParameterItem for the currently highlighted BrowserItem.
    /// </summary>
    public InspectorParameterItem? GetInspectorParam(BrowserItem? item)
    {
        if (item == null) return null;
        _inspectorParamMap.TryGetValue(item.ElementId, out var param);
        return param;
    }

    // ── Parameter Filter Mode (!pf) ────────────────────────────────

    private ParamFilterField _paramFilterFocus = ParamFilterField.Scope;
    public ParamFilterField ParamFilterFocus
    {
        get => _paramFilterFocus;
        set
        {
            _paramFilterFocus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsParamScopeFocused));
            OnPropertyChanged(nameof(IsParamParameterFocused));
            OnPropertyChanged(nameof(IsParamRuleFocused));
            OnPropertyChanged(nameof(IsParamValueFocused));
            OnPropertyChanged(nameof(PlaceholderText));
        }
    }

    public bool IsParamScopeFocused => _paramFilterFocus == ParamFilterField.Scope;
    public bool IsParamParameterFocused => _paramFilterFocus == ParamFilterField.Parameter;
    public bool IsParamRuleFocused => _paramFilterFocus == ParamFilterField.Rule;
    public bool IsParamValueFocused => _paramFilterFocus == ParamFilterField.Value;

    private ParamFilterScope _paramFilterScopeValue = ParamFilterScope.EntireProject;
    public ParamFilterScope ParamFilterScopeValue
    {
        get => _paramFilterScopeValue;
        set { _paramFilterScopeValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(ParamFilterScopeLabel)); }
    }

    private string? _paramFilterParameterName;
    public string? ParamFilterParameterName
    {
        get => _paramFilterParameterName;
        set { _paramFilterParameterName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ParamFilterParameterLabel)); }
    }

    private ParamFilterRule? _paramFilterRuleValue;
    public ParamFilterRule? ParamFilterRuleValue
    {
        get => _paramFilterRuleValue;
        set { _paramFilterRuleValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(ParamFilterRuleLabel)); OnPropertyChanged(nameof(ParamFilterValueLabel)); }
    }

    private string _paramFilterValueText = string.Empty;
    public string ParamFilterValueText
    {
        get => _paramFilterValueText;
        set { _paramFilterValueText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ParamFilterValueLabel)); }
    }

    public string ParamFilterScopeLabel => _paramFilterScopeValue.Label();
    public string ParamFilterParameterLabel =>
        string.IsNullOrEmpty(_paramFilterParameterName) ? "(choose…)" : _paramFilterParameterName!;
    public string ParamFilterRuleLabel =>
        _paramFilterRuleValue.HasValue ? _paramFilterRuleValue.Value.Label() : "(choose…)";

    /// <summary>
    /// Label shown for the Value field. Greyed out when the chosen rule doesn't take a value.
    /// </summary>
    public string ParamFilterValueLabel
    {
        get
        {
            if (_paramFilterRuleValue.HasValue && !_paramFilterRuleValue.Value.TakesValue())
                return "(not needed)";
            return string.IsNullOrEmpty(_paramFilterValueText) ? "(type or pick…)" : _paramFilterValueText;
        }
    }

    public string ParamFilterFooterHint => IsInParameterFilterMode
        ? "Enter: pick · Tab/Shift+Tab: next/previous field · Ctrl+Enter: run · Esc: close quip"
        : string.Empty;

    /// <summary>
    /// True when all four fields are valid and the filter can be executed.
    /// </summary>
    public bool ParamFilterCanExecute =>
        !string.IsNullOrEmpty(_paramFilterParameterName)
        && _paramFilterRuleValue.HasValue
        && (!_paramFilterRuleValue.Value.TakesValue() || !string.IsNullOrEmpty(_paramFilterValueText));

    private List<string> _paramFilterAllParams = new();
    private List<string> _paramFilterAllValues = new();

    /// <summary>Fired after the user commits a parameter name; code-behind reloads
    /// the Value field's suggestion list for the new parameter.</summary>
    public event Action<string>? ParamFilterParamSelected;

    /// <summary>Fired when Enter is pressed on the final field with all values valid.
    /// Code-behind runs the query and updates the Revit selection.</summary>
    public event Action? ParamFilterExecuteRequested;

    /// <summary>
    /// Enters Parameter Filter mode with the supplied parameter-name index.
    /// </summary>
    public void EnterParameterFilterMode(List<string> parameterNames)
    {
        ClearSurfaceState();
        _paramFilterAllParams = parameterNames;
        _paramFilterScopeValue = ParamFilterScope.EntireProject;

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(ParamFilterScopeLabel));
        OnPropertyChanged(nameof(ParamFilterParameterLabel));
        OnPropertyChanged(nameof(ParamFilterRuleLabel));
        OnPropertyChanged(nameof(ParamFilterValueLabel));
        OnPropertyChanged(nameof(ParamFilterFocus));
        OnPropertyChanged(nameof(IsParamScopeFocused));
        OnPropertyChanged(nameof(IsParamParameterFocused));
        OnPropertyChanged(nameof(IsParamRuleFocused));
        OnPropertyChanged(nameof(IsParamValueFocused));

        Surface = PaneSurface.ParamFilter;
        RefreshParameterFilterDisplay();
    }

    /// <summary>
    /// Replaces the Value field's suggestion list (called after a parameter is picked).
    /// </summary>
    public void SetParamFilterValueSuggestions(List<string> values)
    {
        _paramFilterAllValues = values;
        if (IsInParameterFilterMode && _paramFilterFocus == ParamFilterField.Value)
            RefreshParameterFilterDisplay();
    }

    /// <summary>
    /// Tab / Shift+Tab. Skips the Value field when the rule doesn't take a value.
    /// </summary>
    public void ParamFilterMoveFocus(int direction)
    {
        var order = new List<ParamFilterField> { ParamFilterField.Scope, ParamFilterField.Parameter, ParamFilterField.Rule };
        if (!_paramFilterRuleValue.HasValue || _paramFilterRuleValue.Value.TakesValue())
            order.Add(ParamFilterField.Value);

        int idx = order.IndexOf(_paramFilterFocus);
        if (idx < 0) idx = 0;
        int next = idx + direction;
        if (next < 0) next = order.Count - 1;
        if (next >= order.Count) next = 0;

        ParamFilterFocus = order[next];
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        RefreshParameterFilterDisplay();
    }

    /// <summary>
    /// Commits the dropdown's currently-highlighted row to the focused field
    /// and advances. Returns true if a commit happened.
    /// </summary>
    public bool ParamFilterCommitHighlight()
    {
        if (HighlightedItem == null) return false;
        var label = HighlightedItem.Name;
        return CommitToFocusedField(label);
    }

    /// <summary>
    /// Commits the search-box text to the focused field (used for free-text Value).
    /// </summary>
    public bool ParamFilterCommitTyped()
    {
        var typed = _searchText.Trim();
        if (string.IsNullOrEmpty(typed)) return false;
        return CommitToFocusedField(typed);
    }

    private bool CommitToFocusedField(string label)
    {
        switch (_paramFilterFocus)
        {
            case ParamFilterField.Scope:
                var scope = ParamFilterScopeExt.FromLabel(label);
                if (!scope.HasValue) return false;
                ParamFilterScopeValue = scope.Value;
                AdvanceFocusAfterCommit();
                return true;

            case ParamFilterField.Parameter:
                ParamFilterParameterName = label;
                ParamFilterParamSelected?.Invoke(label);
                AdvanceFocusAfterCommit();
                return true;

            case ParamFilterField.Rule:
                var rule = ParamFilterRuleExt.FromLabel(label);
                if (!rule.HasValue) return false;
                ParamFilterRuleValue = rule.Value;
                AdvanceFocusAfterCommit();
                return true;

            case ParamFilterField.Value:
                ParamFilterValueText = label;
                // No further field; the caller (code-behind) will request execute.
                return true;
        }
        return false;
    }

    private void AdvanceFocusAfterCommit()
    {
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        ParamFilterMoveFocus(1);
    }

    /// <summary>
    /// Builds the dropdown items for the currently focused field, filtered by SearchText.
    /// </summary>
    private void RefreshParameterFilterDisplay()
    {
        IEnumerable<string> source = _paramFilterFocus switch
        {
            ParamFilterField.Scope => new[] { "Entire Project", "Active View" },
            ParamFilterField.Parameter => _paramFilterAllParams,
            ParamFilterField.Rule => Enum.GetValues<ParamFilterRule>().Select(r => r.Label()),
            ParamFilterField.Value => _paramFilterAllValues,
            _ => Array.Empty<string>()
        };

        long nextId = -5000;
        var items = source.Select(label => new BrowserItem
        {
            Name = label,
            Category = string.Empty,
            ElementId = nextId--,
            Kind = BrowserItemKind.Shebang, // hides star and routes through generic styling
            GroupKey = string.Empty
        }).ToList();

        if (!string.IsNullOrWhiteSpace(_searchText))
            items = FuzzyMatcher.Match(items, _searchText.TrimStart());

        Results.ReplaceAll(items);
        HighlightedItem = Results.Count > 0 ? Results[0] : null;

        StatusText = _paramFilterFocus switch
        {
            ParamFilterField.Scope => "Choose scope for the filter",
            ParamFilterField.Parameter => $"{_paramFilterAllParams.Count} parameters in scope",
            ParamFilterField.Rule => "Choose how the value should match",
            ParamFilterField.Value => _paramFilterAllValues.Count > 0
                ? $"{_paramFilterAllValues.Count} known values. Type your own or pick one"
                : "Type a value",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Called by the view to push the latest search-box text into the Value field
    /// as the user types (so ParamFilterCanExecute / labels stay current).
    /// </summary>
    public void ParamFilterSyncValueFromSearch()
    {
        if (_paramFilterFocus != ParamFilterField.Value) return;
        ParamFilterValueText = _searchText.Trim();
    }

    /// <summary>
    /// Requests execution of the filter. Code-behind subscribes via ParamFilterExecuteRequested.
    /// </summary>
    public void RequestParamFilterExecute() => ParamFilterExecuteRequested?.Invoke();

    // ── Family input parsing ────────────────────────────────────────

    /// <summary>
    /// Parses the Family Mode input, extracting optional -c category filter
    /// and the remaining fuzzy search query.
    /// </summary>
    private (string query, string? categoryFilter) ParseFamilyInput()
    {
        if (_cachedFamilyInput.HasValue) return _cachedFamilyInput.Value;

        var raw = _searchText.TrimStart();
        string? categoryFilter = null;

        // Look for -c flag followed by a filter word
        int flagIndex = raw.IndexOf("-c ", StringComparison.OrdinalIgnoreCase);
        if (flagIndex >= 0)
        {
            var afterFlag = raw.Substring(flagIndex + 3).TrimStart();
            int spaceIndex = afterFlag.IndexOf(' ');
            if (spaceIndex < 0)
            {
                categoryFilter = afterFlag;
                raw = raw.Substring(0, flagIndex).TrimEnd();
            }
            else
            {
                categoryFilter = afterFlag.Substring(0, spaceIndex);
                raw = (raw.Substring(0, flagIndex) + afterFlag.Substring(spaceIndex)).Trim();
            }
        }

        _cachedFamilyInput = (raw, categoryFilter);
        return _cachedFamilyInput.Value;
    }

    private string EffectiveSearchText => IsFamilyMode
        ? ParseFamilyInput().query
        : _searchText;

    public BulkObservableCollection<BrowserItem> Results { get; } = new();
    public BulkObservableCollection<BrowserItem> Favorites { get; } = new();
    public BulkObservableCollection<BrowserItem> TopRankedItems { get; } = new();

    public event Action<BrowserItem>? ItemSelectionRequested;
    public event Action? FavoritesChanged;
    public event Action? FocusSearchRequested;

    /// <summary>
    /// Restores a previously saved mode and search text on pane open.
    /// </summary>
    public void RestoreState(ActiveMode mode, string searchText)
    {
        if (mode != ActiveMode.Browser)
            ActiveMode = mode;
        else
            ActiveMode = ActiveMode.Browser;

        _searchText = searchText;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        HighlightedItem = null;
        _debounceTimer.Stop();
        RefreshResults();
        FocusSearchRequested?.Invoke();
    }

    /// <summary>
    /// Activates a default mode on pane open (for "Clean slate" with a non-Browser default).
    /// </summary>
    public void ActivateDefaultMode(ActiveMode mode)
    {
        // Always reset to the requested mode (fixes clean-slate not resetting)
        ActivateMode(mode);
        FocusSearchRequested?.Invoke();
    }

    /// <summary>
    /// Builds a unified list of favorites + results for keyboard navigation.
    ///
    /// Built fresh on every keypress on purpose. It used to be cached, and any
    /// screen that rebuilt its rows without clearing the cache left Up/Down
    /// walking rows that were no longer on screen. The list is at most a few
    /// hundred items, so there is nothing worth caching.
    /// </summary>
    private List<BrowserItem> GetUnifiedList()
    {
        var list = new List<BrowserItem>(Favorites.Count + Results.Count);
        // Only include favorites in Browser mode (they're hidden in other modes)
        if (ShowFavorites)
        {
            foreach (var fav in Favorites) list.Add(fav);
        }
        foreach (var res in Results)
        {
            if (ShowFavorites && res.IsFavorite) continue;
            list.Add(res);
        }
        return list;
    }

    public void MoveHighlight(int direction)
    {
        var unified = GetUnifiedList();
        if (unified.Count == 0) return;

        // No cursor yet, or the cursor is on a row that is no longer listed:
        // start from the top (Down) or the bottom (Up).
        int index = HighlightedItem == null ? -1 : unified.IndexOf(HighlightedItem);
        if (index < 0)
        {
            HighlightedItem = direction > 0 ? unified[0] : unified[^1];
            return;
        }

        int newIndex = Math.Clamp(index + direction, 0, unified.Count - 1);
        HighlightedItem = unified[newIndex];
    }

    public void LoadCommands(List<BrowserItem> commands)
    {
        _commandItems = commands;
    }

    public void LoadShebangs(List<BrowserItem> shebangs)
    {
        _shebangItems = shebangs;
    }

    public void LoadItems(List<BrowserItem> items, HashSet<long> favoriteIds)
    {
        _allItems = items;
        _favoriteIds = favoriteIds;

        foreach (var item in _allItems)
            item.IsFavorite = _favoriteIds.Contains(item.ElementId);

        // Populate available categories for Place mode empty state
        AvailableCategories.Clear();
        var cats = _allItems
            .Where(i => i.Kind == BrowserItemKind.FamilyType)
            .Select(i => i.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .Take(12);
        foreach (var cat in cats)
            AvailableCategories.Add(cat);

        StatusText = $"{_allItems.Count} items loaded.";
        RefreshFavorites();
        RefreshResults();
    }

    public void ToggleFavorite(BrowserItem item)
    {
        item.IsFavorite = !item.IsFavorite;

        if (item.IsFavorite)
            _favoriteIds.Add(item.ElementId);
        else
            _favoriteIds.Remove(item.ElementId);

        RefreshFavorites();
        FavoritesChanged?.Invoke();
    }

    public HashSet<long> GetFavoriteIds() => _favoriteIds;

    private const int MaxResults = 200;

    /// <summary>
    /// Assigns GroupKey to each item based on its Kind.
    /// </summary>
    private static string GetGroupKey(BrowserItem item)
    {
        return item.Kind switch
        {
            BrowserItemKind.View => "VIEWS",
            BrowserItemKind.Sheet => "SHEETS",
            BrowserItemKind.Schedule => "SCHEDULES",
            BrowserItemKind.Family => "FAMILIES",
            BrowserItemKind.FamilyType => item.Category.ToUpperInvariant(),
            BrowserItemKind.Group => "GROUPS",
            BrowserItemKind.RevitLink => "LINKS",
            BrowserItemKind.Assembly => "ASSEMBLIES",
            BrowserItemKind.Command => item.RibbonTab?.ToUpperInvariant() ?? "OTHER",
            BrowserItemKind.Shebang => "QUIPS",
            _ => "OTHER"
        };
    }

    /// <summary>
    /// Group sort order for the unified Browser surface — project items first,
    /// then quips, then Revit commands, then other.
    /// </summary>
    private static int GetBrowserGroupOrder(BrowserItem item)
    {
        // Special synthetic groups
        if (item.GroupKey == "RECENT") return 0;
        if (item.GroupKey == "FAVORITES") return 1;

        return item.Kind switch
        {
            BrowserItemKind.View => 2,
            BrowserItemKind.Sheet => 3,
            BrowserItemKind.Schedule => 4,
            BrowserItemKind.Family or BrowserItemKind.FamilyType => 5,
            BrowserItemKind.Group => 6,
            BrowserItemKind.RevitLink => 7,
            BrowserItemKind.Assembly => 8,
            BrowserItemKind.Shebang => 9,
            BrowserItemKind.Command => 10,
            _ => 11
        };
    }

    public void RefreshResults()
    {
        // In report mode, route to the in-pane report surface
        if (IsInReportMode)
        {
            RefreshReportDisplay();
            return;
        }

        // In parameter filter mode, route to the form-driven dropdown
        if (IsInParameterFilterMode)
        {
            ParamFilterSyncValueFromSearch();
            RefreshParameterFilterDisplay();
            return;
        }

        // In inspector mode, route to inspector display
        if (IsInInspectorMode)
        {
            RefreshInspectorDisplay();
            return;
        }

        if (IsShebangMode)
        {
            var query = _searchText.TrimStart();
            var filtered = FuzzyMatcher.Match(_shebangItems, query);

            foreach (var item in filtered)
                item.GroupKey = "QUIPS";

            Results.ReplaceAll(filtered);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"{_shebangItems.Count} quips"
                : $"{filtered.Count} of {_shebangItems.Count} quips";
            HighlightedItem = Results.Count > 0 ? Results[0] : null;
        }
        else if (IsCommandMode)
        {
            var query = _searchText.TrimStart();
            var filtered = FuzzyMatcher.Match(_commandItems, query);
            int totalMatched = filtered.Count;

            // Group by ribbon tab — "Revit Command" always first
            var grouped = filtered
                .Take(MaxResults)
                .OrderBy(i => string.Equals(i.RibbonTab, "Revit Command", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(i => i.RibbonTab ?? "Other")
                .ToList();

            foreach (var item in grouped)
                item.GroupKey = item.RibbonTab?.ToUpperInvariant() ?? "OTHER";

            Results.ReplaceAll(grouped);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"{_commandItems.Count} commands"
                : $"{Math.Min(totalMatched, MaxResults)} of {_commandItems.Count} commands";
            HighlightedItem = Results.Count > 0 ? Results[0] : null;
        }
        else if (IsFamilyMode)
        {
            RefreshFamilyResults();
        }
        else
        {
            // Unified Browser surface
            if (FamilyNavigationStage == FamilyNavigationStage.TypeLevel && SelectedFamilyForDrilldown != null)
            {
                RefreshTypeLevel();
            }
            else if (string.IsNullOrWhiteSpace(_searchText))
            {
                // Empty search: show top-ranked items by frecency
                RefreshTopRanked();
                Results.ReplaceAll(TopRankedItems.ToList());

                StatusText = TopRankedItems.Count > 0
                    ? $"{_allItems.Count} items · {TopRankedItems.Count} top"
                    : $"{_allItems.Count} items loaded.";
                HighlightedItem = null;
            }
            else
            {
                RefreshUnifiedSearch();
            }
        }

        OnPropertyChanged(nameof(ShowFavorites));
        OnPropertyChanged(nameof(IsEmptyState));

        if (!IsEmptyState)
            ExitChipNavigation();
    }

    private void RefreshFamilyResults()
    {
        // Stage 2 (types of one family) is the same list in > mode and in the
        // unified search, so both use the one method.
        if (FamilyNavigationStage == FamilyNavigationStage.TypeLevel && SelectedFamilyForDrilldown != null)
        {
            RefreshTypeLevel();
            return;
        }

        var (query, categoryFilter) = ParseFamilyInput();

        var source = _allItems.Where(i => i.Kind == BrowserItemKind.FamilyType);

        if (FilterPlacedTypes)
            source = source.Where(i => i.IsPlacedInProject);

        if (!string.IsNullOrEmpty(categoryFilter))
            source = source.Where(i =>
                i.Category.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase));

        var sourceList = source.ToList();

        // Stage 1: show families (grouped by category)
        var filtered = FuzzyMatcher.Match(sourceList, query);
        int totalMatched = filtered.Count;

        // Group by family name, create summary rows
        var resultList = new List<BrowserItem>();
        var familyGroups = filtered
            .GroupBy(i => new { i.FamilyName, i.Category })
            .OrderBy(g => g.Key.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.FamilyName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults);

        foreach (var group in familyGroups)
        {
            var first = group.First();
            resultList.Add(new BrowserItem
            {
                Name = group.Key.FamilyName ?? first.Name,
                Category = group.Key.Category ?? "Family",
                ElementId = first.ElementId,
                Kind = BrowserItemKind.FamilyType,
                FamilyName = group.Key.FamilyName,
                TypeCount = group.Count(),
                GroupKey = (group.Key.Category ?? "FAMILY").ToUpperInvariant()
            });
        }

        Results.ReplaceAll(resultList);

        var statusParts = new List<string> { "Place Mode" };
        if (!string.IsNullOrEmpty(categoryFilter))
            statusParts.Add($"-c {categoryFilter}");
        string prefix = string.Join(" ", statusParts);

        StatusText = string.IsNullOrWhiteSpace(query)
            ? $"{prefix} \u00b7 {Results.Count} families"
            : $"{prefix} \u00b7 {Results.Count} families ({totalMatched} {(totalMatched == 1 ? "type" : "types")})";
        HighlightedItem = Results.Count > 0 ? Results[0] : null;
    }

    /// <summary>
    /// Unified search across project items, families (collapsed by FamilyName), Revit
    /// commands, and shebangs. Family types are grouped into rollup rows that drill in
    /// on Enter, matching the > prefix mode's Stage 1 UX.
    /// </summary>
    private void RefreshUnifiedSearch()
    {
        var query = _searchText.TrimStart();

        // Split _allItems so FamilyTypes can be rolled up after matching. The bare
        // BrowserItemKind.Family node duplicates what each rollup row already
        // represents (same name, same drill behavior) — exclude it here so a search
        // like "panel" doesn't surface both "Panel" and "Panel (N types)".
        var familyTypes = new List<BrowserItem>();
        var otherProjectItems = new List<BrowserItem>();
        foreach (var item in _allItems)
        {
            if (item.Kind == BrowserItemKind.FamilyType)
            {
                if (!FilterPlacedTypes || item.IsPlacedInProject)
                    familyTypes.Add(item);
            }
            else if (item.Kind == BrowserItemKind.Family)
            {
                continue;
            }
            else
            {
                otherProjectItems.Add(item);
            }
        }

        var scoredOther = FuzzyMatcher.MatchScored(otherProjectItems, query);
        var scoredFamilyTypes = FuzzyMatcher.MatchScored(familyTypes, query);
        var scoredCommands = FuzzyMatcher.MatchScored(_commandItems, query);
        var scoredShebangs = FuzzyMatcher.MatchScored(_shebangItems, query);

        int totalMatched = scoredOther.Count + scoredFamilyTypes.Count
            + scoredCommands.Count + scoredShebangs.Count;

        // Collapse family-type matches into one rollup row per family. The rollup
        // carries the *best* score across the family's types so a single strong
        // type match floats the whole family up.
        var familyRollups = scoredFamilyTypes
            .GroupBy(s => new { s.Item.FamilyName, s.Item.Category })
            .Select(g =>
            {
                var first = g.First().Item;
                int bestScore = g.Max(s => s.Score);
                var rollup = new BrowserItem
                {
                    Name = g.Key.FamilyName ?? first.Name,
                    Category = g.Key.Category ?? "Family",
                    ElementId = first.ElementId,
                    Kind = BrowserItemKind.FamilyType,
                    FamilyName = g.Key.FamilyName,
                    TypeCount = g.Count()
                };
                return (Item: rollup, Score: bestScore);
            })
            .ToList();

        var combined = new List<(BrowserItem Item, int Score)>(
            scoredOther.Count + familyRollups.Count + scoredCommands.Count + scoredShebangs.Count);
        combined.AddRange(scoredOther);
        combined.AddRange(familyRollups);
        combined.AddRange(scoredCommands);
        combined.AddRange(scoredShebangs);

        foreach (var entry in combined)
            entry.Item.GroupKey = GetGroupKey(entry.Item);

        // Rank groups by their best-scoring member so a strong match in any Kind
        // lifts its whole group above weakly-matched groups. Kind precedence
        // (GetBrowserGroupOrder) is kept only as a tiebreaker.
        var groupBestScore = combined
            .GroupBy(e => e.Item.GroupKey)
            .ToDictionary(g => g.Key, g => g.Max(e => e.Score));

        var grouped = combined
            .OrderByDescending(e => groupBestScore[e.Item.GroupKey])
            .ThenBy(e => GetBrowserGroupOrder(e.Item))
            .ThenBy(e => e.Item.GroupKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => e.Score)
            .Take(MaxResults)
            .Select(e => e.Item)
            .ToList();

        Results.ReplaceAll(grouped);
        StatusText = BuildBrowserStatusText(grouped, totalMatched);
        HighlightedItem = Results.Count > 0 ? Results[0] : null;
    }

    /// <summary>
    /// Type-level drilldown inside the unified surface. Shows all types of the
    /// selected family with a back row, mirroring Place mode Stage 2.
    /// </summary>
    private void RefreshTypeLevel()
    {
        if (SelectedFamilyForDrilldown == null) return;

        var types = _allItems
            .Where(i => i.Kind == BrowserItemKind.FamilyType
                     && i.FamilyName == SelectedFamilyForDrilldown)
            .ToList();

        if (FilterPlacedTypes)
            types = types.Where(i => i.IsPlacedInProject).ToList();

        int allTypes = types.Count;

        // Typing narrows the types of this family (plain "contains" match on
        // the type name, like the filter box inside a quip).
        var typed = _searchText.Trim();
        if (typed.Length > 0)
        {
            types = types
                .Where(i => (i.TypeName ?? i.Name).Contains(typed, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var resultList = new List<BrowserItem>
        {
            new BrowserItem
            {
                Name = $"← {SelectedFamilyForDrilldown}",
                Category = "",
                ElementId = -1,
                Kind = BrowserItemKind.FamilyType,
                IsBackRow = true,
                GroupKey = SelectedFamilyForDrilldown.ToUpperInvariant()
            }
        };

        foreach (var item in types)
        {
            item.GroupKey = SelectedFamilyForDrilldown.ToUpperInvariant();
            resultList.Add(item);
        }

        Results.ReplaceAll(resultList);
        StatusText = types.Count == allTypes
            ? $"{allTypes} {(allTypes == 1 ? "type" : "types")} in {SelectedFamilyForDrilldown}"
            : $"{types.Count} of {allTypes} types in {SelectedFamilyForDrilldown}";
        HighlightedItem = Results.Count > 1 ? Results[1] : (Results.Count > 0 ? Results[0] : null);
    }

    /// <summary>
    /// Applies a category filter chip from the Place mode empty state.
    /// </summary>
    public void ApplyCategoryFilter(string category)
    {
        ExitChipNavigation();

        if (ActiveMode != ActiveMode.Place)
            ActivateMode(ActiveMode.Place);

        _searchText = $"-c {category} ";
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        RefreshResults();
    }

    private void RefreshFavorites()
    {
        Favorites.ReplaceAll(_allItems.Where(i => i.IsFavorite).ToList());
        HasFavorites = Favorites.Count > 0;
    }

    /// <summary>
    /// Populates TopRankedItems with the highest-frecency entries across all kinds,
    /// resolved back to live BrowserItem objects. Used as the unified empty-state.
    /// </summary>
    private void RefreshTopRanked()
    {
        TopRankedItems.Clear();

        var entries = UsageStore.GetTopRanked(15);
        if (entries.Count == 0) return;

        var projLookup = new Dictionary<long, BrowserItem>();
        foreach (var i in _allItems) projLookup.TryAdd(i.ElementId, i);

        var cmdLookup = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in _commandItems)
            if (i.CommandName != null) cmdLookup.TryAdd(i.CommandName, i);

        var sbgLookup = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in _shebangItems)
            if (i.CommandName != null) sbgLookup.TryAdd(i.CommandName, i);

        var resolved = new List<BrowserItem>();
        foreach (var entry in entries)
        {
            BrowserItem? item = null;
            if (!string.IsNullOrEmpty(entry.StringId))
            {
                if (!cmdLookup.TryGetValue(entry.StringId, out item))
                    sbgLookup.TryGetValue(entry.StringId, out item);
            }
            else
            {
                projLookup.TryGetValue(entry.ElementId, out item);
            }

            if (item != null) resolved.Add(item);
        }

        foreach (var i in resolved) i.GroupKey = GetGroupKey(i);

        var ordered = resolved
            .OrderBy(GetBrowserGroupOrder)
            .ThenBy(i => i.GroupKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var i in ordered) TopRankedItems.Add(i);
    }

    /// <summary>
    /// Builds a contextual status string like "12 views · 3 sheets matching 'ctrl'"
    /// </summary>
    private string BuildBrowserStatusText(List<BrowserItem> shown, int totalMatched)
    {
        // Count by kind
        var counts = new Dictionary<string, int>();
        foreach (var item in shown)
        {
            var label = item.Kind switch
            {
                BrowserItemKind.View => "views",
                BrowserItemKind.Sheet => "sheets",
                BrowserItemKind.Schedule => "schedules",
                BrowserItemKind.FamilyType => "families",
                BrowserItemKind.Group => "groups",
                BrowserItemKind.RevitLink => "links",
                BrowserItemKind.Assembly => "assemblies",
                _ => null
            };
            if (label != null)
            {
                counts.TryGetValue(label, out int c);
                counts[label] = c + 1;
            }
        }

        if (counts.Count == 0)
            return "No results. Try a different spelling";

        // Build parts like "12 views · 3 sheets"
        var parts = counts.Select(kv => $"{kv.Value} {kv.Key}");
        var summary = string.Join(" · ", parts);

        if (totalMatched > shown.Count)
            summary += $" (showing {shown.Count} of {totalMatched})";

        return summary;
    }

    private void OnItemSelected()
    {
        if (_selectedItem != null)
            ItemSelectionRequested?.Invoke(_selectedItem);
    }

    // ── Quick Actions ──────────────────────────────────────────────

    public ObservableCollection<QuickAction> ActiveActions { get; } = new();

    private QuickAction? _focusedAction;
    public QuickAction? FocusedAction
    {
        get => _focusedAction;
        set { _focusedAction = value; OnPropertyChanged(); }
    }

    private BrowserItem? _actionBarItem;
    public BrowserItem? ActionBarItem
    {
        get => _actionBarItem;
        set { _actionBarItem = value; OnPropertyChanged(); }
    }

    private bool _isActionBarVisible;
    public bool IsActionBarVisible
    {
        get => _isActionBarVisible;
        set { _isActionBarVisible = value; OnPropertyChanged(); }
    }

    public event Action<BrowserItem, QuickAction>? QuickActionRequested;

    public void OpenActionBar(BrowserItem item)
    {
        var actions = QuickActionResolver.Resolve(item);
        if (actions.Count == 0) return;

        ActiveActions.Clear();
        foreach (var a in actions) ActiveActions.Add(a);

        ActionBarItem = item;
        FocusedAction = actions[0];
        IsActionBarVisible = true;
    }

    public void CloseActionBar()
    {
        IsActionBarVisible = false;
        FocusedAction = null;
        ActiveActions.Clear();
        ClearInlineError();
    }

    public void MoveActionFocus(int direction)
    {
        if (ActiveActions.Count == 0 || FocusedAction == null) return;
        int index = ActiveActions.IndexOf(FocusedAction);
        int newIndex = Math.Clamp(index + direction, 0, ActiveActions.Count - 1);
        FocusedAction = ActiveActions[newIndex];
    }

    /// <summary>Tab / Shift+Tab on an open action bar: next pill, wrapping round.</summary>
    public void CycleActionFocus(int direction)
    {
        if (ActiveActions.Count == 0) return;
        int index = FocusedAction == null ? -1 : ActiveActions.IndexOf(FocusedAction);
        int next = (index + direction + ActiveActions.Count) % ActiveActions.Count;
        FocusedAction = ActiveActions[next];
    }

    public bool ExecuteAction()
    {
        if (ActionBarItem == null || FocusedAction == null) return false;
        var item = ActionBarItem;
        var action = FocusedAction;
        IsActionBarVisible = false;
        FocusedAction = null;
        ActiveActions.Clear();
        QuickActionRequested?.Invoke(item, action);
        if (!IsInlineRenameVisible)
            ActionBarItem = null;
        return true;
    }

    // ── Inline Rename ──────────────────────────────────────────────

    private bool _isInlineRenameVisible;
    public bool IsInlineRenameVisible
    {
        get => _isInlineRenameVisible;
        set
        {
            if (_isInlineRenameVisible == value) return;
            _isInlineRenameVisible = value;
            OnPropertyChanged();
        }
    }

    private string _renameText = string.Empty;
    public string RenameText
    {
        get => _renameText;
        set { _renameText = value; OnPropertyChanged(); }
    }

    private string _renameSheetNumber = string.Empty;
    public string RenameSheetNumber
    {
        get => _renameSheetNumber;
        set { _renameSheetNumber = value; OnPropertyChanged(); }
    }

    private bool _isSheetRename;
    public bool IsSheetRename
    {
        get => _isSheetRename;
        set { _isSheetRename = value; OnPropertyChanged(); }
    }

    private BrowserItem? _renameItem;
    public BrowserItem? RenameItem
    {
        get => _renameItem;
        set { _renameItem = value; OnPropertyChanged(); }
    }

    public event Action? FocusRenameRequested;
    public event Action<BrowserItem, string, string?>? InlineRenameConfirmed;

    public void OpenInlineRename(BrowserItem item)
    {
        CloseActionBar();
        ClearInlineError();
        RenameItem = item;

        if (item.Kind == BrowserItemKind.Sheet)
        {
            IsSheetRename = true;
            var dashIndex = item.Name.IndexOf(" - ");
            if (dashIndex >= 0)
            {
                RenameSheetNumber = item.Name.Substring(0, dashIndex);
                RenameText = item.Name.Substring(dashIndex + 3);
            }
            else
            {
                RenameSheetNumber = string.Empty;
                RenameText = item.Name;
            }
        }
        else
        {
            IsSheetRename = false;
            RenameText = item.Name;
        }

        IsInlineRenameVisible = true;
        FocusRenameRequested?.Invoke();
    }

    public void ConfirmInlineRename()
    {
        if (RenameItem == null) return;
        if (IsExcelExportMode)
        {
            ConfirmExcelExport();
            return;
        }
        InlineRenameConfirmed?.Invoke(RenameItem, RenameText, IsSheetRename ? RenameSheetNumber : null);
        CloseInlineRename();
    }

    public void CloseInlineRename()
    {
        IsInlineRenameVisible = false;
        IsExcelExportMode = false;
        RenameItem = null;
        RenameText = string.Empty;
        RenameSheetNumber = string.Empty;
        IsSheetRename = false;
    }

    // ── Excel Export ──────────────────────────────────────────────

    private bool _isExcelExportMode;
    public bool IsExcelExportMode
    {
        get => _isExcelExportMode;
        set { _isExcelExportMode = value; OnPropertyChanged(); }
    }

    public event Action<BrowserItem, string>? ExcelExportConfirmed;

    public void OpenExcelExportInput(BrowserItem item)
    {
        CloseActionBar();
        ClearInlineError();
        RenameItem = item;
        IsSheetRename = false;
        IsExcelExportMode = true;
        RenameText = item.Name;

        IsInlineRenameVisible = true;
        FocusRenameRequested?.Invoke();
    }

    public void ConfirmExcelExport()
    {
        if (RenameItem == null) return;
        ExcelExportConfirmed?.Invoke(RenameItem, RenameText);
        CloseInlineRename();
    }

    // ── Inline Message (Error / Success) ──────────────────────────

    private string? _inlineError;
    public string? InlineError
    {
        get => _inlineError;
        set { _inlineError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInlineError)); }
    }

    public bool HasInlineError => !string.IsNullOrEmpty(_inlineError);

    private bool _isSuccessMessage;
    public bool IsSuccessMessage
    {
        get => _isSuccessMessage;
        set { _isSuccessMessage = value; OnPropertyChanged(); }
    }

    private DispatcherTimer? _inlineMessageTimer;

    public void ShowInlineError(string message)
    {
        IsSuccessMessage = false;
        InlineError = message;
        StartInlineMessageTimer();
    }

    public void ShowInlineSuccess(string message)
    {
        IsSuccessMessage = true;
        InlineError = message;
        StartInlineMessageTimer();
    }

    public void ClearInlineError()
    {
        _inlineMessageTimer?.Stop();
        InlineError = null;
    }

    private void StartInlineMessageTimer()
    {
        _inlineMessageTimer?.Stop();
        _inlineMessageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _inlineMessageTimer.Tick += (_, _) =>
        {
            _inlineMessageTimer.Stop();
            ClearInlineError();
        };
        _inlineMessageTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
