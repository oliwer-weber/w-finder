using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.ViewModels;

/// <summary>
/// ViewModel for the finder pane. Holds the full item cache, search text, and filtered results.
/// </summary>
public class FinderPaneViewModel : INotifyPropertyChanged
{
    private List<BrowserItem> _allItems = new();
    private List<BrowserItem> _commandItems = new();
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

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            _cachedFamilyInput = null;
            OnPropertyChanged();
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

    public bool IsFamilyMode => _searchText.StartsWith(">");
    public bool IsCommandMode => _searchText.StartsWith(":");
    public bool IsEditFamilyMode => IsFamilyMode && ParseFamilyInput().editMode;
    public bool IsPlaceFamilyMode => IsFamilyMode && !IsEditFamilyMode;
    public bool IsBrowserMode => !IsFamilyMode && !IsCommandMode;
    public bool ShowFavorites => HasFavorites && !IsFamilyMode && !IsCommandMode;

    /// <summary>
    /// Parses the Family Mode input, extracting optional -c category filter,
    /// -e edit flag, and the remaining fuzzy search query.
    /// </summary>
    private (string query, string? categoryFilter, bool editMode) ParseFamilyInput()
    {
        if (_cachedFamilyInput.HasValue) return _cachedFamilyInput.Value;

        var raw = _searchText.Substring(1).TrimStart();
        string? categoryFilter = null;
        bool editMode = false;

        // Check for -e flag (standalone, no argument)
        int eIndex = raw.IndexOf("-e", StringComparison.OrdinalIgnoreCase);
        if (eIndex >= 0)
        {
            // Make sure it's a standalone flag (at end, or followed by space)
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
        : IsCommandMode
            ? _searchText.Substring(1).TrimStart()
            : _searchText;

    public ObservableCollection<BrowserItem> Results { get; } = new();
    public ObservableCollection<BrowserItem> Favorites { get; } = new();

    public event Action<BrowserItem>? ItemSelectionRequested;
    public event Action<BrowserItem>? ItemOpenRequested;

    /// <summary>
    /// Fired when favorites change, so the view can persist via ExternalEvent.
    /// </summary>
    public event Action? FavoritesChanged;

    /// <summary>
    /// Fired after the pane is shown, so the view can focus the search box.
    /// </summary>
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
        foreach (var fav in Favorites) list.Add(fav);
        foreach (var res in Results)
        {
            if (!res.IsFavorite) list.Add(res);
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

    /// <summary>
    /// Opens the currently highlighted item. Returns true if an item was opened.
    /// </summary>
    public bool OpenHighlighted()
    {
        if (HighlightedItem == null) return false;
        ItemOpenRequested?.Invoke(HighlightedItem);
        return true;
    }

    /// <summary>
    /// Loads items and applies saved favorite state.
    /// </summary>
    public void LoadCommands(List<BrowserItem> commands)
    {
        _commandItems = commands;
    }

    public void LoadItems(List<BrowserItem> items, HashSet<long> favoriteIds)
    {
        _allItems = items;
        _favoriteIds = favoriteIds;

        // Mark items that are favorites
        foreach (var item in _allItems)
            item.IsFavorite = _favoriteIds.Contains(item.ElementId);

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

    // Maximum items rendered at once — keeps the list fast regardless of project size.
    private const int MaxResults = 200;

    public void RefreshResults()
    {
        _cachedUnifiedList = null;
        Results.Clear();

        if (IsCommandMode)
        {
            var query = _searchText.Substring(1).TrimStart();
            var filtered = FuzzyMatcher.Match(_commandItems, query);
            int totalMatched = filtered.Count;

            foreach (var item in filtered.Take(MaxResults))
                Results.Add(item);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"Command Mode \u2014 {_commandItems.Count} commands"
                : $"Command Mode \u2014 {Math.Min(totalMatched, MaxResults)} of {_commandItems.Count} commands";
            HighlightedItem = Results.Count > 0 ? Results[0] : null;
        }
        else if (IsFamilyMode)
        {
            var (query, categoryFilter, editMode) = ParseFamilyInput();

            var source = _allItems.Where(i => i.Kind == BrowserItemKind.FamilyType);

            // Apply -c category filter (substring match, case-insensitive)
            if (!string.IsNullOrEmpty(categoryFilter))
                source = source.Where(i =>
                    i.Category.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase));

            var sourceList = source.ToList();
            var filtered = FuzzyMatcher.Match(sourceList, query);
            int totalMatched = filtered.Count;

            // Sort by category for grouped display, then cap
            var sorted = filtered
                .OrderBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults);

            foreach (var item in sorted)
                Results.Add(item);

            var statusParts = new List<string> { editMode ? "Edit Family Mode" : "Family Mode" };
            if (!string.IsNullOrEmpty(categoryFilter))
                statusParts.Add($"-c {categoryFilter}");
            string prefix = string.Join(" ", statusParts);

            StatusText = string.IsNullOrWhiteSpace(query)
                ? $"{prefix} \u2014 {totalMatched} types"
                : $"{prefix} \u2014 {Math.Min(totalMatched, MaxResults)} of {sourceList.Count} types";
            HighlightedItem = Results.Count > 0 ? Results[0] : null;
        }
        else
        {
            var filtered = FuzzyMatcher.Match(_allItems, _searchText);
            int totalMatched = filtered.Count;

            foreach (var item in filtered.Take(MaxResults))
                Results.Add(item);

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                StatusText = $"{Math.Min(totalMatched, MaxResults)} of {_allItems.Count} items.";
                HighlightedItem = Results.Count > 0 ? Results[0] : null;
            }
            else
            {
                StatusText = $"{_allItems.Count} items loaded.";
                HighlightedItem = null;
            }
        }

        OnPropertyChanged(nameof(IsFamilyMode));
        OnPropertyChanged(nameof(IsEditFamilyMode));
        OnPropertyChanged(nameof(IsPlaceFamilyMode));
        OnPropertyChanged(nameof(IsCommandMode));
        OnPropertyChanged(nameof(IsBrowserMode));
        OnPropertyChanged(nameof(ShowFavorites));
    }

    private void RefreshFavorites()
    {
        _cachedUnifiedList = null;
        Favorites.Clear();
        foreach (var item in _allItems.Where(i => i.IsFavorite))
            Favorites.Add(item);
        HasFavorites = Favorites.Count > 0;
    }

    private void OnItemSelected()
    {
        if (_selectedItem != null)
            ItemSelectionRequested?.Invoke(_selectedItem);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
