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
    Edit,
    Command,
    Shebang
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
    private (string query, string? categoryFilter, bool editMode)? _cachedFamilyInput;
    private List<BrowserItem>? _cachedUnifiedList;

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
            OnPropertyChanged(nameof(IsEditFamilyMode));
            OnPropertyChanged(nameof(IsFamilyMode));
            OnPropertyChanged(nameof(IsCommandMode));
            OnPropertyChanged(nameof(IsShebangMode));
            OnPropertyChanged(nameof(ShowFavorites));
            OnPropertyChanged(nameof(IsEmptyState));
            OnPropertyChanged(nameof(ModePillText));
            OnPropertyChanged(nameof(HasModePill));
        }
    }

    /// <summary>
    /// The display text for the mode pill chip inside the search box.
    /// </summary>
    public string ModePillText => ActiveMode switch
    {
        ActiveMode.Place => "PLACE",
        ActiveMode.Edit => "EDIT",
        ActiveMode.Command => "CMD",
        ActiveMode.Shebang => "SHEBANG",
        _ => "BROWSER"
    };

    public bool HasModePill => true;

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

            // Check for -e flag in family mode to toggle Place/Edit
            if (ActiveMode == ActiveMode.Place || ActiveMode == ActiveMode.Edit)
            {
                var parsed = ParseFamilyInput();
                if (parsed.editMode && ActiveMode != ActiveMode.Edit)
                {
                    _activeMode = ActiveMode.Edit;
                    OnPropertyChanged(nameof(ActiveMode));
                    OnPropertyChanged(nameof(IsEditFamilyMode));
                    OnPropertyChanged(nameof(IsPlaceFamilyMode));
                    OnPropertyChanged(nameof(ModePillText));
                }
                else if (!parsed.editMode && ActiveMode == ActiveMode.Edit)
                {
                    _activeMode = ActiveMode.Place;
                    OnPropertyChanged(nameof(ActiveMode));
                    OnPropertyChanged(nameof(IsEditFamilyMode));
                    OnPropertyChanged(nameof(IsPlaceFamilyMode));
                    OnPropertyChanged(nameof(ModePillText));
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmptyState));
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private string _statusText = "Open a project and click the Rauncher button.";
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

    public bool IsFamilyMode => ActiveMode == ActiveMode.Place || ActiveMode == ActiveMode.Edit;
    public bool IsCommandMode => ActiveMode == ActiveMode.Command;
    public bool IsShebangMode => ActiveMode == ActiveMode.Shebang;
    public bool IsEditFamilyMode => ActiveMode == ActiveMode.Edit;
    public bool IsPlaceFamilyMode => ActiveMode == ActiveMode.Place;
    public bool IsBrowserMode => ActiveMode == ActiveMode.Browser;

    /// <summary>
    /// When true, Family Mode only shows types that have placed instances in the project.
    /// </summary>
    public bool FilterPlacedTypes { get; set; }
    public bool ShowFavorites => HasFavorites && IsBrowserMode;

    // ── Empty state ─────────────────────────────────────────────────

    public bool IsEmptyState => string.IsNullOrWhiteSpace(_searchText) && FamilyNavigationStage == FamilyNavigationStage.FamilyLevel;

    /// <summary>
    /// Available categories for Place mode empty state chips.
    /// </summary>
    public ObservableCollection<string> AvailableCategories { get; } = new();

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
        }
    }

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
        SelectedFamilyForDrilldown = familyItem.FamilyName;
        FamilyNavigationStage = FamilyNavigationStage.TypeLevel;
        // Set search text to family name for context
        _searchText = familyItem.FamilyName;
        OnPropertyChanged(nameof(SearchText));
        RefreshResults();
    }

    /// <summary>
    /// Returns from Stage 2 (type level) back to Stage 1 (family level).
    /// </summary>
    public void NavigateBackToFamilies()
    {
        SelectedFamilyForDrilldown = null;
        FamilyNavigationStage = FamilyNavigationStage.FamilyLevel;
        _searchText = string.Empty;
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        RefreshResults();
    }

    // ── Family input parsing ────────────────────────────────────────

    /// <summary>
    /// Parses the Family Mode input, extracting optional -c category filter,
    /// -e edit flag, and the remaining fuzzy search query.
    /// </summary>
    private (string query, string? categoryFilter, bool editMode) ParseFamilyInput()
    {
        if (_cachedFamilyInput.HasValue) return _cachedFamilyInput.Value;

        var raw = _searchText.TrimStart();
        string? categoryFilter = null;
        bool editMode = false;

        // Check for -e flag (standalone, no argument)
        int eIndex = raw.IndexOf("-e", StringComparison.OrdinalIgnoreCase);
        if (eIndex >= 0)
        {
            int afterE = eIndex + 2;
            if (afterE >= raw.Length || raw[afterE] == ' ')
            {
                editMode = true;
                raw = (raw.Substring(0, eIndex) + (afterE < raw.Length ? raw.Substring(afterE) : "")).Trim();
            }
        }

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

        _cachedFamilyInput = (raw, categoryFilter, editMode);
        return _cachedFamilyInput.Value;
    }

    private string EffectiveSearchText => IsFamilyMode
        ? ParseFamilyInput().query
        : _searchText;

    public BulkObservableCollection<BrowserItem> Results { get; } = new();
    public BulkObservableCollection<BrowserItem> Favorites { get; } = new();
    public BulkObservableCollection<BrowserItem> RecentItems { get; } = new();

    public event Action<BrowserItem>? ItemSelectionRequested;
    public event Action<BrowserItem>? ItemOpenRequested;
    public event Action? FavoritesChanged;
    public event Action? FocusSearchRequested;

    public void RequestFocusSearch()
    {
        SearchText = string.Empty;
        HighlightedItem = null;
        FocusSearchRequested?.Invoke();
    }

    public void RequestOpen(BrowserItem item) => ItemOpenRequested?.Invoke(item);

    /// <summary>
    /// Builds a unified list of favorites + results for keyboard navigation.
    /// </summary>
    private List<BrowserItem> GetUnifiedList()
    {
        if (_cachedUnifiedList != null) return _cachedUnifiedList;

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
        _cachedUnifiedList = list;
        return list;
    }

    public void MoveHighlight(int direction)
    {
        var unified = GetUnifiedList();
        if (unified.Count == 0) return;

        if (HighlightedItem == null)
        {
            HighlightedItem = direction > 0 ? unified[0] : unified[^1];
            return;
        }

        int index = unified.IndexOf(HighlightedItem);
        int newIndex = Math.Clamp(index + direction, 0, unified.Count - 1);
        HighlightedItem = unified[newIndex];
    }

    public bool OpenHighlighted()
    {
        if (HighlightedItem == null) return false;

        // Two-stage family navigation: drill into family on Enter at Stage 1
        if (IsFamilyMode && FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
            && HighlightedItem.TypeCount > 0)
        {
            DrillIntoFamily(HighlightedItem);
            return true;
        }

        // Back row in Stage 2
        if (HighlightedItem.IsBackRow)
        {
            NavigateBackToFamilies();
            return true;
        }

        ItemOpenRequested?.Invoke(HighlightedItem);
        return true;
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
            BrowserItemKind.Shebang => "COMMANDS",
            _ => "OTHER"
        };
    }

    /// <summary>
    /// Group sort order for Browser mode sections.
    /// </summary>
    private static int GetBrowserGroupOrder(string groupKey)
    {
        return groupKey switch
        {
            "RECENT" => 0,
            "FAVORITES" => 1,
            "VIEWS" => 2,
            "SHEETS" => 3,
            "SCHEDULES" => 4,
            "FAMILIES" => 5,
            "GROUPS" => 6,
            "LINKS" => 7,
            "ASSEMBLIES" => 8,
            _ => 9
        };
    }

    public void RefreshResults()
    {
        _cachedUnifiedList = null;

        if (IsShebangMode)
        {
            var query = _searchText.TrimStart();
            var filtered = FuzzyMatcher.Match(_shebangItems, query);

            foreach (var item in filtered)
                item.GroupKey = "COMMANDS";

            Results.ReplaceAll(filtered);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"{_shebangItems.Count} commands"
                : $"{filtered.Count} of {_shebangItems.Count} commands";
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
            // Browser mode
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                // Empty search: show recent items instead of full list
                RefreshRecentItems();
                var recentList = new List<BrowserItem>(RecentItems);
                Results.ReplaceAll(recentList);

                StatusText = RecentItems.Count > 0
                    ? $"{_allItems.Count} items · {RecentItems.Count} recent"
                    : $"{_allItems.Count} items loaded.";
                HighlightedItem = null;
            }
            else
            {
                var filtered = FuzzyMatcher.Match(_allItems, _searchText);
                int totalMatched = filtered.Count;

                // Group by item kind and sort groups
                var grouped = filtered
                    .Take(MaxResults)
                    .Select(i => { i.GroupKey = GetGroupKey(i); return i; })
                    .OrderBy(i => GetBrowserGroupOrder(i.GroupKey))
                    .ThenBy(i => i.GroupKey)
                    .ToList();

                Results.ReplaceAll(grouped);

                StatusText = BuildBrowserStatusText(grouped, totalMatched);
                HighlightedItem = Results.Count > 0 ? Results[0] : null;
            }
        }

        OnPropertyChanged(nameof(ShowFavorites));
        OnPropertyChanged(nameof(IsEmptyState));
    }

    private void RefreshFamilyResults()
    {
        var (query, categoryFilter, editMode) = ParseFamilyInput();

        var source = _allItems.Where(i => i.Kind == BrowserItemKind.FamilyType);

        if (FilterPlacedTypes)
            source = source.Where(i => i.IsPlacedInProject);

        if (!string.IsNullOrEmpty(categoryFilter))
            source = source.Where(i =>
                i.Category.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase));

        var sourceList = source.ToList();

        if (FamilyNavigationStage == FamilyNavigationStage.TypeLevel && SelectedFamilyForDrilldown != null)
        {
            // Stage 2: show types within the selected family
            var types = sourceList
                .Where(i => i.FamilyName == SelectedFamilyForDrilldown)
                .ToList();

            var resultList = new List<BrowserItem>();

            // Add back row
            resultList.Add(new BrowserItem
            {
                Name = $"\u2190 {SelectedFamilyForDrilldown}",
                Category = "",
                ElementId = -1,
                Kind = BrowserItemKind.FamilyType,
                IsBackRow = true,
                GroupKey = SelectedFamilyForDrilldown.ToUpperInvariant()
            });

            foreach (var item in types)
            {
                item.GroupKey = SelectedFamilyForDrilldown.ToUpperInvariant();
                resultList.Add(item);
            }

            Results.ReplaceAll(resultList);

            StatusText = $"{types.Count} types in {SelectedFamilyForDrilldown}";
            HighlightedItem = Results.Count > 1 ? Results[1] : (Results.Count > 0 ? Results[0] : null);
        }
        else
        {
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

            var statusParts = new List<string> { editMode ? "Edit Mode" : "Place Mode" };
            if (!string.IsNullOrEmpty(categoryFilter))
                statusParts.Add($"-c {categoryFilter}");
            string prefix = string.Join(" ", statusParts);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"{prefix} \u2014 {Results.Count} families"
                : $"{prefix} \u2014 {Results.Count} families ({totalMatched} types)";
            HighlightedItem = Results.Count > 0 ? Results[0] : null;
        }
    }

    /// <summary>
    /// Applies a category filter chip from the Place mode empty state.
    /// </summary>
    public void ApplyCategoryFilter(string category)
    {
        if (ActiveMode != ActiveMode.Place && ActiveMode != ActiveMode.Edit)
            ActivateMode(ActiveMode.Place);

        _searchText = $"-c {category} ";
        _cachedFamilyInput = null;
        OnPropertyChanged(nameof(SearchText));
        RefreshResults();
    }

    private void RefreshFavorites()
    {
        _cachedUnifiedList = null;
        Favorites.ReplaceAll(_allItems.Where(i => i.IsFavorite).ToList());
        HasFavorites = Favorites.Count > 0;
    }

    /// <summary>
    /// Populates the RecentItems collection from the RecentItemsStore,
    /// resolving stored element IDs back to full BrowserItem objects.
    /// </summary>
    private void RefreshRecentItems()
    {
        RecentItems.Clear();
        var recents = RecentItemsStore.GetRecents("Browser", 10);
        var itemLookup = new Dictionary<long, BrowserItem>();
        foreach (var i in _allItems)
            itemLookup.TryAdd(i.ElementId, i);

        foreach (var entry in recents)
        {
            if (itemLookup.TryGetValue(entry.ElementId, out var item))
            {
                item.GroupKey = "RECENT";
                RecentItems.Add(item);
            }
        }
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
            return "No results — try a different spelling";

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
    public event Action? RefreshRequested;

    public void RequestRefresh() => RefreshRequested?.Invoke();

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
        set { _isInlineRenameVisible = value; OnPropertyChanged(); }
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
        if (IsShortcutEditMode)
        {
            ConfirmShortcutEdit();
            return;
        }
        InlineRenameConfirmed?.Invoke(RenameItem, RenameText, IsSheetRename ? RenameSheetNumber : null);
        CloseInlineRename();
    }

    public void CloseInlineRename()
    {
        IsInlineRenameVisible = false;
        IsShortcutEditMode = false;
        IsExcelExportMode = false;
        RenameItem = null;
        RenameText = string.Empty;
        RenameSheetNumber = string.Empty;
        IsSheetRename = false;
    }

    // ── Shortcut Editing ──────────────────────────────────────────

    private bool _isShortcutEditMode;
    public bool IsShortcutEditMode
    {
        get => _isShortcutEditMode;
        set { _isShortcutEditMode = value; OnPropertyChanged(); }
    }

    public event Action<BrowserItem, string>? ShortcutEditConfirmed;

    public void OpenShortcutEdit(BrowserItem item)
    {
        CloseActionBar();
        ClearInlineError();
        RenameItem = item;
        IsSheetRename = false;
        IsShortcutEditMode = true;
        RenameText = item.ShortcutKeys ?? string.Empty;

        IsInlineRenameVisible = true;
        FocusRenameRequested?.Invoke();
    }

    public void ConfirmShortcutEdit()
    {
        if (RenameItem == null) return;
        ShortcutEditConfirmed?.Invoke(RenameItem, RenameText);
        CloseInlineRename();
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
