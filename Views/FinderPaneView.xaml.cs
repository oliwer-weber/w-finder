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
        App.ViewModel.ReportActionRequested += OnReportActionRequested;
        App.ViewModel.ReportElementActivated += OnReportElementActivated;
        App.ViewModel.InspectorEditConfirmed += OnInspectorEditConfirmed;
        App.ViewModel.InspectorRefreshRequested += OnInspectorRefreshRequested;
        App.ViewModel.ParamFilterParamSelected += OnParamFilterParamSelected;
        App.ViewModel.ParamFilterExecuteRequested += OnParamFilterExecuteRequested;

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

        // Compute uniform pill width once from all possible pill texts
        ComputeUniformPillWidth();
    }

    private void ComputeUniformPillWidth()
    {
        var typeface = new Typeface(ModePillTextBlock.FontFamily, ModePillTextBlock.FontStyle,
            ModePillTextBlock.FontWeight, ModePillTextBlock.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(this);
        double pixelsPerDip = dpi.PixelsPerDip > 0 ? dpi.PixelsPerDip : 1.0;

        double maxWidth = 0;
        foreach (var text in FinderPaneViewModel.AllPillTexts)
        {
            var ft = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, ModePillTextBlock.FontSize, Brushes.Black,
                pixelsPerDip);
            if (ft.Width > maxWidth) maxWidth = ft.Width;
        }

        // Add horizontal padding (5 left + 5 right) + border (1+1)
        ModePillBorder.MinWidth = Math.Ceiling(maxWidth) + 12;
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
    /// All modes (including Browser) use the same formula:
    /// border at 100% accent, background at ~22% accent, tinted text.
    /// </summary>
    private void UpdateModePillAccent()
    {
        // Parameter Filter mode reuses the Filter family accent (gold/amber)
        if (App.ViewModel.IsInParameterFilterMode)
        {
            var pfColor = (WpfColor)FindResource("FilterAccentColor");
            var pfBrush = new SolidColorBrush(pfColor);
            pfBrush.Freeze();
            ModePillBorder.BorderBrush = pfBrush;
            ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);
            var pfBg = WpfColor.FromArgb((byte)(255 * 0.22), pfColor.R, pfColor.G, pfColor.B);
            ModePillBorder.Background = new SolidColorBrush(pfBg);
            ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("FilterPillText");
            return;
        }

        // Report mode borrows the quip purple — reports are always quip-launched
        if (App.ViewModel.IsInReportMode)
        {
            var reportColor = (WpfColor)FindResource("ShebangAccentColor");
            var reportBrush = new SolidColorBrush(reportColor);
            reportBrush.Freeze();
            ModePillBorder.BorderBrush = reportBrush;
            ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);
            var reportBg = WpfColor.FromArgb((byte)(255 * 0.22), reportColor.R, reportColor.G, reportColor.B);
            ModePillBorder.Background = new SolidColorBrush(reportBg);
            ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("ShebangPillText");
            return;
        }

        // Inspector mode uses warm amber/gold accent (same family as Filter)
        if (App.ViewModel.IsInInspectorMode)
        {
            var inspectColor = (WpfColor)FindResource("InspectAccentColor");
            var inspectBrush = new SolidColorBrush(inspectColor);
            inspectBrush.Freeze();
            ModePillBorder.BorderBrush = inspectBrush;
            ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);
            var inspectBg = WpfColor.FromArgb((byte)(255 * 0.22), inspectColor.R, inspectColor.G, inspectColor.B);
            ModePillBorder.Background = new SolidColorBrush(inspectBg);
            ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("InspectPillText");
            return;
        }

        // Selection filter mode uses its own amber/gold accent
        if (App.ViewModel.IsInSelectionFilterMode)
        {
            var filterColor = (WpfColor)FindResource("FilterAccentColor");
            var filterBrush = new SolidColorBrush(filterColor);
            filterBrush.Freeze();
            ModePillBorder.BorderBrush = filterBrush;
            ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);
            var filterBg = WpfColor.FromArgb((byte)(255 * 0.22), filterColor.R, filterColor.G, filterColor.B);
            ModePillBorder.Background = new SolidColorBrush(filterBg);
            ModePillTextBlock.Foreground = (SolidColorBrush)FindResource("FilterPillText");
            return;
        }

        // Resolve accent color and text brush for current mode
        WpfColor accentColor = App.ViewModel.ActiveMode switch
        {
            ActiveMode.Place => (WpfColor)FindResource("PlaceAccentColor"),
            ActiveMode.Command => (WpfColor)FindResource("CommandAccentColor"),
            ActiveMode.Shebang => (WpfColor)FindResource("ShebangAccentColor"),
            _ => (WpfColor)FindResource("BrowserAccentColor")
        };

        string textBrushKey = App.ViewModel.ActiveMode switch
        {
            ActiveMode.Place => "PlacePillText",
            ActiveMode.Command => "CommandPillText",
            ActiveMode.Shebang => "ShebangPillText",
            _ => "BrowserPillText"
        };

        // All borders use accent color — thicker left for identity signal
        var accentBrush = new SolidColorBrush(accentColor);
        accentBrush.Freeze();
        ModePillBorder.BorderBrush = accentBrush;
        ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1);

        // Accent background tint at ~22% opacity
        var bgColor = WpfColor.FromArgb((byte)(255 * 0.22), accentColor.R, accentColor.G, accentColor.B);
        ModePillBorder.Background = new SolidColorBrush(bgColor);

        // Mode-tinted text
        ModePillTextBlock.Foreground = (SolidColorBrush)FindResource(textBrushKey);
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
        var vm = App.ViewModel;
        int maxChips = vm.AreCategoryChipsExpanded ? vm.AvailableCategories.Count : 6;
        int shown = 0;

        foreach (var cat in vm.AvailableCategories)
        {
            if (shown >= maxChips) break;

            var chip = new Border
            {
                Tag = shown,
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
            chip.MouseLeftButtonDown += (_, _) => vm.ApplyCategoryFilter(category);
            CategoryChipsPanel.Children.Add(chip);
            shown++;
        }

        // Show overflow count if needed (and not already expanded)
        int remaining = vm.AvailableCategories.Count - shown;
        if (remaining > 0 && !vm.AreCategoryChipsExpanded)
        {
            var moreElement = new Border
            {
                Tag = shown,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 4),
                Background = (Brush)FindResource("ItemHoverBackground"),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = $"+{remaining} more",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("MutedText"),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            moreElement.MouseLeftButtonDown += (_, _) =>
            {
                vm.ExpandCategoryChips();
                PopulateCategoryChips();
                UpdateChipHighlight();
            };
            CategoryChipsPanel.Children.Add(moreElement);
            shown++;
        }

        vm.VisibleChipCount = shown;
    }

    private void UpdateChipHighlight()
    {
        var vm = App.ViewModel;
        var accentColor = (WpfColor)FindResource("PlaceAccentColor");
        var focusBrush = new SolidColorBrush(accentColor);
        var defaultBrush = (Brush)FindResource("ItemHoverBackground");
        var defaultTextBrush = (Brush)FindResource("PrimaryText");
        var mutedTextBrush = (Brush)FindResource("MutedText");

        foreach (var child in CategoryChipsPanel.Children)
        {
            if (child is Border border && border.Tag is int idx)
            {
                bool isFocused = vm.IsChipNavigationActive && idx == vm.FocusedChipIndex;
                border.Background = isFocused ? focusBrush : defaultBrush;
                if (border.Child is TextBlock tb)
                {
                    // The "+x more" chip has MutedText foreground by default
                    bool isOverflow = idx >= Math.Min(6, vm.AvailableCategories.Count);
                    tb.Foreground = isFocused ? Brushes.White : (isOverflow ? mutedTextBrush : defaultTextBrush);
                }
            }
        }
    }


    // ── Result Grouping ────────────────────────────────────────────

    private void UpdateResultGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(ResultsList.ItemsSource);
        if (view == null) return;

        view.GroupDescriptions.Clear();

        // Group by GroupKey for all modes except Shebang, Selection Filter, Inspector,
        // and Parameter Filter (the last two have their own flat layouts).
        if (App.ViewModel.IsInReportMode)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.GroupKey)));
        }
        else if (!App.ViewModel.IsShebangMode && !App.ViewModel.IsInSelectionFilterMode
            && !App.ViewModel.IsInInspectorMode && !App.ViewModel.IsInParameterFilterMode)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.GroupKey)));
        }
        // Inspector uses grouping for parameter groups
        else if (App.ViewModel.IsInInspectorMode && !App.ViewModel.InspectorIsMixedCategory)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.GroupKey)));
        }
    }

    // ── ViewModel Property Changed Handler ─────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinderPaneViewModel.IsActionBarVisible))
        {
            ActionBarPopup.Visibility = App.ViewModel.IsActionBarVisible
                ? WpfVisibility.Visible : WpfVisibility.Collapsed;
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

        if (e.PropertyName == nameof(FinderPaneViewModel.FocusedChipIndex)
            || e.PropertyName == nameof(FinderPaneViewModel.IsChipNavigationActive))
        {
            UpdateChipHighlight();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInSelectionFilterMode))
        {
            FilterHintBar.Visibility = App.ViewModel.IsInSelectionFilterMode
                ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            UpdateModePillAccent();
            UpdateResultGrouping();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInParameterFilterMode))
        {
            ParamFilterHintBar.Visibility = App.ViewModel.IsInParameterFilterMode
                ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            UpdateModePillAccent();
            UpdateResultGrouping();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInReportMode))
        {
            ReportHintBar.Visibility = App.ViewModel.IsInReportMode
                ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            UpdateModePillAccent();
            UpdateResultGrouping();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInInspectorMode))
        {
            InspectorHintBar.Visibility = App.ViewModel.IsInInspectorMode
                ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            UpdateModePillAccent();
            UpdateResultGrouping();
            return;
        }

        if (e.PropertyName == nameof(FinderPaneViewModel.IsInspectorEditMode))
        {
            if (App.ViewModel.IsInspectorEditMode)
            {
                PositionOverlay(InspectorEditPopup);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    InspectorEditBox.Focus();
                    Keyboard.Focus(InspectorEditBox);
                    InspectorEditBox.SelectAll();
                });
            }
            else
            {
                InspectorEditPopup.Visibility = WpfVisibility.Collapsed;
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }
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

            // At Type Level (Place mode or unified family drilldown), auto-select the
            // highlighted type in Revit so the Properties pane updates to reflect the
            // family/type. This is needed for Edit Family to work — Revit requires the
            // type selected.
            if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.TypeLevel
                && App.ViewModel.HighlightedItem is BrowserItem hi
                && !hi.IsBackRow
                && hi.Kind == BrowserItemKind.FamilyType
                && hi.ElementId > 0)
            {
                var elementId = hi.ElementId;
                RevitBackgroundTask.Raise(uiApp =>
                {
                    var uidoc = uiApp.ActiveUIDocument;
                    if (uidoc == null) return;
                    uidoc.Selection.SetElementIds(new List<ElementId> { new ElementId(elementId) });
                });
            }

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
        bool shouldShow;
        if (overlay == InlineRenamePopup)
            shouldShow = App.ViewModel.IsInlineRenameVisible;
        else if (overlay == InspectorEditPopup)
            shouldShow = App.ViewModel.IsInspectorEditMode;
        else
            shouldShow = App.ViewModel.HasInlineError;

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
            SearchBox.CaretIndex = SearchBox.Text.Length;
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
        var vm = App.ViewModel;

        // Report mode (!dwg, !lp) intercepts keys first
        if (vm.IsInReportMode)
        {
            HandleReportKeyDown(e);
            return;
        }

        // Parameter Filter mode intercepts keys first
        if (vm.IsInParameterFilterMode)
        {
            HandleParameterFilterKeyDown(e);
            return;
        }

        // Inspector mode intercepts keys first
        if (vm.IsInInspectorMode)
        {
            HandleInspectorKeyDown(e);
            return;
        }

        // Selection filter mode intercepts all keys first
        if (vm.IsInSelectionFilterMode)
        {
            switch (e.Key)
            {
                case Key.Space:
                    vm.FilterToggleChecked();
                    e.Handled = true;
                    return;
                case Key.Right:
                    vm.FilterDrillIn();
                    e.Handled = true;
                    return;
                case Key.Left:
                    vm.FilterGoBack();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    ApplySelectionFilter();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    vm.ExitSelectionFilterMode();
                    UpdateModePillAccent();
                    UpdateResultGrouping();
                    e.Handled = true;
                    return;
                case Key.Up:
                    vm.MoveHighlight(-1);
                    ScrollHighlightedIntoView();
                    e.Handled = true;
                    return;
                case Key.Down:
                    vm.MoveHighlight(1);
                    ScrollHighlightedIntoView();
                    e.Handled = true;
                    return;
                case Key.Back:
                    if (string.IsNullOrEmpty(vm.SearchText))
                    {
                        vm.FilterGoBack();
                        e.Handled = true;
                    }
                    // else: let Backspace delete search text normally
                    return;
            }
            // All other keys: let typing through for fuzzy search within current level
            return;
        }

        switch (e.Key)
        {
            case Key.Back:
                // Backspace on empty text → navigate back one level
                if (string.IsNullOrEmpty(App.ViewModel.SearchText))
                {
                    if (App.ViewModel.IsActionBarVisible)
                    {
                        e.Handled = true;
                        App.ViewModel.CloseActionBar();
                    }
                    else if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.TypeLevel)
                    {
                        e.Handled = true;
                        App.ViewModel.NavigateBackToFamilies();
                    }
                    else if (App.ViewModel.ActiveMode != ActiveMode.Browser)
                    {
                        e.Handled = true;
                        App.ViewModel.DeactivateMode();
                        UpdateModePillAccent();
                        UpdateEmptyState();
                        UpdateResultGrouping();
                    }
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
                else
                {
                    // Universal close — save state, then hide
                    SettingsService.SaveLastState((int)App.ViewModel.ActiveMode, App.ViewModel.SearchText);
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
                if (App.ViewModel.IsChipNavigationActive)
                {
                    int chipIdx = App.ViewModel.FocusedChipIndex;
                    int maxVisible = App.ViewModel.AreCategoryChipsExpanded
                        ? App.ViewModel.AvailableCategories.Count
                        : Math.Min(6, App.ViewModel.AvailableCategories.Count);

                    if (chipIdx < maxVisible)
                    {
                        App.ViewModel.ApplyCategoryFilter(App.ViewModel.AvailableCategories[chipIdx]);
                        UpdateEmptyState();
                        UpdateResultGrouping();
                    }
                    else
                    {
                        // "+x more" — expand all chips
                        App.ViewModel.ExpandCategoryChips();
                        PopulateCategoryChips();
                        App.ViewModel.FocusedChipIndex = Math.Min(chipIdx, App.ViewModel.VisibleChipCount - 1);
                        UpdateChipHighlight();
                    }
                    break;
                }
                var highlighted = App.ViewModel.HighlightedItem;
                if (highlighted != null)
                {
                    // Save state before executing (pane may hide)
                    SettingsService.SaveLastState((int)App.ViewModel.ActiveMode, App.ViewModel.SearchText);

                    // Family rollup row → drill into types. TypeCount > 0 marks a rollup
                    // whether we're in unified Browser or > prefix mode.
                    if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
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

                    UsageStore.RecordUsage(highlighted);

                    if (highlighted.Kind == BrowserItemKind.Shebang)
                    {
                        var shebangId = highlighted.CommandName;
                        if (shebangId == "sf")
                        {
                            LaunchSelectionFilter();
                            break;
                        }
                        if (shebangId == "pi")
                        {
                            LaunchPropertyInspector();
                            break;
                        }
                        if (shebangId == "pf")
                        {
                            LaunchParameterFilter();
                            break;
                        }
                        if (shebangId == "dwg")
                        {
                            LaunchCadReport();
                            break;
                        }
                        if (shebangId == "lp")
                        {
                            LaunchLinePatternCleanup();
                            break;
                        }
                        if (shebangId == "ut")
                        {
                            LaunchUnusedTypes();
                            break;
                        }
                        if (shebangId == "us")
                        {
                            LaunchUnusedTypesOfSelection();
                            break;
                        }
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
                    else if (highlighted.Kind == BrowserItemKind.Command)
                    {
                        var cmdName = highlighted.CommandName;
                        if (cmdName == "__quip_settings")
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
                                RevitCommandId? cmdId = null;
                                bool canPost = false;
                                Exception? postEx = null;
                                try
                                {
                                    cmdId = RevitCommandId.LookupPostableCommandId(postableCmd);
                                    canPost = cmdId != null && uiApp.CanPostCommand(cmdId);
                                    if (cmdId != null && canPost)
                                    {
                                        var pane = uiApp.GetDockablePane(App.PaneId);
                                        pane.Hide();
                                        try { uiApp.PostCommand(cmdId); }
                                        catch (Exception ex) { postEx = ex; }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    postEx = ex;
                                }
                                finally
                                {
                                    CommandDiagnostics.LogAttempt("PostableCommand", cmdName, cmdId, canPost, uiApp, postEx);
                                }

                                if (cmdId == null || !canPost || postEx != null)
                                {
                                    Dispatcher.Invoke(() =>
                                        App.ViewModel.ShowInlineError("Command not available in the current context"));
                                }
                            });
                        }
                        else if (highlighted.RevitCommandId != null)
                        {
                            var revitCmdIdStr = highlighted.RevitCommandId;
                            RevitBackgroundTask.Raise(uiApp =>
                            {
                                // 1) Shortcut path. If Revit has a keyboard shortcut assigned
                                //    to this command, send those keys — that routes through
                                //    Revit's own dispatcher, identical to a real keypress.
                                //    Re-read from XML cache so same-session edits are picked up.
                                var shortcut = KeyboardShortcutService.GetShortcutByCommandId(revitCmdIdStr);
                                if (!string.IsNullOrEmpty(shortcut))
                                {
                                    var pane = uiApp.GetDockablePane(App.PaneId);
                                    pane.Hide();
                                    bool keysSent = KeystrokeSimulator.TrySend(shortcut, uiApp.MainWindowHandle, out var keysDiag);
                                    if (keysSent)
                                    {
                                        CommandDiagnostics.LogAttempt("Internal", revitCmdIdStr, null, false, uiApp, null, $"keys={keysDiag}");
                                        return;
                                    }
                                    // Parse failed — fall through to other mechanisms.
                                    // The pane is already hidden; that's fine.
                                }

                                // 2) Try ribbon-execute. Reaches internal system commands
                                //    (ID_...) that PostCommand refuses, by invoking the live
                                //    ribbon button's CommandHandler directly.
                                bool ribbonOk = RibbonCommandInvoker.TryExecute(revitCmdIdStr, out var ribbonDiag);
                                if (ribbonOk)
                                {
                                    CommandDiagnostics.LogAttempt("Internal", revitCmdIdStr, null, false, uiApp, null, ribbonDiag);
                                    var pane = uiApp.GetDockablePane(App.PaneId);
                                    pane.Hide();
                                    return;
                                }

                                // 2) Fallback: PostCommand. Catches external add-in commands
                                //    (CustomCtrl_...) that Revit accepts but that aren't in
                                //    the live ribbon tree.
                                RevitCommandId? cmdId = null;
                                bool canPost = false;
                                Exception? postEx = null;
                                bool postAttempted = false;
                                try
                                {
                                    cmdId = RevitCommandId.LookupCommandId(revitCmdIdStr);
                                    if (cmdId != null)
                                    {
                                        canPost = uiApp.CanPostCommand(cmdId);
                                        if (canPost)
                                        {
                                            var pane = uiApp.GetDockablePane(App.PaneId);
                                            pane.Hide();
                                            postAttempted = true;
                                            try { uiApp.PostCommand(cmdId); }
                                            catch (Exception ex) { postEx = ex; }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    postEx = ex;
                                }
                                finally
                                {
                                    CommandDiagnostics.LogAttempt("Internal", revitCmdIdStr, cmdId, canPost, uiApp, postEx, ribbonDiag);
                                }

                                if (!postAttempted || postEx != null)
                                {
                                    var msg = postEx != null
                                        ? $"Command failed: {postEx.Message}"
                                        : "Command not available in the current context";
                                    Dispatcher.Invoke(() => App.ViewModel.ShowInlineError(msg));
                                }
                            });
                        }
                    }
                    else if (highlighted.Kind == BrowserItemKind.FamilyType)
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
                // Family rollup row → Tab drills (same as Enter), regardless of mode
                if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
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
                if (App.ViewModel.IsChipNavigationActive)
                {
                    e.Handled = true;
                    App.ViewModel.MoveChipFocus(+1);
                    break;
                }
                // Family rollup row → Right drills into types, regardless of mode
                if (!App.ViewModel.IsActionBarVisible
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

            case Key.Left:
                if (App.ViewModel.IsChipNavigationActive)
                {
                    e.Handled = true;
                    App.ViewModel.MoveChipFocus(-1);
                }
                else if (App.ViewModel.IsActionBarVisible)
                {
                    e.Handled = true;
                    App.ViewModel.MoveActionFocus(-1);
                }
                break;

            case Key.Down:
                e.Handled = true;
                if (App.ViewModel.IsChipNavigationActive)
                {
                    // Down from chips → exit chips, enter results
                    App.ViewModel.ExitChipNavigation();
                    App.ViewModel.MoveHighlight(+1);
                    ScrollHighlightedIntoView();
                }
                else if (App.ViewModel.IsEmptyState
                         && App.ViewModel.ActiveMode == ActiveMode.Place
                         && App.ViewModel.HighlightedItem == null
                         && App.ViewModel.VisibleChipCount > 0)
                {
                    // Down from search box in Place empty state → enter chips
                    App.ViewModel.EnterChipNavigation();
                }
                else
                {
                    App.ViewModel.MoveHighlight(+1);
                    ScrollHighlightedIntoView();
                }
                break;

            case Key.Up:
                e.Handled = true;
                if (App.ViewModel.IsChipNavigationActive)
                {
                    // Up from chips → back to search box
                    App.ViewModel.ExitChipNavigation();
                }
                else if (App.ViewModel.IsEmptyState
                         && App.ViewModel.ActiveMode == ActiveMode.Place
                         && App.ViewModel.VisibleChipCount > 0
                         && App.ViewModel.HighlightedItem != null
                         && App.ViewModel.Results.Count > 0
                         && App.ViewModel.HighlightedItem == App.ViewModel.Results[0])
                {
                    // Up from top of results in Place empty state → enter chips at last chip
                    App.ViewModel.EnterChipNavigation();
                    App.ViewModel.FocusedChipIndex = App.ViewModel.VisibleChipCount - 1;
                }
                else
                {
                    App.ViewModel.MoveHighlight(-1);
                    ScrollHighlightedIntoView();
                }
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
                // Both EditFamily and ExcelExport use "E" shortcut — pick whichever is in the active actions.
                if (App.ViewModel.ActiveActions.Any(a => a.Kind == Models.QuickActionKind.EditFamily))
                    ExecuteActionByKind(Models.QuickActionKind.EditFamily);
                else
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

        var themeUri = new Uri($"pack://application:,,,/Quip;component/{themeFile}");
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
        // Save state before executing (pane may hide)
        SettingsService.SaveLastState((int)App.ViewModel.ActiveMode, App.ViewModel.SearchText);

        UsageStore.RecordUsage(item);

        if (item.Kind == BrowserItemKind.Shebang)
        {
            var shebangId = item.CommandName;
            if (shebangId == "sf")
            {
                LaunchSelectionFilter();
                return;
            }
            if (shebangId == "pi")
            {
                LaunchPropertyInspector();
                return;
            }
            if (shebangId == "pf")
            {
                LaunchParameterFilter();
                return;
            }
            if (shebangId == "dwg")
            {
                LaunchCadReport();
                return;
            }
            if (shebangId == "lp")
            {
                LaunchLinePatternCleanup();
                return;
            }
            if (shebangId == "ut")
            {
                LaunchUnusedTypes();
                return;
            }
            if (shebangId == "us")
            {
                LaunchUnusedTypesOfSelection();
                return;
            }
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

        if (item.Kind == BrowserItemKind.FamilyType)
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

    // ── Selection Filter ───────────────────────────────────────────

    /// <summary>
    /// Reads the current Revit selection on the API thread, builds the filter tree,
    /// and enters selection filter mode on the UI thread.
    /// </summary>
    private void LaunchSelectionFilter()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                Dispatcher.Invoke(() =>
                    App.ViewModel.ShowInlineError("Select elements in the model first"));
                return;
            }

            var doc = uidoc.Document;
            var elements = new List<ElementData>();

            foreach (var id in selIds)
            {
                var elem = doc.GetElement(id);
                if (elem == null) continue;

                string catName = elem.Category?.Name ?? "Uncategorized";
                string famName;
                string typeName;

                if (elem is FamilyInstance fi)
                {
                    famName = fi.Symbol?.Family?.Name ?? "Unknown";
                    typeName = fi.Symbol?.Name ?? "";
                }
                else
                {
                    // System families: try ALL_MODEL_FAMILY_NAME on the type element
                    var typeId = elem.GetTypeId();
                    var typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                    var famParam = typeElem?.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                    famName = famParam?.AsString() ?? elem.Category?.Name ?? "Other";
                    typeName = typeElem?.Name ?? "";
                }

                // Instance label: Mark parameter if available, else element ID
                var mark = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                string instanceLabel = !string.IsNullOrEmpty(mark) ? mark : $"#{id.Value}";

                elements.Add(new ElementData
                {
                    ElementId = id.Value,
                    CategoryName = catName,
                    FamilyName = famName,
                    TypeName = typeName,
                    InstanceLabel = instanceLabel
                });
            }

            var roots = SelectionFilterNode.Build(elements);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.EnterSelectionFilterMode(roots);
                UpdateModePillAccent();
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// Applies the current filter selection to Revit and exits filter mode.
    /// </summary>
    private void ApplySelectionFilter()
    {
        var checkedIds = App.ViewModel.GetFilterCheckedElementIds();

        if (checkedIds.Count == 0)
        {
            App.ViewModel.ShowInlineError("No elements selected");
            return;
        }

        int count = checkedIds.Count;
        App.ViewModel.ExitSelectionFilterMode();
        UpdateModePillAccent();
        UpdateResultGrouping();

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;
            var elementIds = checkedIds.Select(id => new ElementId(id)).ToList();
            uidoc.Selection.SetElementIds(elementIds);

            Dispatcher.Invoke(() =>
                App.ViewModel.ShowInlineSuccess($"Selection refined to {count} elements"));
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
            // Family rollup row → double-click drills, regardless of mode
            if (App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel
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
                OpenRevitShortcutDialogWithSearch(item);
                break;

            case Models.QuickActionKind.RemoveShortcut:
                HandleRemoveShortcut(item);
                break;

            case Models.QuickActionKind.EditFamily:
                HandleEditFamily(item);
                break;
        }
    }

    private void HandleEditFamily(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            try
            {
                var uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError("No active document"));
                    return;
                }
                var doc = uidoc.Document;
                var element = doc.GetElement(new ElementId(item.ElementId));

                if (element == null)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError($"Element not found (ID {item.ElementId})"));
                    return;
                }

                // Resolve the Family object.
                Family? family = element switch
                {
                    FamilySymbol sym => sym.Family,
                    Family fam => fam,
                    FamilyInstance inst => inst.Symbol?.Family,
                    _ => null
                };

                if (family == null)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError($"Not a family element ({element.GetType().Name})"));
                    return;
                }

                if (!family.IsEditable)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError("Family is not editable (in-place or linked)"));
                    return;
                }

                // Extract the family to a temp .rfa file, then open it with
                // OpenAndActivateDocument — this forces Revit to switch to the
                // family editor UI, same as File > Open.
                var familyDoc = doc.EditFamily(family);
                if (familyDoc == null)
                {
                    Dispatcher.Invoke(() =>
                        App.ViewModel.ShowInlineError("Revit could not open family for editing"));
                    return;
                }

                var familyName = family.Name;
                var tempPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    familyName + ".rfa");

                var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                familyDoc.SaveAs(tempPath, saveOpts);
                familyDoc.Close(false);

                // Hide the pane, then open the family file.
                var pane = uiApp.GetDockablePane(App.PaneId);
                pane.Hide();

                uiApp.OpenAndActivateDocument(tempPath);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    App.ViewModel.ShowInlineError($"Edit failed: {ex.Message}"));
            }
        });
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

                using var tx = new Transaction(doc, "Quip: Rename Sheet");
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
                    using var tx = new Transaction(doc, "Quip: Rename");
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

            using var tx = new Transaction(doc, "Quip: Delete");
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
                using var tx = new Transaction(doc, "Quip: Duplicate");
                tx.Start();
                view.Duplicate(option);
                tx.Commit();
            }

            RefreshAfterMutation(uiApp);
        });
    }

    // ── Shortcut Edit Handlers ─────────────────────────────────────

    /// <summary>
    /// Opens Revit's native Keyboard Shortcuts dialog and types the command name
    /// into its search box, so the user only has to assign keys and click OK.
    /// Revit's own OK handler does the hot-reload, so the new shortcut works
    /// immediately — sidestepping the in-memory-binding refresh problem entirely.
    /// </summary>
    private void OpenRevitShortcutDialogWithSearch(BrowserItem item)
    {
        var searchTerm = item.Name;

        RevitBackgroundTask.Raise(uiApp =>
        {
            try
            {
                var pane = uiApp.GetDockablePane(App.PaneId);
                pane.Hide();

                var cmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.KeyboardShortcuts);
                if (cmdId == null || !uiApp.CanPostCommand(cmdId)) return;
                uiApp.PostCommand(cmdId);
            }
            catch
            {
                return;
            }

            // PostCommand only queues — dialog opens after this Raise() returns.
            // Spawn a background task that waits for the dialog to appear and
            // auto-focus its search box, then types the search term.
            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(300);
                KeystrokeSimulator.SendText(searchTerm);
            });
        });
    }

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
            item.ShortcutKeys = KeyboardShortcutService.GetShortcutByCommandId(commandId) ?? "-";
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

        if (string.IsNullOrEmpty(item.ShortcutKeys) || item.ShortcutKeys == "-")
        {
            App.ViewModel.ShowInlineError("No shortcut assigned");
            return;
        }

        var success = KeyboardShortcutService.RemoveShortcut(commandId);
        if (success)
        {
            item.ShortcutKeys = "-";
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

    // ── In-Pane Report: !ut (unused types) ────────────────────────
    //
    // Flow: scan → pick categories → confirm → delete. Selection is held on the
    // type IDs rather than on the rows, so a category row and the type rows
    // underneath it are always two views of the same truth.

    /// <summary>The most recent unused-type scan, kept for the delete actions.</summary>
    private UnusedTypeScan? _unusedScan;

    /// <summary>Element IDs of the types currently ticked for deletion.</summary>
    private readonly HashSet<long> _unusedSelected = new();

    /// <summary>
    /// Null for !ut (whole project). For !us, the selection's sibling families,
    /// captured once at launch so the rescan after a delete stays on them.
    /// </summary>
    private UnusedTypeScope? _unusedScope;

    /// <summary>Breadcrumb prefix, so a scoped report never looks like a project-wide one.</summary>
    private string UnusedCrumb => _unusedScope == null ? "TYPES" : "TYPES IN SELECTION";

    /// <summary>
    /// !ut — scans model categories for element types with zero instances and
    /// opens the category picker in the pane.
    /// </summary>
    private void LaunchUnusedTypes()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var scan = UnusedTypeService.Scan(doc);

            Dispatcher.Invoke(() => OpenUnusedReport(scan, null));
        });
    }

    /// <summary>
    /// !us: same report as !ut, but only for the other types of the families
    /// the selected elements belong to (all "Basic Wall" types for a basic
    /// wall, every type of the door family for a door, and so on).
    /// </summary>
    private void LaunchUnusedTypesOfSelection()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                Dispatcher.Invoke(() =>
                    App.ViewModel.ShowInlineError("Select elements in the model first"));
                return;
            }

            var scope = UnusedTypeScope.FromSelection(doc, selIds);
            if (scope.IsEmpty)
            {
                Dispatcher.Invoke(() =>
                    App.ViewModel.ShowInlineError("Nothing in the selection has a type"));
                return;
            }

            var scan = UnusedTypeService.Scan(doc, scope);

            Dispatcher.Invoke(() => OpenUnusedReport(scan, scope));
        });
    }

    /// <summary>Shared by !ut and !us: resets the ticks and opens the category page.</summary>
    private void OpenUnusedReport(UnusedTypeScan scan, UnusedTypeScope? scope)
    {
        _unusedScope = scope;
        _unusedScan = scan;
        _unusedSelected.Clear();
        App.ViewModel.EnterReportMode(
            BuildUnusedCategoryPage(scan), "TYPES", DrillUnusedTypes);
        UpdateModePillAccent();
        UpdateResultGrouping();
    }

    /// <summary>The unused types in one category, in scan order.</summary>
    private List<UnusedType> TypesInCategory(string category) =>
        _unusedScan == null
            ? new List<UnusedType>()
            : _unusedScan.Unused.Where(t => t.CategoryName == category).ToList();

    /// <summary>Tri-state for a category: all ticked, none ticked, or some.</summary>
    private bool? CategoryCheckState(string category)
    {
        var types = TypesInCategory(category);
        if (types.Count == 0) return false;

        var ticked = types.Count(t => _unusedSelected.Contains(t.ElementId));
        if (ticked == 0) return false;
        if (ticked == types.Count) return true;
        return null;
    }

    /// <summary>
    /// Space on a category row: tick the whole category, unless it is already
    /// fully ticked, in which case clear it.
    /// </summary>
    private void ToggleCategory(string category)
    {
        var types = TypesInCategory(category);
        var turnOn = CategoryCheckState(category) != true;

        foreach (var t in types)
        {
            if (turnOn) _unusedSelected.Add(t.ElementId);
            else _unusedSelected.Remove(t.ElementId);
        }
    }

    /// <summary>The "Select all categories" row, and Ctrl+A / Ctrl+D.</summary>
    private void SetAllUnusedCategories(bool select)
    {
        _unusedSelected.Clear();
        if (select && _unusedScan != null)
        {
            foreach (var t in _unusedScan.Unused)
                _unusedSelected.Add(t.ElementId);
        }
        App.ViewModel.RefreshCurrentReportPage();
    }

    /// <summary>Flips between everything selected and nothing selected.</summary>
    private void ToggleAllUnusedCategories()
    {
        var everything = _unusedScan?.Unused.Count ?? 0;
        SetAllUnusedCategories(_unusedSelected.Count < everything);
    }

    /// <summary>
    /// The root page: one row per category with a tri-state tick, a select-all
    /// row, and the run row. Space ticks a whole category, Enter opens it.
    /// </summary>
    private ReportPage BuildUnusedCategoryPage(UnusedTypeScan scan)
    {
        var page = new ReportPage
        {
            Hint = "Space: tick category · Enter: open · Ctrl+A / Ctrl+D: all / none · Esc: close",
            SetAllChecks = SetAllUnusedCategories,
        };

        if (scan.Unused.Count == 0)
        {
            page.Breadcrumb = $"{UnusedCrumb} · 0 unused of {scan.TotalTypes}";
            page.Rows.Add(new ReportRow
            {
                Text = "Nothing to purge",
                Label = _unusedScope == null
                    ? $"All {scan.TotalTypes} model types have at least one instance"
                    : $"All {scan.TotalTypes} types in the selection's families are in use",
                GroupKey = "UNUSED TYPES",
            });
            return page;
        }

        page.Rows.Add(new ReportRow
        {
            Text = "Select all categories",
            Label = "Toggles every category at once",
            Value = "Enter",
            ActionKey = "unused-select-all",
            GroupKey = "ACTIONS",
        });

        page.Rows.Add(new ReportRow
        {
            Text = "Delete selected types",
            Label = "Nothing ticked yet",
            ActionKey = "unused-delete-selected",
            GroupKey = "ACTIONS",
        });

        var categories = scan.Unused
            .GroupBy(t => t.CategoryName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in categories)
        {
            var category = group.Key;
            page.Rows.Add(new ReportRow
            {
                Text = category,
                IsCheckable = true,
                DrillKey = "unused-cat:" + category,
                GroupKey = "CATEGORIES",
                OnToggle = _ => ToggleCategory(category),
            });
        }

        page.Recalculate = p =>
        {
            var total = _unusedScan?.Unused.Count ?? 0;
            var ticked = _unusedSelected.Count;

            p.Breadcrumb = $"{UnusedCrumb} · {total} unused of {scan.TotalTypes} · {ticked} selected";

            var deleteRow = p.Rows[1];
            deleteRow.Text = ticked == 0
                ? "Delete selected types"
                : $"Delete {ticked} selected type" + (ticked == 1 ? "" : "s");
            deleteRow.Label = ticked == 0
                ? "Nothing ticked yet — Space ticks the highlighted category"
                : "Asks for confirmation first";
            deleteRow.Value = ticked == 0 ? null : "Enter";

            // Category rows: refresh tick state and the "n of m" counts, which
            // both change when the user edits a selection one level down.
            foreach (var row in p.Rows.Where(r => r.IsCheckable))
            {
                var types = TypesInCategory(row.Text);
                var selected = types.Count(t => _unusedSelected.Contains(t.ElementId));

                row.IsChecked = CategoryCheckState(row.Text);
                row.Value = $"{selected} of {types.Count}";
                row.Label = types.Count == 1 ? "1 unused type" : $"{types.Count} unused types";
            }
        };

        return page;
    }

    /// <summary>Enter on a category row: the types inside it, individually tickable.</summary>
    private ReportPage? DrillUnusedTypes(ReportRow row)
    {
        if (row.DrillKey == null || !row.DrillKey.StartsWith("unused-cat:")) return null;

        var category = row.DrillKey.Substring("unused-cat:".Length);
        var types = TypesInCategory(category);
        if (types.Count == 0) return null;

        var page = new ReportPage
        {
            BackLabel = "Categories",
            Hint = "Space: tick type · Ctrl+A / Ctrl+D: all / none · Esc: back",
            SetAllChecks = select =>
            {
                foreach (var t in types)
                {
                    if (select) _unusedSelected.Add(t.ElementId);
                    else _unusedSelected.Remove(t.ElementId);
                }
            },
        };

        foreach (var t in types)
        {
            var id = t.ElementId;
            page.Rows.Add(new ReportRow
            {
                Text = t.DisplayName,
                Value = id.ToString(),
                IsCheckable = true,
                GroupKey = category.ToUpperInvariant(),
                OnToggle = _ =>
                {
                    if (!_unusedSelected.Remove(id)) _unusedSelected.Add(id);
                },
            });
        }

        page.Recalculate = p =>
        {
            var selected = types.Count(t => _unusedSelected.Contains(t.ElementId));
            p.Breadcrumb = $"{UnusedCrumb} · {category} — {selected} of {types.Count} selected";

            foreach (var r in p.Rows.Where(r => r.IsCheckable))
            {
                if (long.TryParse(r.Value, out var id))
                    r.IsChecked = _unusedSelected.Contains(id);
            }
        };

        return page;
    }

    /// <summary>The ticked types, in scan order.</summary>
    private List<UnusedType> SelectedUnusedTypes() =>
        _unusedScan == null
            ? new List<UnusedType>()
            : _unusedScan.Unused.Where(t => _unusedSelected.Contains(t.ElementId)).ToList();

    /// <summary>The yes/no page shown before any type is actually deleted.</summary>
    private ReportPage BuildUnusedConfirmPage(List<UnusedType> targets, string backLabel)
    {
        var page = new ReportPage
        {
            Breadcrumb = $"{UnusedCrumb} · confirm deleting {targets.Count}",
            BackLabel = backLabel,
            Hint = "Enter: delete · Esc: cancel",
        };

        var categoryCount = targets.Select(t => t.CategoryName).Distinct().Count();

        page.Rows.Add(new ReportRow
        {
            Text = $"Yes, delete {targets.Count} type" + (targets.Count == 1 ? "" : "s"),
            Label = $"From {categoryCount} categor" + (categoryCount == 1 ? "y" : "ies")
                + " · a family's last type takes the family with it · Ctrl+Z still undoes this",
            Value = "Enter",
            ActionKey = "unused-delete-confirmed",
            GroupKey = "CONFIRM",
        });

        foreach (var t in targets)
        {
            page.Rows.Add(new ReportRow
            {
                Text = t.DisplayName,
                Value = t.ElementId.ToString(),
                GroupKey = t.CategoryName.ToUpperInvariant(),
            });
        }

        return page;
    }

    /// <summary>Types staged by a confirm page, waiting on the user's Enter.</summary>
    private List<UnusedType> _pendingUnusedTypes = new();

    private void StageUnusedDeletion(List<UnusedType> targets, string backLabel)
    {
        if (targets.Count == 0) return;
        _pendingUnusedTypes = targets;
        App.ViewModel.PushReportPage(BuildUnusedConfirmPage(targets, backLabel));
    }

    /// <summary>Runs the staged deletion and replaces the page with the result.</summary>
    private void RunUnusedDeletion()
    {
        var ids = _pendingUnusedTypes.Select(t => t.ElementId).ToList();
        var attempted = ids.Count;
        if (attempted == 0) return;

        // Captured here, on the UI thread, so the rescan uses the scope the
        // report was opened with (the live selection may have changed since).
        var scope = _unusedScope;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (deleted, failed) = UnusedTypeService.Delete(doc, ids);

            // Re-scan so the numbers shown afterwards are the real ones — a
            // family deleted with its last type takes its siblings along.
            var rescan = UnusedTypeService.Scan(doc, scope);

            Dispatcher.Invoke(() =>
            {
                _pendingUnusedTypes.Clear();
                _unusedSelected.Clear();
                _unusedScan = rescan;

                var page = new ReportPage
                {
                    Breadcrumb = $"{UnusedCrumb} · deleted {deleted} of {attempted}",
                    Hint = "Enter: back to categories · Esc: close",
                };

                page.Rows.Add(new ReportRow
                {
                    Text = $"Deleted {deleted} of {attempted} type" + (attempted == 1 ? "" : "s"),
                    Label = failed.Count == 0
                        ? scope == null
                            ? $"{rescan.Unused.Count} unused type(s) left in the project"
                            : $"{rescan.Unused.Count} unused type(s) left in the selection's families"
                        : $"{failed.Count} could not be deleted",
                    GroupKey = "RESULT",
                });

                page.Rows.Add(new ReportRow
                {
                    Text = "Back to categories",
                    Label = "Re-scanned after the delete",
                    Value = "Enter",
                    ActionKey = "unused-rescan",
                    GroupKey = "RESULT",
                });

                foreach (var f in failed)
                    page.Rows.Add(new ReportRow { Text = f, GroupKey = "FAILED" });

                // Replace rather than push: the pages behind this one describe a
                // model that no longer exists.
                App.ViewModel.ClearReportStack();
                App.ViewModel.ReplaceReportPage(page);
            });
        });
    }

    // ── In-Pane Reports: !dwg and !lp ─────────────────────────────

    /// <summary>Line patterns staged for deletion by the !lp confirm page.</summary>
    private List<(string Name, long Id)> _pendingLinePatterns = new();

    /// <summary>
    /// !dwg — collects every linked and imported CAD in the project and opens
    /// the two-menu report inside the pane.
    /// </summary>
    private void LaunchCadReport()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var report = DwgCleanupService.CollectCad(doc);
            var pages = new CadReportPages(report);
            var root = pages.BuildRoot();

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.EnterReportMode(root, "CAD", pages.Drill);
                UpdateModePillAccent();
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// !lp — lists the "IMPORT-" line patterns and asks for confirmation in the
    /// pane before deleting anything.
    /// </summary>
    private void LaunchLinePatternCleanup()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var patterns = DwgCleanupService.FindImportedLinePatterns(doc);

            Dispatcher.Invoke(() =>
            {
                _pendingLinePatterns = patterns;
                App.ViewModel.EnterReportMode(BuildLinePatternPage(patterns), "PATTERNS");
                UpdateModePillAccent();
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>The !lp confirm page: the delete row on top, the victims below.</summary>
    private static ReportPage BuildLinePatternPage(List<(string Name, long Id)> patterns)
    {
        if (patterns.Count == 0)
        {
            return new ReportPage
            {
                Breadcrumb = "PATTERNS · nothing to clean",
                Hint = "Esc: close",
                Rows =
                {
                    new ReportRow
                    {
                        Text = "No “IMPORT-” line patterns found",
                        Label = "Nothing to clean up",
                        GroupKey = "IMPORTED LINE PATTERNS",
                    }
                }
            };
        }

        var page = new ReportPage
        {
            Breadcrumb = $"PATTERNS · {patterns.Count} “IMPORT-” pattern"
                + (patterns.Count == 1 ? "" : "s"),
            Hint = "Enter on the delete row to confirm · Esc: cancel",
        };

        page.Rows.Add(new ReportRow
        {
            Text = $"Delete {patterns.Count} line pattern" + (patterns.Count == 1 ? "" : "s"),
            Label = "Affected lines fall back to solid · line styles are not touched",
            Value = "Enter",
            ActionKey = "purge-line-patterns",
            GroupKey = "CONFIRM",
        });

        foreach (var (name, id) in patterns)
        {
            page.Rows.Add(new ReportRow
            {
                Text = name,
                Value = id.ToString(),
                GroupKey = "WILL BE DELETED",
            });
        }

        return page;
    }

    /// <summary>Runs an actionable report row. Currently only the !lp delete.</summary>
    private void OnReportActionRequested(ReportRow row)
    {
        switch (row.ActionKey)
        {
            case "unused-select-all":
                ToggleAllUnusedCategories();
                return;

            case "unused-delete-selected":
                StageUnusedDeletion(SelectedUnusedTypes(), "Categories");
                return;

            case "unused-delete-confirmed":
                RunUnusedDeletion();
                return;

            case "unused-rescan":
                if (_unusedScan != null)
                    App.ViewModel.ReplaceReportPage(BuildUnusedCategoryPage(_unusedScan));
                return;
        }

        if (row.ActionKey != "purge-line-patterns") return;

        var ids = _pendingLinePatterns.Select(p => p.Id).ToList();
        var attempted = ids.Count;
        if (attempted == 0) return;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (deleted, failed) = DwgCleanupService.DeleteLinePatterns(doc, ids);

            Dispatcher.Invoke(() =>
            {
                _pendingLinePatterns.Clear();

                var page = new ReportPage
                {
                    Breadcrumb = $"PATTERNS · deleted {deleted} of {attempted}",
                    Hint = "Esc: close",
                };

                page.Rows.Add(new ReportRow
                {
                    Text = $"Deleted {deleted} of {attempted} line pattern"
                        + (attempted == 1 ? "" : "s"),
                    Label = failed.Count == 0
                        ? "Clean — line styles untouched"
                        : $"{failed.Count} could not be deleted",
                    GroupKey = "RESULT",
                });

                foreach (var f in failed)
                {
                    page.Rows.Add(new ReportRow
                    {
                        Text = f,
                        GroupKey = "FAILED",
                    });
                }

                App.ViewModel.ReplaceReportPage(page);
            });
        });
    }

    /// <summary>Selects (and reveals) the element behind a report row in the model.</summary>
    private void OnReportElementActivated(long elementId)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var ids = new List<ElementId> { new ElementId(elementId) };
            uidoc.Selection.SetElementIds(ids);

            // ShowElements switches to a view the element is visible in, which
            // for a view-specific import is the drafting view that hides it.
            try { uidoc.ShowElements(ids); }
            catch { /* Revit already told the user it couldn't find a good view */ }
        });
    }

    /// <summary>Keyboard handling while an in-pane report is open.</summary>
    private void HandleReportKeyDown(KeyEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                // Walk back out the way we came in; leave the mode at the root.
                if (!vm.ReportGoBack()) vm.ExitReportMode();
                return;

            case Key.Space:
                // Only swallow Space on a checkable row, so it still types a
                // space into the filter box everywhere else.
                if (vm.ToggleHighlightedReportRow())
                {
                    e.Handled = true;
                    ScrollHighlightedIntoView();
                }
                return;

            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                vm.SetAllReportChecks(true);
                return;

            case Key.D when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                vm.SetAllReportChecks(false);
                return;

            case Key.Enter:
                e.Handled = true;
                vm.ActivateHighlightedReportRow();
                ScrollHighlightedIntoView();
                return;

            case Key.Right:
                e.Handled = true;
                vm.ActivateHighlightedReportRow();
                ScrollHighlightedIntoView();
                return;

            case Key.Left:
                e.Handled = true;
                vm.ReportGoBack();
                return;

            case Key.Back:
                // Only go back on an empty filter box, so backspace still edits.
                if (string.IsNullOrEmpty(vm.SearchText))
                {
                    e.Handled = true;
                    vm.ReportGoBack();
                }
                return;

            case Key.Up:
                e.Handled = true;
                vm.MoveHighlight(-1);
                ScrollHighlightedIntoView();
                return;

            case Key.Down:
                e.Handled = true;
                vm.MoveHighlight(1);
                ScrollHighlightedIntoView();
                return;
        }

        // Everything else falls through so typing filters the current page.
    }

    // ── Property Inspector ────────────────────────────────────────

    /// <summary>
    /// Reads the current Revit selection and enters inspector mode.
    /// </summary>
    private void LaunchPropertyInspector()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                Dispatcher.Invoke(() =>
                    App.ViewModel.ShowInlineError("Select elements in the model first"));
                return;
            }

            var doc = uidoc.Document;
            var (items, breadcrumb, isMixed) = ParameterCollector.Collect(doc, selIds);

            var elementIdList = selIds.Select(id => id.Value).ToList();

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.InspectorElementIds = elementIdList;
                App.ViewModel.InspectorElementCount = elementIdList.Count;
                App.ViewModel.EnterInspectorMode(items, breadcrumb, isMixed);
                UpdateModePillAccent();
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// Handles all keyboard input while in inspector mode.
    /// </summary>
    private void HandleInspectorKeyDown(KeyEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (vm.IsInspectorEditMode)
                {
                    vm.CancelInspectorEdit();
                }
                else if (vm.InspectorDrilledCategory != null)
                {
                    vm.InspectorGoBack();
                }
                else
                {
                    vm.ExitInspectorMode();
                    UpdateModePillAccent();
                    UpdateResultGrouping();
                }
                return;

            case Key.Enter:
                e.Handled = true;
                if (vm.HighlightedItem == null) return;

                var param = vm.GetInspectorParam(vm.HighlightedItem);
                if (param == null) return;

                // Category row → drill in
                if (param.IsCategoryRow)
                {
                    vm.InspectorDrillIn(param);
                    InspectorDrillIntoCategory(param);
                    return;
                }

                // Divider → no-op
                if (param.IsDivider) return;

                // Read-only → hint
                if (param.IsReadOnly)
                {
                    if (param.StorageType == 4) // ElementId
                        vm.ShowInlineError("ElementId parameters \u2014 editing coming in a future update");
                    else
                        vm.ShowInlineError("Read-only parameter");
                    return;
                }

                // Enter edit mode
                vm.EnterInspectorEdit(param);
                return;

            case Key.Space:
                e.Handled = true;
                if (vm.HighlightedItem == null) return;

                var spaceParam = vm.GetInspectorParam(vm.HighlightedItem);
                if (spaceParam is { IsYesNo: true, IsReadOnly: false })
                {
                    OnInspectorToggleRequested(spaceParam);
                }
                return;

            case Key.Right:
                if (vm.InspectorIsMixedCategory && vm.HighlightedItem != null)
                {
                    e.Handled = true;
                    var rightParam = vm.GetInspectorParam(vm.HighlightedItem);
                    if (rightParam is { IsCategoryRow: true })
                    {
                        vm.InspectorDrillIn(rightParam);
                        InspectorDrillIntoCategory(rightParam);
                    }
                }
                return;

            case Key.Left:
                if (vm.InspectorDrilledCategory != null)
                {
                    e.Handled = true;
                    vm.InspectorGoBack();
                }
                return;

            case Key.Back:
                if (string.IsNullOrEmpty(vm.SearchText))
                {
                    if (vm.InspectorDrilledCategory != null)
                    {
                        e.Handled = true;
                        vm.InspectorGoBack();
                    }
                    // else: don't exit inspector on backspace — use Escape
                }
                return;

            case Key.Up:
                e.Handled = true;
                vm.MoveHighlight(-1);
                // Skip divider rows
                SkipInspectorDividers(-1);
                ScrollHighlightedIntoView();
                return;

            case Key.Down:
                e.Handled = true;
                vm.MoveHighlight(1);
                SkipInspectorDividers(1);
                ScrollHighlightedIntoView();
                return;
        }

        // All other keys: let typing through for search filtering
    }

    /// <summary>
    /// Skips divider rows when navigating with arrow keys.
    /// </summary>
    private void SkipInspectorDividers(int direction)
    {
        var vm = App.ViewModel;
        if (vm.HighlightedItem == null) return;

        var param = vm.GetInspectorParam(vm.HighlightedItem);
        if (param is { IsDivider: true })
        {
            vm.MoveHighlight(direction);
        }
    }

    /// <summary>
    /// Drills into a category from the mixed-category picker.
    /// Calls ParameterCollector on the Revit API thread, then updates ViewModel.
    /// </summary>
    private void InspectorDrillIntoCategory(InspectorParameterItem catItem)
    {
        if (catItem.CategoryName == null || catItem.ElementIds.Count == 0) return;

        var categoryName = catItem.CategoryName;
        var elementIds = catItem.ElementIds.ToList();
        int totalCount = App.ViewModel.InspectorElementCount;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (items, breadcrumb) = ParameterCollector.CollectForCategory(
                doc, categoryName, elementIds, totalCount);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.InspectorShowCategoryParams(items, breadcrumb);
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// Handles keyboard input in the inspector edit TextBox.
    /// </summary>
    private void InspectorEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                vm.ConfirmInspectorEdit();
                break;

            case Key.Escape:
                e.Handled = true;
                vm.CancelInspectorEdit();
                break;

            case Key.Tab:
                e.Handled = true;
                int direction = Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1;
                vm.CommitAndMoveInspectorEdit(direction);
                break;
        }
    }

    /// <summary>
    /// Called when an inspector edit is confirmed — writes the value to Revit.
    /// </summary>
    private void OnInspectorEditConfirmed(InspectorParameterItem paramItem, string newValue)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (success, message) = ParameterWriter.Write(doc, paramItem, newValue);

            Dispatcher.Invoke(() =>
            {
                if (success)
                    App.ViewModel.ShowInlineSuccess(message);
                else
                    App.ViewModel.ShowInlineError(message);
            });

            // Refresh parameters after edit
            if (success)
                RefreshInspectorParams(uiApp);
        });
    }

    /// <summary>
    /// Called when a Yes/No parameter toggle is requested.
    /// </summary>
    private void OnInspectorToggleRequested(InspectorParameterItem paramItem)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (success, message) = ParameterWriter.ToggleYesNo(doc, paramItem);

            Dispatcher.Invoke(() =>
            {
                if (success)
                    App.ViewModel.ShowInlineSuccess(message);
                else
                    App.ViewModel.ShowInlineError(message);
            });

            if (success)
                RefreshInspectorParams(uiApp);
        });
    }

    /// <summary>
    /// Called when inspector needs a full parameter refresh (triggered by ViewModel event).
    /// </summary>
    private void OnInspectorRefreshRequested()
    {
        RevitBackgroundTask.Raise(uiApp => RefreshInspectorParams(uiApp));
    }

    /// <summary>
    /// Re-reads parameters from Revit and updates the inspector display.
    /// Preserves scroll position and highlighted parameter.
    /// Must be called on the Revit API thread.
    /// </summary>
    private void RefreshInspectorParams(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;

        var vm = App.ViewModel;
        var elementIds = vm.InspectorElementIds;
        if (elementIds.Count == 0) return;

        // If drilled into a category, refresh just that category
        if (vm.InspectorDrilledCategory != null)
        {
            // Find element IDs for the drilled category
            var catElementIds = elementIds; // use all — ParameterCollector filters by category
            var (items, breadcrumb) = ParameterCollector.CollectForCategory(
                doc, vm.InspectorDrilledCategory, catElementIds, vm.InspectorElementCount);

            Dispatcher.Invoke(() =>
            {
                vm.RefreshInspectorParams(items);
            });
        }
        else
        {
            var revitIds = elementIds.Select(id => new ElementId(id)).ToList();
            var (items, breadcrumb, isMixed) = ParameterCollector.Collect(doc, revitIds);

            Dispatcher.Invoke(() =>
            {
                if (isMixed)
                {
                    // Don't refresh category list after edit — shouldn't happen
                    // but just in case, re-enter with updated data
                    vm.RefreshInspectorParams(items);
                }
                else
                {
                    vm.RefreshInspectorParams(items);
                }
            });
        }
    }

    // ── Parameter Filter (!pf) ─────────────────────────────────────

    /// <summary>
    /// Builds the parameter-name index on the Revit thread and enters Parameter
    /// Filter mode. Shows a transient "Indexing…" hint so the UI feels responsive.
    /// </summary>
    private void LaunchParameterFilter()
    {
        App.ViewModel.ShowInlineSuccess("Indexing parameters…");

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            // Default scope when entering is EntireProject — index against that.
            var names = ParameterFilterService.CollectParameterNames(
                doc, ParamFilterScope.EntireProject, view);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.ClearInlineError();
                App.ViewModel.EnterParameterFilterMode(names);
                UpdateModePillAccent();
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// All keyboard handling while in Parameter Filter mode.
    /// </summary>
    private void HandleParameterFilterKeyDown(KeyEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                vm.ExitParameterFilterMode();
                UpdateModePillAccent();
                UpdateResultGrouping();
                return;

            case Key.Tab:
                e.Handled = true;
                int direction = Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1;
                // Auto-commit a typed Value before moving away.
                if (vm.ParamFilterFocus == ParamFilterField.Value)
                    vm.ParamFilterCommitTyped();
                vm.ParamFilterMoveFocus(direction);
                return;

            case Key.Up:
                e.Handled = true;
                vm.MoveHighlight(-1);
                ScrollHighlightedIntoView();
                return;

            case Key.Down:
                e.Handled = true;
                vm.MoveHighlight(1);
                ScrollHighlightedIntoView();
                return;

            case Key.Enter:
                e.Handled = true;
                // Value field with rule that takes a value: commit typed text if any,
                // else commit dropdown highlight. Then execute when fields are valid.
                if (vm.ParamFilterFocus == ParamFilterField.Value)
                {
                    if (!string.IsNullOrWhiteSpace(vm.SearchText))
                        vm.ParamFilterCommitTyped();
                    else
                        vm.ParamFilterCommitHighlight();

                    if (vm.ParamFilterCanExecute)
                        vm.RequestParamFilterExecute();
                    return;
                }

                // Non-Value fields: commit highlighted dropdown row, then advance.
                bool committed = vm.ParamFilterCommitHighlight();

                // If we're on Rule and the chosen rule doesn't take a value, the field
                // order skips Value — pressing Enter on Rule should execute immediately
                // when all earlier fields are filled.
                if (committed && vm.ParamFilterFocus == ParamFilterField.Scope
                    && vm.ParamFilterCanExecute)
                {
                    // ParamFilterMoveFocus wrapped from the last field (Rule) back to Scope —
                    // that means the rule is value-less and we're ready to execute.
                    vm.RequestParamFilterExecute();
                }
                return;

            case Key.Back:
                // Backspace on empty text in PF: step back one field. Otherwise let
                // it delete characters normally.
                if (string.IsNullOrEmpty(vm.SearchText))
                {
                    e.Handled = true;
                    vm.ParamFilterMoveFocus(-1);
                }
                return;
        }

        // All other keys (typing): let them through — SearchText binding will refresh
        // the dropdown via RefreshResults → RefreshParameterFilterDisplay.
    }

    /// <summary>
    /// Called when a parameter name is picked. Loads the value-suggestion list
    /// for that parameter on the Revit thread.
    /// </summary>
    private void OnParamFilterParamSelected(string parameterName)
    {
        var scope = App.ViewModel.ParamFilterScopeValue;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            var values = ParameterFilterService.CollectValuesFor(doc, parameterName, scope, view);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.SetParamFilterValueSuggestions(values);
            });
        });
    }

    /// <summary>
    /// Runs the filter on the Revit thread, replaces the active selection with
    /// the matches, shows feedback, and exits PF mode.
    /// </summary>
    private void OnParamFilterExecuteRequested()
    {
        var vm = App.ViewModel;
        if (!vm.ParamFilterCanExecute) return;

        var scope = vm.ParamFilterScopeValue;
        var paramName = vm.ParamFilterParameterName!;
        var rule = vm.ParamFilterRuleValue!.Value;
        var value = vm.ParamFilterValueText;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            var matches = ParameterFilterService.Execute(doc, scope, view, paramName, rule, value);
            int count = matches.Count;

            uidoc.Selection.SetElementIds(matches);

            Dispatcher.Invoke(() =>
            {
                vm.ExitParameterFilterMode();
                UpdateModePillAccent();
                UpdateResultGrouping();

                if (count == 0)
                    vm.ShowInlineError("No elements matched");
                else
                    vm.ShowInlineSuccess($"Selected {count} element{(count == 1 ? "" : "s")}");
            });
        });
    }
}
