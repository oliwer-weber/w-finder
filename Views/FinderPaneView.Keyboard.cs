using System.Windows.Input;
using w_finder.Models;
using w_finder.ViewModels;

namespace w_finder.Views;

// The one keyboard handler for the whole pane.
//
// Every screen (search list, family drill-down, and each quip) answers the
// same keys the same way:
//
//   Up / Down        move the cursor
//   Enter            the row's main action (open, run, drill in, edit, pick)
//   Ctrl+Enter       finish a multi-step quip (apply filter, delete ticked, run)
//   Right            drill in. Never runs anything.
//   Left             back one level (only when the caret is at the start)
//   Backspace        on an empty box: back one level, or leave a prefix mode
//   Space            tick, on rows that have a tick box. Types a space otherwise.
//   Tab              action bar (search) / next field (parameter filter)
//   Esc              step back one layer; closes the pane when nothing is left
//
// Each key is handled once here and then asks the current screen what it
// means, instead of every screen having its own copy of the key handling.
public partial class FinderPaneView
{
    /// <summary>
    /// True once the user has deliberately put the cursor on a row (arrow keys
    /// or a click) since they last typed. Lets Enter tell "I picked this row"
    /// apart from "the first row just happens to be highlighted".
    /// </summary>
    private bool _cursorMovedSinceTyping;

    /// <summary>
    /// Safety net for the whole pane. All navigation lives on the search box,
    /// so if keyboard focus ever ends up somewhere else in the pane (a click
    /// on the list background, a section header, the hint bar...) the keys
    /// would silently stop working. Instead: pull focus back to the search
    /// box and handle the key as if it had been there all along.
    /// </summary>
    private void Pane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The search box, rename boxes and inspector edit box handle their own keys.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;

        SearchBox_PreviewKeyDown(SearchBox, e);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = App.ViewModel;
        var mods = Keyboard.Modifiers;
        bool boxEmpty = string.IsNullOrEmpty(vm.SearchText);

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (vm.StepBack()) UpdateResultGrouping(); // a level up may group differently
                else RequestClose();
                return;

            case Key.Enter:
                e.Handled = true;
                if (mods == ModifierKeys.Control) FinishQuip();
                else ActivateHighlighted();
                return;

            case Key.Down:
                e.Handled = true;
                MoveCursor(+1);
                return;

            case Key.Up:
                e.Handled = true;
                MoveCursor(-1);
                return;

            case Key.Right:
                if (HandleHorizontal(+1)) e.Handled = true;
                return;

            case Key.Left:
                if (HandleHorizontal(-1)) e.Handled = true;
                return;

            // IsRepeat: holding Backspace to clear the box must stop at "empty"
            // and not carry on backing out of the screen as well.
            case Key.Back when boxEmpty && !e.IsRepeat:
                if (HandleBackspaceOnEmpty()) e.Handled = true;
                return;

            case Key.Space when mods == ModifierKeys.None:
                if (ToggleHighlighted()) e.Handled = true;
                return;

            case Key.Tab:
                // Always swallowed: an unhandled Tab moves keyboard focus out
                // of the search box, and then no key works any more.
                e.Handled = true;
                HandleTab(mods == ModifierKeys.Shift ? -1 : +1);
                return;

            case Key.A when mods == ModifierKeys.Control
                         && vm.Surface == PaneSurface.Report && vm.ReportHasCheckableRows:
                e.Handled = true;
                vm.SetAllReportChecks(true);
                return;

            case Key.D when mods == ModifierKeys.Control
                         && vm.Surface == PaneSurface.Report && vm.ReportHasCheckableRows:
                e.Handled = true;
                vm.SetAllReportChecks(false);
                return;

