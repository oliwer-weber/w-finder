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
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfVisibility = System.Windows.Visibility;

namespace w_finder.Views;

public partial class FinderPaneView : UserControl
{
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
        App.ViewModel.ShortcutEditConfirmed += OnShortcutEditConfirmed;
        App.ViewModel.ExcelExportConfirmed += OnExcelExportConfirmed;
        App.ViewModel.RefreshRequested += OnRefreshRequested;

        ApplyTheme(SettingsService.Current.IsDarkMode);

        SettingsService.SettingsChanged += () =>
            Dispatcher.Invoke(() =>
            {
                ApplyTheme(SettingsService.Current.IsDarkMode);
                App.ViewModel.FilterPlacedTypes = SettingsService.Current.FilterPlacedTypesOnly;
                App.ViewModel.RefreshResults();
            });

        // Set up TextInput handler for consumed prefix detection
        SearchBox.PreviewTextInput += SearchBox_PreviewTextInput;
    }

    // ── Consumed Prefix (Mode Pill) ────────────────────────────────

    /// <summary>
    /// Intercepts prefix characters (>, :, !) before they appear in the text box.
    /// Consumes the character and activates the corresponding mode pill.
    /// </summary>
    private void SearchBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (App.ViewModel.ActiveMode != ActiveMode.Browser) return; // Already in a mode

        // Only consume prefix if the search box is empty (or effectively empty)
        if (!string.IsNullOrEmpty(App.ViewModel.SearchText)) return;

        switch (e.Text)
        {
            case ">":
                e.Handled = true;
                App.ViewModel.ActivateMode(ActiveMode.Place);
                UpdateModePillAccent();
                UpdateEmptyState();
                UpdateResultGrouping();
                break;
            case ":":
                e.Handled = true;
                App.ViewModel.ActivateMode(ActiveMode.Command);
                UpdateModePillAccent();
                UpdateEmptyState();
                UpdateResultGrouping();
                break;
            case "!":
                e.Handled = true;
                App.ViewModel.ActivateMode(ActiveMode.Shebang);
                UpdateModePillAccent();
                UpdateEmptyState();
                UpdateResultGrouping();
                break;
        }
    }

    /// <summary>
    /// Updates the mode pill border accent color based on the active mode.
    /// </summary>
    private void UpdateModePillAccent()
    {
        if (App.ViewModel.ActiveMode == ActiveMode.Browser)
        {
            // Neutral pill: oxford-blue border all around, no accent tint
            var pillBorder = (SolidColorBrush)FindResource("PillBorder");
            ModePillBorder.BorderBrush = pillBorder;
            ModePillBorder.BorderThickness = new Thickness(1);
            ModePillBorder.Background = Brushes.Transparent;
            ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("BrowserPillText");
            return;
        }

        WpfColor accentColor = App.ViewModel.ActiveMode switch
        {
            ActiveMode.Place => (WpfColor)FindResource("PlaceAccentColor"),
            ActiveMode.Edit => (WpfColor)FindResource("EditAccentColor"),
            ActiveMode.Command => (WpfColor)FindResource("CommandAccentColor"),
            ActiveMode.Shebang => (WpfColor)FindResource("ShebangAccentColor"),
            _ => WpfColor.FromArgb(0, 0, 0, 0)
        };

        // All borders use accent color — thicker left for identity signal
        var accentBrush = new SolidColorBrush(accentColor);
        accentBrush.Freeze();
        ModePillBorder.BorderBrush = accentBrush;
        ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);

        // Accent background tint at 14% — enough to feel intentional
        var bgColor = WpfColor.FromArgb((byte)(255 * 0.14), accentColor.R, accentColor.G, accentColor.B);
        ModePillBorder.Background = new SolidColorBrush(bgColor);

        // Bright pill text for colored modes
        ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("PillText");
    }

    // ── Empty State Management ─────────────────────────────────────

    private void UpdateEmptyState()
    {
        bool isEmpty = App.ViewModel.IsEmptyState;

        // Browser hints (Row 2) — shown when in browser mode with empty search
        BrowserHints.Visibility = (isEmpty && App.ViewModel.ActiveMode == ActiveMode.Browser)
            ? WpfVisibility.Visible
            : WpfVisibility.Collapsed;

        // Hide all empty state panels first
        PlaceEmptyState.Visibility = WpfVisibility.Collapsed;
        CommandEmptyState.Visibility = WpfVisibility.Collapsed;

        if (!isEmpty)
        {
            EmptyStatePanel.Visibility = WpfVisibility.Collapsed;
            return;
        }

        switch (App.ViewModel.ActiveMode)
        {
            case ActiveMode.Browser:
                // Hints are now in BrowserHints (Row 2), nothing extra here
                break;
            case ActiveMode.Place:
            case ActiveMode.Edit:
                PlaceEmptyState.Visibility = WpfVisibility.Visible;
                PopulateCategoryChips();
                break;
            case ActiveMode.Command:
                CommandEmptyState.Visibility = WpfVisibility.Visible;
                break;
            case ActiveMode.Shebang:
                // Shebang mode shows all commands in results — no empty state needed
                break;
        }

        // Only show EmptyStatePanel if a non-browser empty state is active
        bool hasNonBrowserContent = PlaceEmptyState.Visibility == WpfVisibility.Visible
            || CommandEmptyState.Visibility == WpfVisibility.Visible;

        if (!hasNonBrowserContent)
        {
            EmptyStatePanel.Visibility = WpfVisibility.Collapsed;
            return;
        }

        EmptyStatePanel.Visibility = WpfVisibility.Visible;
    }

    /// <summary>
    /// Populates category filter chips in the Place mode empty state.
    /// </summary>
    private void PopulateCategoryChips()
    {
        CategoryChipsPanel.Children.Clear();
        int maxChips = 6;
        int shown = 0;

        foreach (var cat in App.ViewModel.AvailableCategories)
        {
            if (shown >= maxChips) break;

            var chip = new Border
            {
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Background = (Brush)FindResource("ItemHoverBackground"),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = cat,
                    FontSize = 10,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("PrimaryText")
                }
            };

            var category = cat; // capture for lambda
            chip.MouseLeftButtonDown += (_, _) => App.ViewModel.ApplyCategoryFilter(category);
            CategoryChipsPanel.Children.Add(chip);
            shown++;
        }

        // Show overflow count if needed
        int remaining = App.ViewModel.AvailableCategories.Count - shown;
        if (remaining > 0)
        {
            CategoryChipsPanel.Children.Add(new TextBlock
            {
                Text = $"+{remaining} more",
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 4)
            });
        }
    }


    // ── Result Grouping ────────────────────────────────────────────

    private void UpdateResultGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(ResultsList.ItemsSource);
        if (view == null) return;

        view.GroupDescriptions.Clear();

        // Group by GroupKey for all modes except Shebang
        if (!App.ViewModel.IsShebangMode)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.GroupKey)));
        }
    }

    // ── ViewModel Property Changed Handler ─────────────────────────

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
            InlineErrorPopup.Background = App.ViewModel.IsSuccessMessage
                ? new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(WpfColor.FromRgb(0xE6, 0x86, 0x0A));
            PositionOverlay(InlineErrorPopup);
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.ActiveMode))
        {
            UpdateModePillAccent();
            UpdateEmptyState();
            UpdateResultGrouping();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsEmptyState))
        {
            UpdateEmptyState();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.HighlightedItem))
        {
            SyncListSelections();
            return;
        }
    }

    /// <summary>
    /// Keeps FavoritesList and ResultsList selections mutually exclusive.
    /// When the highlighted item is in one list, the other list's selection is cleared.
    /// </summary>
    private void SyncListSelections()
    {
        _syncingSelections = true;
        try
        {
            var item = App.ViewModel.HighlightedItem;
            if (item == null)
            {
                FavoritesList.SelectedItem = null;
                ResultsList.SelectedItem = null;
                return;
            }

            if (App.ViewModel.Favorites.Contains(item))
            {
                // Item is in favorites — select there, clear results
                FavoritesList.SelectedItem = item;
                ResultsList.SelectedItem = null;
            }
            else
            {
                // Item is in results — select there, clear favorites
                ResultsList.SelectedItem = item;
                FavoritesList.SelectedItem = null;
            }
        }
        finally
        {
            _syncingSelections = false;
        }
    }

    // ── Overlay Positioning ────────────────────────────────────────

    private void PositionOverlay(Border overlay)
    {
        bool shouldShow = overlay == ActionBarPopup ? App.ViewModel.IsActionBarVisible
            : overlay == InlineRenamePopup ? App.ViewModel.IsInlineRenameVisible
            : App.ViewModel.HasInlineError;

        if (!shouldShow)
        {
            overlay.Visibility = WpfVisibility.Collapsed;
            return;
        }

        var highlighted = App.ViewModel.HighlightedItem ?? App.ViewModel.ActionBarItem ?? App.ViewModel.RenameItem;
        if (highlighted == null)
        {
            overlay.Visibility = WpfVisibility.Collapsed;
            return;
        }

        ListBox ownerList = ResultsList;
        var container = ResultsList.ItemContainerGenerator.ContainerFromItem(highlighted) as ListBoxItem;
        if (container == null)
        {
            container = FavoritesList.ItemContainerGenerator.ContainerFromItem(highlighted) as ListBoxItem;
            if (container != null)
                ownerList = FavoritesList;
        }

        if (container == null)
        {
            overlay.Margin = new Thickness(8, 0, 8, 0);
            overlay.Visibility = WpfVisibility.Visible;
            return;
        }

        var transform = container.TransformToAncestor(ownerList);
        var point = transform.Transform(new WpfPoint(0, container.ActualHeight));

        overlay.Margin = new Thickness(8, point.Y, 8, 0);
        overlay.Visibility = WpfVisibility.Visible;
    }

    // ── Focus Handlers ─────────────────────────────────────────────

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
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }
        else if (e.Key == Key.Tab && App.ViewModel.IsSheetRename)
        {
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

    // ── Main Keyboard Handler ──────────────────────────────────────

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
                // Backspace on empty text with a colored mode pill → deactivate to Browser
                if (string.IsNullOrEmpty(App.ViewModel.SearchText) && App.ViewModel.ActiveMode != ActiveMode.Browser)
                {
                    e.Handled = true;
                    App.ViewModel.DeactivateMode();
                    UpdateModePillAccent();
                    UpdateEmptyState();
                    UpdateResultGrouping();
                }
                break;

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
                else if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.TypeLevel)
                {
                    // Escape in Stage 2 → back to Stage 1
                    App.ViewModel.NavigateBackToFamilies();
                }
                else if (App.ViewModel.ActiveMode != ActiveMode.Browser)
                {
                    // Escape with a colored mode pill → clear and return to Browser
                    App.ViewModel.DeactivateMode();
                    UpdateModePillAccent();
                    UpdateEmptyState();
                    UpdateResultGrouping();
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
                if (App.ViewModel.HasInlineError)
                {
                    App.ViewModel.ClearInlineError();
                    break;
                }
                if (App.ViewModel.IsActionBarVisible)
                {
                    App.ViewModel.ExecuteAction();
                    break;
                }
                var highlighted = App.ViewModel.HighlightedItem;
                if (highlighted != null)
                {
                    // Two-stage family nav: drill into family at Stage 1
                    if (App.ViewModel.IsFamilyMode
                        && App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
                        && highlighted.TypeCount > 0)
                    {
                        App.ViewModel.DrillIntoFamily(highlighted);
                        break;
                    }

                    // Back row → navigate back
                    if (highlighted.IsBackRow)
                    {
                        App.ViewModel.NavigateBackToFamilies();
                        break;
                    }

                    // Record usage for recent items
                    string modeKey = App.ViewModel.ActiveMode.ToString();
                    RecentItemsStore.RecordUsage(modeKey, highlighted.ElementId, highlighted.Name);

                    if (App.ViewModel.IsShebangMode && highlighted.Kind == BrowserItemKind.Shebang)
                    {
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
                        var cmdName = highlighted.CommandName;
                        if (cmdName == "__rauncher_settings")
                        {
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var window = new SettingsWindow();
                                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                                    helper.Owner = uiApp.MainWindowHandle;
                                    window.ShowDialog();
                                });
                            });
                        }
                        else if (cmdName != null && Enum.TryParse<PostableCommand>(cmdName, out var postableCmd))
                        {
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                var pane = uiApp.GetDockablePane(App.PaneId);
                                pane.Hide();
                                var cmdId = RevitCommandId.LookupPostableCommandId(postableCmd);
                                uiApp.PostCommand(cmdId);
                            });
                        }
                        else if (highlighted.RevitCommandId != null)
                        {
                            var revitCmdIdStr = highlighted.RevitCommandId;
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                var pane = uiApp.GetDockablePane(App.PaneId);
                                pane.Hide();
                                try
                                {
                                    var cmdId = RevitCommandId.LookupCommandId(revitCmdIdStr);
                                    if (cmdId != null)
                                        uiApp.PostCommand(cmdId);
                                }
                                catch
                                {
                                    Dispatcher.Invoke(() =>
                                        App.ViewModel.ShowInlineError("Command unavailable in current context"));
                                }
                            });
                        }
                    }
                    else if (App.ViewModel.IsFamilyMode && highlighted.Kind == BrowserItemKind.FamilyType)
                    {
                        if (App.ViewModel.IsEditFamilyMode)
                        {
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
                // In family mode Stage 1, Tab drills into family (same as Enter)
                if (App.ViewModel.IsFamilyMode
                    && App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
                    && App.ViewModel.HighlightedItem is BrowserItem familyItem
                    && familyItem.TypeCount > 0)
                {
                    App.ViewModel.DrillIntoFamily(familyItem);
                    break;
                }
                // Otherwise, open action bar
                if (App.ViewModel.HighlightedItem is BrowserItem actionItem)
                    App.ViewModel.OpenActionBar(actionItem);
                break;

            case Key.Right:
                // In family mode Stage 1 with action bar not open, drill into family
                if (!App.ViewModel.IsActionBarVisible
                    && App.ViewModel.IsFamilyMode
                    && App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
                    && App.ViewModel.HighlightedItem is BrowserItem rightFamilyItem
                    && rightFamilyItem.TypeCount > 0)
                {
                    e.Handled = true;
                    App.ViewModel.DrillIntoFamily(rightFamilyItem);
                    break;
                }
                if (App.ViewModel.IsActionBarVisible)
                {
                    e.Handled = true;
                    App.ViewModel.MoveActionFocus(+1);
                }
                break;

            case Key.Left when App.ViewModel.IsActionBarVisible:
                e.Handled = true;
                App.ViewModel.MoveActionFocus(-1);
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

            case Key.E when App.ViewModel.IsActionBarVisible && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                ExecuteActionByKind(Models.QuickActionKind.ExcelExport);
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

        if (App.ViewModel.Favorites.Contains(item))
            FavoritesList.ScrollIntoView(item);
        else if (App.ViewModel.Results.Contains(item))
            ResultsList.ScrollIntoView(item);
    }

    // ── Theme ──────────────────────────────────────────────────────

    private void ApplyTheme(bool isDark)
    {
        string themeFile = isDark
            ? "Views/DarkTheme.xaml"
            : "Views/LightTheme.xaml";

        var themeUri = new Uri($"pack://application:,,,/w_finder;component/{themeFile}");
        var themeDictionary = new ResourceDictionary { Source = themeUri };

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(themeDictionary);

        // Re-apply pill accent after theme change
        if (App.ViewModel.HasModePill)
            UpdateModePillAccent();
    }

    // ── Event Handlers ─────────────────────────────────────────────

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
                    uidoc.RequestViewChange(view);
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

    private bool _syncingSelections;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelections) return;
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
            // Two-stage family nav: double-click drills into family at Stage 1
            if (App.ViewModel.IsFamilyMode
                && App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
                && item.TypeCount > 0)
            {
                App.ViewModel.DrillIntoFamily(item);
                return;
            }

            if (item.IsBackRow)
            {
                App.ViewModel.NavigateBackToFamilies();
                return;
            }

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

            case Models.QuickActionKind.ExcelExport:
                App.ViewModel.OpenExcelExportInput(item);
                break;

            case Models.QuickActionKind.AssignShortcut:
                App.ViewModel.OpenShortcutEdit(item);
                break;

            case Models.QuickActionKind.RemoveShortcut:
                HandleRemoveShortcut(item);
                break;
        }
    }

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

                if (!string.IsNullOrEmpty(trimmedNumber) && trimmedNumber != sheet.SheetNumber)
                {
                    var existing = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Any(s => s.Id != sheet.Id && s.SheetNumber == trimmedNumber);

                    if (existing)
                    {
                        Dispatcher.Invoke(() =>
                            App.ViewModel.ShowInlineError($"Sheet number \"{trimmedNumber}\" already exists"));
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

            uidoc?.RefreshActiveView();

            Dispatcher.Invoke(() =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            });
            RefreshAfterMutation(uiApp);
        });
    }

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

            if (element is View viewToDelete && uidoc.ActiveView.Id == elementId)
            {
                var openViews = uidoc.GetOpenUIViews();
                if (openViews.Count <= 1)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError("Cannot delete the only open view"));
                    return;
                }

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

    // ── Shortcut Edit Handlers ─────────────────────────────────────

    private void OnShortcutEditConfirmed(BrowserItem item, string newShortcut)
    {
        var commandId = item.RevitCommandId;
        if (string.IsNullOrEmpty(commandId)) return;

        var trimmed = newShortcut.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            HandleRemoveShortcut(item);
            return;
        }

        var success = KeyboardShortcutService.AssignShortcut(commandId, trimmed);
        if (success)
        {
            item.ShortcutKeys = KeyboardShortcutService.GetShortcutByCommandId(commandId);
            CommandCollector.Invalidate();
            App.ViewModel.ShowInlineError("Shortcut saved — takes effect next Revit session");
        }
        else
        {
            App.ViewModel.ShowInlineError("Failed to save shortcut");
        }

        Dispatcher.Invoke(() =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }

    private void HandleRemoveShortcut(BrowserItem item)
    {
        var commandId = item.RevitCommandId;
        if (string.IsNullOrEmpty(commandId))
        {
            App.ViewModel.ShowInlineError("No command ID available");
            return;
        }

        if (string.IsNullOrEmpty(item.ShortcutKeys))
        {
            App.ViewModel.ShowInlineError("No shortcut assigned");
            return;
        }

        var success = KeyboardShortcutService.RemoveShortcut(commandId);
        if (success)
        {
            item.ShortcutKeys = null;
            CommandCollector.Invalidate();
            App.ViewModel.ShowInlineError("Shortcut removed — takes effect next Revit session");
        }
        else
        {
            App.ViewModel.ShowInlineError("Failed to remove shortcut");
        }
    }

    private void OnExcelExportConfirmed(BrowserItem item, string filename)
    {
        var sanitized = string.Join("_", filename.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Schedule";
        if (!sanitized.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            sanitized += ".xlsx";

        var exportDir = SettingsService.Current.DefaultExportPath;
        var fullPath = System.IO.Path.Combine(exportDir, sanitized);

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var element = doc.GetElement(new ElementId(item.ElementId));
            if (element is not ViewSchedule schedule)
            {
                Dispatcher.Invoke(() => App.ViewModel.ShowInlineError("Element is not a schedule"));
                return;
            }

            try
            {
                var tableData = schedule.GetTableData();
                var headerSection = tableData.GetSectionData(SectionType.Header);
                var bodySection = tableData.GetSectionData(SectionType.Body);

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var sheetName = schedule.Name.Length > 31
                    ? schedule.Name.Substring(0, 31)
                    : schedule.Name;
                var worksheet = workbook.Worksheets.Add(sheetName);

                int excelRow = 1;

                if (headerSection != null && headerSection.NumberOfRows > 0)
                {
                    int lastHeaderRow = headerSection.NumberOfRows - 1;
                    for (int c = 0; c < headerSection.NumberOfColumns; c++)
                    {
                        worksheet.Cell(1, c + 1).Value = schedule.GetCellText(SectionType.Header, lastHeaderRow, c);
                        worksheet.Cell(1, c + 1).Style.Font.Bold = true;
                    }
                    excelRow = 2;
                }

                if (bodySection != null && bodySection.NumberOfRows > 0)
                {
                    for (int r = 0; r < bodySection.NumberOfRows; r++)
                    {
                        for (int c = 0; c < bodySection.NumberOfColumns; c++)
                        {
                            worksheet.Cell(excelRow, c + 1).Value = schedule.GetCellText(SectionType.Body, r, c);
                        }
                        excelRow++;
                    }
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(fullPath);

                Dispatcher.Invoke(() => App.ViewModel.ShowInlineSuccess($"Exported to {fullPath}"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => App.ViewModel.ShowInlineError($"Export failed: {ex.Message}"));
            }
        });

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void RefreshAfterMutation(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;
        var items = BrowserItemCollector.Collect(doc);
        var favoriteIds = FavoritesStore.Load(doc);
        Dispatcher.Invoke(() =>
        {
            App.ViewModel.LoadItems(items, favoriteIds);
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });
    }
}
