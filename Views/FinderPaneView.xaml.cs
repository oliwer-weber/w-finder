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

// This class is split over several files with the `partial` keyword. C# glues
// them back into one class at compile time, so they all share the same fields
// and methods; the split is purely so each topic is easy to find:
//
//   FinderPaneView.xaml.cs               this file: setup, look, mouse, closing
//   FinderPaneView.Keyboard.cs           the one keyboard handler for every screen
//   FinderPaneView.Activation.cs         what Enter / double-click does to a row
//   FinderPaneView.QuickActions.cs       rename, delete, duplicate, export...
//   FinderPaneView.SelectionFilter.cs    !sf
//   FinderPaneView.Inspector.cs          !pi
//   FinderPaneView.ParamFilter.cs        !pf
//   FinderPaneView.Reports.cs            !dwg, !lp and the shared report plumbing
//   FinderPaneView.UnusedTypes.cs        !ut, !us
public partial class FinderPaneView : UserControl
{
    public FinderPaneView()
    {
        InitializeComponent();
        DataContext = App.ViewModel;

        App.ViewModel.ItemSelectionRequested += OnItemSelectionRequested;
        App.ViewModel.FavoritesChanged += OnFavoritesChanged;
        App.ViewModel.QuickActionRequested += OnQuickActionRequested;

        App.ViewModel.FocusSearchRequested += FocusSearchBox;
        App.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        App.ViewModel.FocusRenameRequested += OnFocusRenameRequested;
        App.ViewModel.InlineRenameConfirmed += OnInlineRenameConfirmed;
        App.ViewModel.ExcelExportConfirmed += OnExcelExportConfirmed;
        App.ViewModel.ReportActionRequested += OnReportActionRequested;
        App.ViewModel.ReportElementActivated += OnReportElementActivated;
        App.ViewModel.InspectorEditConfirmed += OnInspectorEditConfirmed;
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

        // Keys pressed while focus is anywhere else in the pane are redirected
        // to the search box (see Pane_PreviewKeyDown).
        PreviewKeyDown += Pane_PreviewKeyDown;

        // Every time the pane appears, the keyboard starts in the search box.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) FocusSearchBox();
        };

        // Compute uniform pill width once from all possible pill texts
        ComputeUniformPillWidth();