            case Key.F when mods == ModifierKeys.Control && vm.Surface == PaneSurface.Search:
                e.Handled = true;
                if (vm.HighlightedItem is BrowserItem favItem && CanBeFavorite(favItem))
                    vm.ToggleFavorite(favItem);
                return;
        }

        // Action bar hotkeys (F2, Del, D, Shift+D...). Matched against the
        // shortcut text each pill shows, so the bar and the keys can't disagree.
        if (vm.IsActionBarVisible && TryRunActionShortcut(e.Key, mods))
            e.Handled = true;

        // Everything else is typing, and falls through to the text box.
    }

    private static bool CanBeFavorite(BrowserItem item) =>
        !item.IsBackRow && item.TypeCount == 0 && item.ElementId != 0;

    // ── Up / Down ──────────────────────────────────────────────────

    private void MoveCursor(int direction)
    {
        var vm = App.ViewModel;

        // The action bar belongs to the row it was opened on; moving on closes it.
        if (vm.IsActionBarVisible) vm.CloseActionBar();

        if (vm.Surface == PaneSurface.Search && MoveCursorThroughChips(direction))
            return;

        vm.MoveHighlight(direction);
        _cursorMovedSinceTyping = true;

        if (vm.Surface == PaneSurface.Inspector)
            SkipInspectorDividers(direction);

        ScrollHighlightedIntoView();
    }

    /// <summary>
    /// The category chips in the Family empty state sit between the search box
    /// and the list, so Up/Down pass through them. Returns true if the key was
    /// used up by entering or leaving the chip row.
    /// </summary>
    private bool MoveCursorThroughChips(int direction)
    {
        var vm = App.ViewModel;

        if (vm.IsChipNavigationActive)
        {
            vm.ExitChipNavigation();
            // Down continues into the list; Up just returns to the search box.
            return direction < 0;
        }

        bool chipsOnScreen = vm.IsEmptyState
            && vm.ActiveMode == ActiveMode.Place
            && vm.VisibleChipCount > 0;
        if (!chipsOnScreen) return false;

        if (direction > 0 && vm.HighlightedItem == null)
        {
            vm.EnterChipNavigation();
            return true;
        }

        bool onFirstRow = vm.Results.Count > 0 && vm.HighlightedItem == vm.Results[0];
        if (direction < 0 && onFirstRow)
        {
            vm.EnterChipNavigation();
            vm.FocusedChipIndex = vm.VisibleChipCount - 1;
            return true;
        }

        return false;
    }

    // ── Left / Right ───────────────────────────────────────────────

    /// <summary>
    /// Right = drill in, Left = back one level. Only when the caret is at that
    /// end of the text, so the arrows still move the caret while editing.
    /// </summary>
    private bool HandleHorizontal(int direction)
    {
        var vm = App.ViewModel;

        // Pill rows take the arrows outright.
        if (vm.IsChipNavigationActive)
        {
            vm.MoveChipFocus(direction);
            return true;
        }
        if (vm.IsActionBarVisible)
        {
            vm.MoveActionFocus(direction);
            return true;
        }

        bool caretAtEdge = direction > 0
            ? SearchBox.CaretIndex >= SearchBox.Text.Length
            : SearchBox.CaretIndex == 0;
        if (!caretAtEdge || SearchBox.SelectionLength > 0) return false;

        bool moved = direction > 0 ? DrillIn() : vm.GoBackLevel();
        if (moved)
        {
            if (vm.Surface == PaneSurface.Inspector) UpdateResultGrouping();
            ScrollHighlightedIntoView();
        }
        return moved;
    }

    /// <summary>Right arrow. Opens the highlighted row's children, if it has any.</summary>
    private bool DrillIn()
    {
        var vm = App.ViewModel;

        switch (vm.Surface)
        {
            case PaneSurface.Report:
                return vm.DrillHighlightedReportRow();

            case PaneSurface.Inspector:
                return InspectorDrillHighlighted();

            case PaneSurface.ParamFilter:
                return false;
        }

        if (IsFamilyRollup(vm.HighlightedItem))
        {
            vm.DrillIntoFamily(vm.HighlightedItem!);
            return true;
        }
        return false;
    }

    /// <summary>A "Family (N types)" row that opens into its types.</summary>
    private static bool IsFamilyRollup(BrowserItem? item) =>
        item != null
        && item.TypeCount > 0
        && App.ViewModel.FamilyNavigationStage == FamilyNavigationStage.FamilyLevel;

    // ── Backspace ──────────────────────────────────────────────────

    private bool HandleBackspaceOnEmpty()
    {
        var vm = App.ViewModel;

        if (vm.IsActionBarVisible)
        {
            vm.CloseActionBar();
            return true;
        }

        if (vm.GoBackLevel())
        {
            if (vm.Surface == PaneSurface.Inspector) UpdateResultGrouping();
            return true;
        }

        // Top level of a prefix mode: Backspace eats the pill, like deleting the prefix.
        if (vm.Surface == PaneSurface.Search && vm.ActiveMode != ActiveMode.Browser)
        {
            vm.DeactivateMode();
            return true;
        }

        return false;
    }

    // ── Space ──────────────────────────────────────────────────────

    /// <summary>Ticks the highlighted row. False when it has no tick box.</summary>
    private bool ToggleHighlighted()
    {
        var vm = App.ViewModel;

        switch (vm.Surface)
        {
            case PaneSurface.Report:
                if (!vm.ToggleHighlightedReportRow()) return false;
                ScrollHighlightedIntoView();
                return true;

            case PaneSurface.Inspector:
                return InspectorToggleHighlighted();

            default:
                return false;
        }
    }

    // ── Tab ────────────────────────────────────────────────────────

    private void HandleTab(int direction)
    {
        var vm = App.ViewModel;

        switch (vm.Surface)
        {
            case PaneSurface.ParamFilter:
                ParamFilterTab(direction);
                return;

            case PaneSurface.Report:
            case PaneSurface.Inspector:
                return; // nothing to tab between

            case PaneSurface.Search:
                if (vm.IsActionBarVisible)
                {
                    vm.CycleActionFocus(direction);
                }
                else if (IsFamilyRollup(vm.HighlightedItem))
                {
                    vm.DrillIntoFamily(vm.HighlightedItem!);
                }
                else if (vm.HighlightedItem is BrowserItem item && !item.IsBackRow)
                {
                    vm.OpenActionBar(item);
                }
                return;
        }
    }

    // ── Action bar hotkeys ─────────────────────────────────────────

    /// <summary>
    /// Runs the action whose pill shows this key combination, e.g. "Shift+D".
    /// </summary>
    private bool TryRunActionShortcut(Key key, ModifierKeys mods)
    {
        string? keyName = key switch
        {
            Key.F2 => "F2",
            Key.Delete => "Del",
            >= Key.A and <= Key.Z => key.ToString(),
            _ => null
        };
        if (keyName == null) return false;

        string combo = mods switch
        {
            ModifierKeys.None => keyName,
            ModifierKeys.Shift => "Shift+" + keyName,
            ModifierKeys.Control => "Ctrl+" + keyName,
            _ => ""
        };

        var action = App.ViewModel.ActiveActions
            .FirstOrDefault(a => string.Equals(a.Shortcut, combo, StringComparison.OrdinalIgnoreCase));
        if (action == null) return false;

        App.ViewModel.FocusedAction = action;
        App.ViewModel.ExecuteAction();
        return true;
    }

    // ── Rename box ─────────────────────────────────────────────────

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
            App.ViewModel.CloseInlineRename(); // focus returns to the search box via IsInlineRenameVisible
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true; // keep focus inside the rename box(es)
            if (!App.ViewModel.IsSheetRename) return;

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
}
