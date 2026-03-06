using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.Views;

public partial class FinderPaneView : UserControl
{
    // Unicode icons: ☀ (sun) for dark mode toggle, ☾ (moon) for light mode toggle
    private const string SunIcon = "\u2600";
    private const string MoonIcon = "\u263E";

    public FinderPaneView()
    {
        InitializeComponent();
        DataContext = App.ViewModel;

        App.ViewModel.ItemSelectionRequested += OnItemSelectionRequested;
        App.ViewModel.ItemOpenRequested += OnItemOpenRequested;
        App.ViewModel.FavoritesChanged += OnFavoritesChanged;

        ApplyTheme(ThemeService.IsDarkMode());
    }

    private void ApplyTheme(bool isDark)
    {
        ThemeService.SetDarkMode(isDark);

        string themeFile = isDark
            ? "Views/DarkTheme.xaml"
            : "Views/LightTheme.xaml";

        var themeUri = new Uri($"pack://application:,,,/w_finder;component/{themeFile}");
        var themeDictionary = new ResourceDictionary { Source = themeUri };

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(themeDictionary);

        // Update the toggle button icon: show sun in dark mode, moon in light mode
        ThemeToggleBtn.Content = isDark ? SunIcon : MoonIcon;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        bool newIsDark = !ThemeService.IsDarkMode();
        ApplyTheme(newIsDark);
    }

    private void OnItemSelectionRequested(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var elementId = new ElementId(item.ElementId);
            uidoc.Selection.SetElementIds(new List<ElementId> { elementId });
        });
    }

    private void OnItemOpenRequested(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var doc = uidoc.Document;
            var elementId = new ElementId(item.ElementId);
            var element = doc.GetElement(elementId);

            if (element is View view)
            {
                uidoc.RequestViewChange(view);
            }
        });
    }

    private void OnFavoritesChanged()
    {
        FavoritesStore.Save(App.ViewModel.GetFavoriteIds());
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BrowserItem item)
        {
            App.ViewModel.ToggleFavorite(item);
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (App.ViewModel.SelectedItem is BrowserItem item)
        {
            App.ViewModel.RequestOpen(item);
        }
    }
}
