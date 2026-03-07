using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;
using w_finder.ViewModels;

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

        App.ViewModel.FocusSearchRequested += OnFocusSearchRequested;
        App.ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyTheme(ThemeService.IsDarkMode());
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FinderPaneViewModel.IsFamilyMode)
            && e.PropertyName != nameof(FinderPaneViewModel.IsCommandMode)) return;

        var view = CollectionViewSource.GetDefaultView(ResultsList.ItemsSource);
        if (view == null) return;

        if (App.ViewModel.IsFamilyMode)
        {
            if (view.GroupDescriptions.Count == 0)
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.Category)));
        }
        else
        {
            view.GroupDescriptions.Clear();
        }
    }

    private void OnFocusSearchRequested()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                RevitBackgroundTask.Raise(uiApp =>
                {
                    var pane = uiApp.GetDockablePane(App.PaneId);
                    pane.Hide();
                });
                break;

            case Key.Enter:
                e.Handled = true;
                var highlighted = App.ViewModel.HighlightedItem;
                if (highlighted != null)
                {
                    if (App.ViewModel.IsCommandMode && highlighted.Kind == BrowserItemKind.Command)
                    {
                        // Command Mode: execute the Revit command
                        var cmdName = highlighted.CommandName;
                        if (cmdName != null && Enum.TryParse<PostableCommand>(cmdName, out var postableCmd))
                        {
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                var pane = uiApp.GetDockablePane(App.PaneId);
                                pane.Hide();

                                var cmdId = RevitCommandId.LookupPostableCommandId(postableCmd);
                                uiApp.PostCommand(cmdId);
                            });
                        }
                    }
                    else if (App.ViewModel.IsFamilyMode && highlighted.Kind == BrowserItemKind.FamilyType)
                    {
                        if (App.ViewModel.IsEditFamilyMode)
                        {
                            // Edit Family Mode: open the family for editing
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                var uidoc = uiApp.ActiveUIDocument;
                                if (uidoc == null) return;
                                var doc = uidoc.Document;
                                var element = doc.GetElement(new ElementId(highlighted.ElementId));
                                if (element is FamilySymbol symbol && symbol.Family != null && symbol.Family.IsEditable)
                                {
                                    var pane = uiApp.GetDockablePane(App.PaneId);
                                    pane.Hide();
                                    doc.EditFamily(symbol.Family);
                                }
                            });
                        }
                        else
                        {
                            // Family Mode: activate the symbol and start placement
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                var uidoc = uiApp.ActiveUIDocument;
                                if (uidoc == null) return;
                                var doc = uidoc.Document;
                                var element = doc.GetElement(new ElementId(highlighted.ElementId));
                                if (element is FamilySymbol symbol)
                                {
                                    using (var tx = new Transaction(doc, "Activate Family Symbol"))
                                    {
                                        tx.Start();
                                        if (!symbol.IsActive) symbol.Activate();
                                        tx.Commit();
                                    }
                                    var pane = uiApp.GetDockablePane(App.PaneId);
                                    pane.Hide();

                                    try
                                    {
                                        uidoc.PromptForFamilyInstancePlacement(symbol);
                                    }
                                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                                    {
                                        // User pressed Escape to finish placing — normal
                                    }
                                }
                            });
                        }
                    }
                    else
                    {
                        // Normal mode: open the view and hide the pane.
                        RevitBackgroundTask.Raise(uiApp =>
                        {
                            var uidoc = uiApp.ActiveUIDocument;
                            if (uidoc != null)
                            {
                                var doc = uidoc.Document;
                                var element = doc.GetElement(new ElementId(highlighted.ElementId));
                                if (element is View view)
                                    uidoc.RequestViewChange(view);
                            }

                            var pane = uiApp.GetDockablePane(App.PaneId);
                            pane.Hide();
                        });
                    }
                }
                break;

            case Key.Down:
                e.Handled = true;
                App.ViewModel.MoveHighlight(+1);
                ScrollHighlightedIntoView();
                break;

            case Key.Up:
                e.Handled = true;
                App.ViewModel.MoveHighlight(-1);
                ScrollHighlightedIntoView();
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                if (App.ViewModel.HighlightedItem is BrowserItem favItem)
                    App.ViewModel.ToggleFavorite(favItem);
                break;
        }
    }

    private void ScrollHighlightedIntoView()
    {
        var item = App.ViewModel.HighlightedItem;
        if (item == null) return;

        // Check which list contains the highlighted item and scroll it into view
        if (App.ViewModel.Favorites.Contains(item))
            FavoritesList.ScrollIntoView(item);
        else if (App.ViewModel.Results.Contains(item))
            ResultsList.ScrollIntoView(item);
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
        if (App.ViewModel.IsFamilyMode && item.Kind == BrowserItemKind.FamilyType)
        {
            if (App.ViewModel.IsEditFamilyMode)
            {
                // Edit Family Mode: open the family for editing
                RevitBackgroundTask.Raise(uiApp =>
                {
                    var uidoc = uiApp.ActiveUIDocument;
                    if (uidoc == null) return;
                    var doc = uidoc.Document;
                    var element = doc.GetElement(new ElementId(item.ElementId));
                    if (element is FamilySymbol symbol && symbol.Family != null && symbol.Family.IsEditable)
                    {
                        doc.EditFamily(symbol.Family);
                    }
                });
            }
            else
            {
                // Family Mode: activate the symbol and start placement
                RevitBackgroundTask.Raise(uiApp =>
                {
                    var uidoc = uiApp.ActiveUIDocument;
                    if (uidoc == null) return;
                    var doc = uidoc.Document;
                    var element = doc.GetElement(new ElementId(item.ElementId));
                    if (element is FamilySymbol symbol)
                    {
                        using (var tx = new Transaction(doc, "Activate Family Symbol"))
                        {
                            tx.Start();
                            if (!symbol.IsActive) symbol.Activate();
                            tx.Commit();
                        }
                        try
                        {
                            uidoc.PromptForFamilyInstancePlacement(symbol);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // User pressed Escape to finish placing — normal
                        }
                    }
                });
            }
        }
        else
        {
            RevitBackgroundTask.Raise(uiApp =>
            {
                var uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null) return;

                var doc = uidoc.Document;
                var element = doc.GetElement(new ElementId(item.ElementId));

                if (element is View view)
                {
                    uidoc.RequestViewChange(view);
                }
            });
        }
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

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Mouse click on a list item: update highlight and trigger Revit selection
        if (sender is ListBox listBox && listBox.SelectedItem is BrowserItem item)
        {
            App.ViewModel.HighlightedItem = item;
            App.ViewModel.SelectedItem = item;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (App.ViewModel.HighlightedItem is BrowserItem item)
        {
            App.ViewModel.RequestOpen(item);
        }
    }
}
