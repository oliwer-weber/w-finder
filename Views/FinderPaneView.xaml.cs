using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        App.ViewModel.QuickActionRequested += OnQuickActionRequested;

        App.ViewModel.FocusSearchRequested += OnFocusSearchRequested;
        App.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        App.ViewModel.FocusRenameRequested += OnFocusRenameRequested;
        App.ViewModel.InlineRenameConfirmed += OnInlineRenameConfirmed;
        App.ViewModel.RefreshRequested += OnRefreshRequested;

        ApplyTheme(ThemeService.IsDarkMode());
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinderPaneViewModel.IsActionBarVisible))
        {
            PositionOverlay(ActionBarPopup);
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInlineRenameVisible))
        {
            PositionOverlay(InlineRenamePopup);
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.HasInlineError))
        {
            PositionOverlay(InlineErrorPopup);
            return;
        }

        if (e.PropertyName != nameof(FinderPaneViewModel.IsFamilyMode)
            && e.PropertyName != nameof(FinderPaneViewModel.IsCommandMode)
            && e.PropertyName != nameof(FinderPaneViewModel.IsShebangMode)) return;

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

    /// <summary>
    /// Positions a popup overlay just below the currently highlighted ListBoxItem.
    /// </summary>
    private void PositionOverlay(Border overlay)
    {
        bool shouldShow = overlay == ActionBarPopup ? App.ViewModel.IsActionBarVisible
            : overlay == InlineRenamePopup ? App.ViewModel.IsInlineRenameVisible
            : App.ViewModel.HasInlineError;

        if (!shouldShow)
        {
            overlay.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        var highlighted = App.ViewModel.HighlightedItem ?? App.ViewModel.ActionBarItem ?? App.ViewModel.RenameItem;
        if (highlighted == null)
        {
            overlay.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        // Find the ListBoxItem container for the highlighted item
        var container = ResultsList.ItemContainerGenerator.ContainerFromItem(highlighted) as ListBoxItem;
        if (container == null)
        {
            container = FavoritesList.ItemContainerGenerator.ContainerFromItem(highlighted) as ListBoxItem;
        }

        if (container == null)
        {
            // Fallback: show at top of results area
            overlay.Margin = new Thickness(8, 0, 8, 0);
            overlay.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        // Get the position of the container relative to the ResultsList
        var transform = container.TransformToAncestor(ResultsList);
        var point = transform.Transform(new System.Windows.Point(0, container.ActualHeight));

        overlay.Margin = new Thickness(8, point.Y, 8, 0);
        overlay.Visibility = System.Windows.Visibility.Visible;
    }

    private void OnFocusSearchRequested()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }

    private void OnFocusRenameRequested()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (App.ViewModel.IsSheetRename)
            {
                RenameSheetNumberBox.Focus();
                Keyboard.Focus(RenameSheetNumberBox);
                RenameSheetNumberBox.SelectAll();
            }
            else
            {
                RenameBox.Focus();
                Keyboard.Focus(RenameBox);
                RenameBox.SelectAll();
            }
        });
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            App.ViewModel.ConfirmInlineRename();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            App.ViewModel.CloseInlineRename();
            // Return focus to search box
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }
        else if (e.Key == Key.Tab && App.ViewModel.IsSheetRename)
        {
            // Tab between sheet number and sheet name fields
            e.Handled = true;
            if (sender == RenameSheetNumberBox)
            {
                RenameSheetNameBox.Focus();
                RenameSheetNameBox.SelectAll();
            }
            else
            {
                RenameSheetNumberBox.Focus();
                RenameSheetNumberBox.SelectAll();
            }
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (App.ViewModel.IsInlineRenameVisible)
                {
                    App.ViewModel.CloseInlineRename();
                }
                else if (App.ViewModel.HasInlineError)
                {
                    App.ViewModel.ClearInlineError();
                }
                else if (App.ViewModel.IsActionBarVisible)
                {
                    App.ViewModel.CloseActionBar();
                }
                else
                {
                    RevitBackgroundTask.Raise(uiApp =>
                    {
                        var pane = uiApp.GetDockablePane(App.PaneId);
                        pane.Hide();
                    });
                }
                break;

            case Key.Enter:
                e.Handled = true;
                // Inline error dismisses on Enter
                if (App.ViewModel.HasInlineError)
                {
                    App.ViewModel.ClearInlineError();
                    break;
                }
                // Quick action bar takes priority
                if (App.ViewModel.IsActionBarVisible)
                {
                    App.ViewModel.ExecuteAction();
                    break;
                }
                var highlighted = App.ViewModel.HighlightedItem;
                if (highlighted != null)
                {
                    if (App.ViewModel.IsShebangMode && highlighted.Kind == BrowserItemKind.Shebang)
                    {
                        // Shebang Mode: execute the custom command and close pane
                        var shebangId = highlighted.CommandName;
                        if (shebangId != null)
                        {
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                ShebangService.Execute(shebangId, uiApp);
                                var pane = uiApp.GetDockablePane(App.PaneId);
                                pane.Hide();
                            });
                        }
                    }
                    else if (App.ViewModel.IsCommandMode && highlighted.Kind == BrowserItemKind.Command)
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

            case Key.Tab:
                e.Handled = true;
                if (App.ViewModel.HighlightedItem is BrowserItem actionItem)
                    App.ViewModel.OpenActionBar(actionItem);
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

            case Key.Left when App.ViewModel.IsActionBarVisible:
                e.Handled = true;
                App.ViewModel.MoveActionFocus(-1);
                break;

            case Key.Right when App.ViewModel.IsActionBarVisible:
                e.Handled = true;
                App.ViewModel.MoveActionFocus(+1);
                break;

            case Key.F2 when App.ViewModel.IsActionBarVisible:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.Rename);
                break;

            case Key.Delete when App.ViewModel.IsActionBarVisible:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.Delete);
                break;

            case Key.D when App.ViewModel.IsActionBarVisible && Keyboard.Modifiers == ModifierKeys.Shift:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.DuplicateWithDetailing);
                break;

            case Key.D when App.ViewModel.IsActionBarVisible && Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.DuplicateDependent);
                break;

            case Key.D when App.ViewModel.IsActionBarVisible && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.Duplicate);
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
        if (App.ViewModel.IsShebangMode && item.Kind == BrowserItemKind.Shebang)
        {
            var shebangId = item.CommandName;
            if (shebangId != null)
            {
                RevitBackgroundTask.Raise(uiApp =>
                {
                    ShebangService.Execute(shebangId, uiApp);
                    var pane = uiApp.GetDockablePane(App.PaneId);
                    pane.Hide();
                });
            }
            return;
        }

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

    private void ExecuteActionByKind(Models.QuickActionKind kind)
    {
        var action = App.ViewModel.ActiveActions.FirstOrDefault(a => a.Kind == kind);
        if (action != null)
        {
            App.ViewModel.FocusedAction = action;
            App.ViewModel.ExecuteAction();
        }
    }

    // ── Refresh after mutations ────────────────────────────────────

    private void OnRefreshRequested()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;
            var items = BrowserItemCollector.Collect(doc);
            var favoriteIds = FavoritesStore.Load(doc);
            Dispatcher.Invoke(() =>
            {
                App.ViewModel.LoadItems(items, favoriteIds);
            });
        });
    }

    // ── Quick Action Handlers ──────────────────────────────────────

    private void OnQuickActionRequested(BrowserItem item, Models.QuickAction action)
    {
        switch (action.Kind)
        {
            case Models.QuickActionKind.Rename:
                // Open inline rename instead of a dialog
                App.ViewModel.OpenInlineRename(item);
                break;

            case Models.QuickActionKind.Delete:
                HandleDelete(item);
                break;

            case Models.QuickActionKind.Duplicate:
                DuplicateView(item, ViewDuplicateOption.Duplicate);
                break;

            case Models.QuickActionKind.DuplicateWithDetailing:
                DuplicateView(item, ViewDuplicateOption.WithDetailing);
                break;

            case Models.QuickActionKind.DuplicateDependent:
                DuplicateView(item, ViewDuplicateOption.AsDependent);
                break;
        }
    }

    /// <summary>
    /// Handles inline rename confirmation from the ViewModel.
    /// For sheets: sheetNumber is non-null and contains the sheet number separately.
    /// </summary>
    private void OnInlineRenameConfirmed(BrowserItem item, string newName, string? sheetNumber)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return;
            var element = doc.GetElement(new ElementId(item.ElementId));
            if (element == null) return;

            if (sheetNumber != null && element is ViewSheet sheet)
            {
                var trimmedNumber = sheetNumber.Trim();
                var trimmedName = newName.Trim();

                // Check for duplicate sheet number before committing
                if (!string.IsNullOrEmpty(trimmedNumber) && trimmedNumber != sheet.SheetNumber)
                {
                    var existing = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Any(s => s.Id != sheet.Id && s.SheetNumber == trimmedNumber);

                    if (existing)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            App.ViewModel.ShowInlineError($"Sheet number \"{trimmedNumber}\" already exists");
                        });
                        return;
                    }
                }

                using var tx = new Transaction(doc, "Rauncher: Rename Sheet");
                tx.Start();
                if (!string.IsNullOrEmpty(trimmedNumber) && trimmedNumber != sheet.SheetNumber)
                    sheet.SheetNumber = trimmedNumber;
                if (!string.IsNullOrEmpty(trimmedName) && trimmedName != sheet.Name)
                    sheet.Name = trimmedName;
                tx.Commit();
            }
            else
            {
                var trimmed = newName.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed != element.Name)
                {
                    using var tx = new Transaction(doc, "Rauncher: Rename");
                    tx.Start();
                    element.Name = trimmed;
                    tx.Commit();
                }
            }

            // Force Revit to update the tab title for renamed views
            uidoc?.RefreshActiveView();

            Dispatcher.Invoke(() =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            });
            RefreshAfterMutation(uiApp);
        });
    }

    /// <summary>
    /// Smart delete: if deleting the active view, switch to an adjacent open view first.
    /// If only one view is open, show an inline error instead.
    /// </summary>
    private void HandleDelete(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null) return;

            var elementId = new ElementId(item.ElementId);
            var element = doc.GetElement(elementId);
            if (element == null) return;

            // Check if this is the active view
            if (element is View viewToDelete && uidoc.ActiveView.Id == elementId)
            {
                // Get all open UIViews
                var openViews = uidoc.GetOpenUIViews();
                if (openViews.Count <= 1)
                {
                    // Cannot delete the only open view
                    Dispatcher.Invoke(() =>
                    {
                        App.ViewModel.ShowInlineError("Cannot delete the only open view");
                    });
                    return;
                }

                // Find the UIView for the active view and close it.
                // UIView.Close() is synchronous — Revit will activate the adjacent tab.
                var activeUIView = openViews.FirstOrDefault(v => v.ViewId == elementId);
                activeUIView?.Close();
            }

            using var tx = new Transaction(doc, "Rauncher: Delete");
            tx.Start();
            doc.Delete(elementId);
            tx.Commit();

            RefreshAfterMutation(uiApp);
        });
    }

    private void DuplicateView(BrowserItem item, ViewDuplicateOption option)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;
            var element = doc.GetElement(new ElementId(item.ElementId));
            if (element is View view && view.CanViewBeDuplicated(option))
            {
                using var tx = new Transaction(doc, "Rauncher: Duplicate");
                tx.Start();
                view.Duplicate(option);
                tx.Commit();
            }

            RefreshAfterMutation(uiApp);
        });
    }

    /// <summary>
    /// Re-collects items and refreshes the UI after a Revit mutation.
    /// Must be called from the Revit thread (inside RevitBackgroundTask.Raise).
    /// </summary>
    private void RefreshAfterMutation(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;
        var items = BrowserItemCollector.Collect(doc);
        var favoriteIds = FavoritesStore.Load(doc);
        Dispatcher.Invoke(() =>
        {
            App.ViewModel.LoadItems(items, favoriteIds);
            // Ensure SearchBox has focus so ESC works to close pane
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }
}
