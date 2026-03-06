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
    private HashSet<long> _favoriteIds = new();
    private readonly DispatcherTimer _debounceTimer;

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
            OnPropertyChanged();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private string _statusText = "Open a project and click the w_finder button.";
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

    private bool _hasFavorites;
    public bool HasFavorites
    {
        get => _hasFavorites;
        set { _hasFavorites = value; OnPropertyChanged(); }
    }

    public ObservableCollection<BrowserItem> Results { get; } = new();
    public ObservableCollection<BrowserItem> Favorites { get; } = new();

    public event Action<BrowserItem>? ItemSelectionRequested;
    public event Action<BrowserItem>? ItemOpenRequested;

    /// <summary>
    /// Fired when favorites change, so the view can persist via ExternalEvent.
    /// </summary>
    public event Action? FavoritesChanged;

    public void RequestOpen(BrowserItem item) => ItemOpenRequested?.Invoke(item);

    /// <summary>
    /// Loads items and applies saved favorite state.
    /// </summary>
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

    public void RefreshResults()
    {
        Results.Clear();

        var filtered = FuzzyMatcher.Match(_allItems, SearchText);

        foreach (var item in filtered)
            Results.Add(item);

        if (!string.IsNullOrWhiteSpace(SearchText))
            StatusText = $"{filtered.Count} of {_allItems.Count} items.";
        else
            StatusText = $"{_allItems.Count} items loaded.";
    }

    private void RefreshFavorites()
    {
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