        // Bring the look in line with the view model once the control is on
        // screen. Without this, section headers and hints only appeared after
        // the first mode switch.
        Loaded += (_, _) =>
        {
            UpdateModePillAccent();
            UpdateEmptyState();
            UpdateResultGrouping();
        };
    }

    // ── Closing ────────────────────────────────────────────────────

    /// <summary>
    /// Asks Revit to close the pane. Hiding a dockable pane is a Revit API
    /// call, so it is queued onto the Revit thread like every other one; the
    /// actual work is in <see cref="FinderCommand.ClosePane"/>.
    /// </summary>
    private void RequestClose()
    {
        RevitBackgroundTask.Raise(FinderCommand.ClosePane);
    }

    /// <summary>Shows an error toast from the Revit thread.</summary>
    private void ShowError(string message) =>
        Dispatcher.Invoke(() => App.ViewModel.ShowInlineError(message));

    /// <summary>Shows a success toast from the Revit thread.</summary>
    private void ShowSuccess(string message) =>
        Dispatcher.Invoke(() => App.ViewModel.ShowInlineSuccess(message));

    // ── Mode Pill ──────────────────────────────────────────────────

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

    /// <summary>
    /// Intercepts prefix characters (>, :, !) before they appear in the text box.
    /// Consumes the character and activates the corresponding mode pill.
    /// </summary>
    private void SearchBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var vm = App.ViewModel;

        // Prefixes only mean something on a plain, empty Browser search.
        if (vm.Surface != PaneSurface.Search) return;
        if (vm.ActiveMode != ActiveMode.Browser) return;
        if (vm.FamilyNavigationStage != FamilyNavigationStage.FamilyLevel) return;
        if (!string.IsNullOrEmpty(vm.SearchText)) return;

        ActiveMode? mode = e.Text switch
        {
            ">" => ActiveMode.Place,
            ":" => ActiveMode.Command,
            "!" => ActiveMode.Shebang,
            _ => null
        };
        if (mode == null) return;

        e.Handled = true;
        vm.ActivateMode(mode.Value); // the ActiveMode change refreshes pill, empty state and grouping
    }

    /// <summary>
    /// Colours the mode pill for the current screen. Every screen uses the
    /// same recipe: border at 100% accent, background at ~22% accent, tinted text.
    /// </summary>
    private void UpdateModePillAccent()
    {
        var vm = App.ViewModel;

        // Each accent family has a "<Name>AccentColor" and "<Name>PillText" resource.
        string family = vm.Surface switch
        {
            PaneSurface.Report => vm.ReportAccent,   // "Shebang" or "Filter"
            PaneSurface.ParamFilter => "Filter",
            PaneSurface.Inspector => "Inspect",
            _ => vm.ActiveMode switch
            {
                ActiveMode.Place => "Place",
                ActiveMode.Command => "Command",
                ActiveMode.Shebang => "Shebang",
                _ => "Browser"
            }
        };

        var accentColor = (WpfColor)FindResource(family + "AccentColor");

        var accentBrush = new SolidColorBrush(accentColor);
        accentBrush.Freeze();
        ModePillBorder.BorderBrush = accentBrush;
        ModePillBorder.BorderThickness = new Thickness(2, 1, 1, 1); // thicker left edge as identity signal

        var bgColor = WpfColor.FromArgb((byte)(255 * 0.22), accentColor.R, accentColor.G, accentColor.B);
        ModePillBorder.Background = new SolidColorBrush(bgColor);

        ModePillTextBlock.Foreground = (SolidColorBrush)FindResource(family + "PillText");
    }

    // ── Empty State Management ─────────────────────────────────────

    private void UpdateEmptyState()
    {
        var vm = App.ViewModel;
        bool isEmpty = vm.IsEmptyState;

        // Browser hints (Row 2): browser mode with an empty search
        BrowserHints.Visibility = (isEmpty && vm.ActiveMode == ActiveMode.Browser)
            ? WpfVisibility.Visible
            : WpfVisibility.Collapsed;

        PlaceEmptyState.Visibility = WpfVisibility.Collapsed;
        CommandEmptyState.Visibility = WpfVisibility.Collapsed;

        if (isEmpty && vm.ActiveMode == ActiveMode.Place)
        {
            PlaceEmptyState.Visibility = WpfVisibility.Visible;
            PopulateCategoryChips();
        }
        else if (isEmpty && vm.ActiveMode == ActiveMode.Command)
        {
            CommandEmptyState.Visibility = WpfVisibility.Visible;
        }

        bool anyPanel = PlaceEmptyState.Visibility == WpfVisibility.Visible
            || CommandEmptyState.Visibility == WpfVisibility.Visible;
        EmptyStatePanel.Visibility = anyPanel ? WpfVisibility.Visible : WpfVisibility.Collapsed;
    }

    /// <summary>How many category chips are shown before the "+N more" chip.</summary>
    private const int CollapsedChipCount = 6;

    /// <summary>
    /// Populates category filter chips in the Place mode empty state.
    /// </summary>
    private void PopulateCategoryChips()
    {
        CategoryChipsPanel.Children.Clear();
        var vm = App.ViewModel;
        int maxChips = vm.AreCategoryChipsExpanded ? vm.AvailableCategories.Count : CollapsedChipCount;
        int shown = 0;

        foreach (var cat in vm.AvailableCategories)
        {
            if (shown >= maxChips) break;

            var category = cat; // capture for lambda
            CategoryChipsPanel.Children.Add(
                BuildChip(shown, cat, "PrimaryText", () => vm.ApplyCategoryFilter(category)));
            shown++;
        }

        // Show overflow count if needed (and not already expanded)
        int remaining = vm.AvailableCategories.Count - shown;
        if (remaining > 0 && !vm.AreCategoryChipsExpanded)
        {
            CategoryChipsPanel.Children.Add(
                BuildChip(shown, $"+{remaining} more", "MutedText", ExpandChips));
            shown++;
        }

        vm.VisibleChipCount = shown;
    }

    private Border BuildChip(int index, string text, string textBrushKey, Action onClick)
    {
        var chip = new Border
        {
            Tag = index,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 4),
            Background = (Brush)FindResource("ItemHoverBackground"),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = (Brush)FindResource(textBrushKey),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        chip.MouseLeftButtonDown += (_, _) =>
        {
            onClick();
            FocusSearchBox();
        };
        return chip;
    }

    private void ExpandChips()
    {
        var vm = App.ViewModel;
        vm.ExpandCategoryChips();
        PopulateCategoryChips();
        if (vm.IsChipNavigationActive)
            vm.FocusedChipIndex = Math.Min(vm.FocusedChipIndex, vm.VisibleChipCount - 1);
        UpdateChipHighlight();
    }

    /// <summary>Enter on the focused chip: apply its category, or expand the list.</summary>
    private void ActivateFocusedChip()
    {
        var vm = App.ViewModel;
        int chipIdx = vm.FocusedChipIndex;
        int categoryChips = vm.AreCategoryChipsExpanded
            ? vm.AvailableCategories.Count
            : Math.Min(CollapsedChipCount, vm.AvailableCategories.Count);

        if (chipIdx >= 0 && chipIdx < categoryChips)
            vm.ApplyCategoryFilter(vm.AvailableCategories[chipIdx]);
        else
            ExpandChips(); // the "+N more" chip
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
                    // The "+N more" chip has MutedText foreground by default
                    bool isOverflow = !vm.AreCategoryChipsExpanded
                        && idx >= Math.Min(CollapsedChipCount, vm.AvailableCategories.Count);
                    tb.Foreground = isFocused ? Brushes.White : (isOverflow ? mutedTextBrush : defaultTextBrush);
                }
            }
        }
    }

    // ── Result Grouping ────────────────────────────────────────────

    /// <summary>
    /// Turns the section headers in the results list on or off for the
    /// current screen.
    /// </summary>
    private void UpdateResultGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(ResultsList.ItemsSource);
        if (view == null) return;

        var vm = App.ViewModel;

        bool grouped = vm.Surface switch
        {
            PaneSurface.Report => true,
            PaneSurface.ParamFilter => false,                       // flat dropdown
            PaneSurface.Inspector => !vm.InspectorIsMixedCategory,  // parameter groups
            _ => !vm.IsShebangMode                                   // quips list is flat
        };

        view.GroupDescriptions.Clear();
        if (grouped)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BrowserItem.GroupKey)));
    }

    // ── ViewModel Property Changed Handler ─────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.PropertyName)
        {
            // The screen changed (search list <-> a quip), or the prefix mode did:
            // everything that depends on "where are we" is refreshed in one place.
            case nameof(FinderPaneViewModel.Surface):
            case nameof(FinderPaneViewModel.ActiveMode):
            case nameof(FinderPaneViewModel.ReportAccent):
                UpdateModePillAccent();
                UpdateEmptyState();
                UpdateResultGrouping();
                break;

            case nameof(FinderPaneViewModel.IsEmptyState):
                UpdateEmptyState();
                break;

            case nameof(FinderPaneViewModel.SearchText):
                // New text means a new list; the cursor is back to "wherever it landed".
                _cursorMovedSinceTyping = false;
                break;

            case nameof(FinderPaneViewModel.IsActionBarVisible):
                ActionBarPopup.Visibility = vm.IsActionBarVisible
                    ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                break;

            case nameof(FinderPaneViewModel.IsInlineRenameVisible):
                PositionOverlay(InlineRenamePopup);
                // When the rename box goes away, typing must land in the search box again.
                if (!vm.IsInlineRenameVisible) FocusSearchBox();
                break;

            case nameof(FinderPaneViewModel.HasInlineError):
                InlineErrorPopup.Background = vm.IsSuccessMessage
                    ? new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32))
                    : new SolidColorBrush(WpfColor.FromRgb(0xE6, 0x86, 0x0A));
                PositionOverlay(InlineErrorPopup);
                break;

            case nameof(FinderPaneViewModel.FocusedChipIndex):
            case nameof(FinderPaneViewModel.IsChipNavigationActive):
                UpdateChipHighlight();
                break;

            case nameof(FinderPaneViewModel.IsInspectorEditMode):
                if (vm.IsInspectorEditMode)
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
                    FocusSearchBox();
                }
                break;

            case nameof(FinderPaneViewModel.HighlightedItem):
                SyncListSelections();
                SelectHighlightedTypeInRevit();
                break;
        }
    }

    /// <summary>
    /// At Type Level (family drill-down), selects the highlighted type in Revit
    /// so the Properties palette follows the cursor.
    /// </summary>
    private void SelectHighlightedTypeInRevit()
    {
        var vm = App.ViewModel;
        if (vm.Surface != PaneSurface.Search) return;
        if (vm.FamilyNavigationStage != FamilyNavigationStage.TypeLevel) return;
        if (vm.HighlightedItem is not BrowserItem hi) return;
        if (hi.IsBackRow || hi.Kind != BrowserItemKind.FamilyType || hi.ElementId <= 0) return;

        SelectInRevit(hi.ElementId);
    }

    /// <summary>Makes one element the Revit selection. Ignores ids that no longer exist.</summary>
    private static void SelectInRevit(long elementId)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var id = new ElementId(elementId);
            if (uidoc.Document.GetElement(id) == null) return;

            uidoc.Selection.SetElementIds(new List<ElementId> { id });
        });
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

            if (App.ViewModel.ShowFavorites && App.ViewModel.Favorites.Contains(item))
            {
                FavoritesList.SelectedItem = item;
                ResultsList.SelectedItem = null;
            }
            else
            {
                ResultsList.SelectedItem = item;
                FavoritesList.SelectedItem = null;
            }
        }
        finally
        {
            _syncingSelections = false;
        }
    }

    private void ScrollHighlightedIntoView()
    {
        var item = App.ViewModel.HighlightedItem;
        if (item == null) return;

        if (App.ViewModel.ShowFavorites && App.ViewModel.Favorites.Contains(item))
            FavoritesList.ScrollIntoView(item);
        else if (App.ViewModel.Results.Contains(item))
            ResultsList.ScrollIntoView(item);
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
            overlay.Margin = new Thickness(8, 0, 8, 0);
            overlay.Visibility = WpfVisibility.Visible;
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

    // ── Focus ──────────────────────────────────────────────────────

    /// <summary>
    /// Puts the keyboard back in the search box. The search box is the only
    /// place navigation keys are handled, so anything that could steal focus
    /// (a mouse click on a row, a closing rename box) ends by calling this.
    /// </summary>
    private void FocusSearchBox()
    {
        // BeginInvoke = "do this once the current input event has finished",
        // otherwise WPF hands focus straight back to whatever was clicked.
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

    // ── Mouse ──────────────────────────────────────────────────────

    private bool _syncingSelections;

    /// <summary>
    /// A row was clicked. Move the cursor there, and keep the keyboard in the
    /// search box so arrows / Enter / Esc carry on working after a click.
    /// </summary>
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelections) return;
        if (sender is ListBox listBox && listBox.SelectedItem is BrowserItem item)
        {
            // The list's selection also follows the keyboard cursor (it is bound
            // to HighlightedItem). When they already agree, this event is just
            // that echo, not a click: selecting in Revit here would wipe the
            // user's model selection every time the results refresh.
            if (ReferenceEquals(item, App.ViewModel.HighlightedItem)) return;

            App.ViewModel.HighlightedItem = item;
            App.ViewModel.SelectedItem = item;
            _cursorMovedSinceTyping = true;
            FocusSearchBox();
        }
    }

    /// <summary>Double-click is Enter on the clicked row, on every screen.</summary>
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking the scrollbar or the star button is not "open this row".
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ListBoxItem>(source) == null) return;
        if (FindAncestor<Button>(source) != null) return;

        // The action bar belongs to another row by now; Enter would run its pill.
        if (App.ViewModel.IsActionBarVisible)
            App.ViewModel.CloseActionBar();

        ActivateHighlighted();
        FocusSearchBox();
    }

    /// <summary>Walks up the visual tree to the nearest parent of type T.</summary>
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            // Text runs are not visuals, so fall back to the logical tree for them.
            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BrowserItem item)
        {
            App.ViewModel.ToggleFavorite(item);
            FocusSearchBox();
        }
    }

    /// <summary>Clicking an action-bar pill runs it, same as Enter on it.</summary>
    private void ActionPill_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuickAction action })
        {
            App.ViewModel.FocusedAction = action;
            App.ViewModel.ExecuteAction();
            e.Handled = true;
        }
    }

    /// <summary>
    /// A single click on a real project item selects it in Revit. Quips,
    /// commands and the rows of an open quip have made-up ids, so they are
    /// skipped (Revit treats negative ids as built-in categories).
    /// </summary>
    private void OnItemSelectionRequested(BrowserItem item)
    {
        if (App.ViewModel.Surface != PaneSurface.Search) return;
        if (item.ElementId <= 0 || item.IsBackRow) return;
        if (item.Kind is BrowserItemKind.Command or BrowserItemKind.Shebang) return;

        SelectInRevit(item.ElementId);
    }

    private void OnFavoritesChanged()
    {
        FavoritesStore.Save(App.ViewModel.GetFavoriteIds());
    }
}
