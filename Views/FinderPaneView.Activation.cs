using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;
using w_finder.ViewModels;

namespace w_finder.Views;

// "Activation" = what happens when a row is chosen, by Enter or by
// double-click. Both go through ActivateHighlighted, so the keyboard and the
// mouse can never drift apart again.
//
// The closing rule lives here too:
//   - anything that hands control back to Revit (open a view, place a family,
//     run a command, run a one-shot quip) closes the pane when it succeeds;
//   - anything that fails keeps the pane open and says why.
public partial class FinderPaneView
{
    /// <summary>Enter / double-click on the highlighted row, on any screen.</summary>
    private void ActivateHighlighted()
    {
        var vm = App.ViewModel;

        switch (vm.Surface)
        {
            case PaneSurface.Report:
                vm.ActivateHighlightedReportRow();
                ScrollHighlightedIntoView();
                return;

            case PaneSurface.Inspector:
                InspectorActivateHighlighted();
                return;

            case PaneSurface.ParamFilter:
                ParamFilterActivateHighlighted();
                return;
        }

        // Search surface. Pill rows first: they own Enter while they have focus.
        if (vm.IsActionBarVisible)
        {
            vm.ExecuteAction();
            return;
        }
        if (vm.IsChipNavigationActive)
        {
            ActivateFocusedChip();
            return;
        }

        var item = vm.HighlightedItem;
        if (item == null) return;

        if (IsFamilyRollup(item))
        {
            vm.DrillIntoFamily(item);
            return;
        }
        if (item.IsBackRow)
        {
            vm.NavigateBackToFamilies();
            return;
        }

        Activate(item);
    }

    /// <summary>Ctrl+Enter: finish the quip that is open, from any row.</summary>
    private void FinishQuip()
    {
        var vm = App.ViewModel;

        switch (vm.Surface)
        {
            case PaneSurface.Report:
                // Pages with nothing to finish treat Ctrl+Enter as plain Enter.
                if (!vm.ActivateReportPrimaryAction()) ActivateHighlighted();
                return;

            case PaneSurface.ParamFilter:
                ParamFilterRun();
                return;

            default:
                ActivateHighlighted();
                return;
        }
    }

    /// <summary>Runs a search-list item according to what kind of thing it is.</summary>
    private void Activate(BrowserItem item)
    {
        UsageStore.RecordUsage(item);

        switch (item.Kind)
        {
            case BrowserItemKind.Shebang:
                LaunchQuip(item.CommandName);
                break;

            case BrowserItemKind.Command:
                RunCommand(item);
                break;

            case BrowserItemKind.FamilyType:
                PlaceFamilyType(item);
                break;

            default:
                OpenOrSelect(item);
                break;
        }
    }

    // ── Quips ──────────────────────────────────────────────────────

    /// <summary>
    /// Starts a quip. Interactive quips take over the pane and stay open;
    /// one-shot quips do their job and close it.
    /// </summary>
    private void LaunchQuip(string? quipId)
    {
        switch (quipId)
        {
            case null: return;
            case "sf": LaunchSelectionFilter(); return;
            case "pi": LaunchPropertyInspector(); return;
            case "pf": LaunchParameterFilter(); return;
            case "dwg": LaunchCadReport(); return;
            case "lp": LaunchLinePatternCleanup(); return;
            case "ut": LaunchUnusedTypes(); return;
            case "us": LaunchUnusedTypesOfSelection(); return;
        }

        RevitBackgroundTask.Raise(uiApp =>
        {
            var (ok, message) = ShebangService.Execute(quipId, uiApp);

            if (ok) FinderCommand.ClosePane(uiApp);
            else ShowError(message);
        });
    }

    // ── Views and other project items ──────────────────────────────

    /// <summary>
    /// Views, sheets and schedules open. Things that can't be opened (groups,
    /// links, assemblies) are selected in the model instead. Either way the
    /// pane closes.
    /// </summary>
    private void OpenOrSelect(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var element = uidoc.Document.GetElement(new ElementId(item.ElementId));
            if (element == null)
            {
                ShowError("That item no longer exists in the project");
                return;
            }

            if (element is View view)
                uidoc.RequestViewChange(view);
            else
                uidoc.Selection.SetElementIds(new List<ElementId> { element.Id });

            FinderCommand.ClosePane(uiApp);
        });
    }

    // ── Family placement ───────────────────────────────────────────

    private void PlaceFamilyType(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var doc = uidoc.Document;
            if (doc.GetElement(new ElementId(item.ElementId)) is not ElementType type)
            {
                ShowError("That type no longer exists in the project");
                return;
            }

            // A family type that has never been used is "inactive" and must be
            // activated (inside a transaction) before it can be placed.
            if (type is FamilySymbol { IsActive: false } inactive)
            {
                using var tx = new Transaction(doc, "Quip: Activate Family Type");
                tx.Start();
                inactive.Activate();
                tx.Commit();
            }

            FinderCommand.ClosePane(uiApp);

            try
            {
                // Queues Revit's own placement tool and returns immediately,
                // so Quip (and its hotkey) stays responsive while you place.
                uidoc.PostRequestForElementTypePlacement(type);
            }
            catch (Exception postEx)
            {
                QuipLog.WriteException("PostRequestForElementTypePlacement", postEx);

                // Older, blocking way of placing. Kept as a fallback only.
                if (type is not FamilySymbol symbol)
                {
                    ReopenWithError(uiApp, $"Can't place this type: {postEx.Message}");
                    return;
                }

                try
                {
                    uidoc.PromptForFamilyInstancePlacement(symbol);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // The user pressed Esc to finish placing. Not an error.
                }
                catch (Exception promptEx)
                {
                    ReopenWithError(uiApp, $"Can't place this type: {promptEx.Message}");
                }
            }
        });
    }

    /// <summary>
    /// For the cases where the pane had to be closed before we could know the
    /// action failed: bring it back so the error is actually seen.
    /// </summary>
    private void ReopenWithError(UIApplication uiApp, string message)
    {
        var pane = uiApp.GetDockablePane(App.PaneId);
        if (!pane.IsShown()) pane.Show();

        ShowError(message);
        FocusSearchBox();
    }

    // ── Revit commands ─────────────────────────────────────────────

    private void RunCommand(BrowserItem item)
    {
        var cmdName = item.CommandName;
        var revitCmdId = item.RevitCommandId;

        if (cmdName == "__quip_settings")
        {
            RevitBackgroundTask.Raise(uiApp =>
            {
                FinderCommand.ClosePane(uiApp);

                var window = new SettingsWindow();
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiApp.MainWindowHandle;
                window.ShowDialog();
            });
            return;
        }

        RevitBackgroundTask.Raise(uiApp =>
        {
            // Commands in Revit's PostableCommand list can simply be posted.
            if (cmdName != null && Enum.TryParse<PostableCommand>(cmdName, out var postable))
            {
                bool posted = TryPostCommand(uiApp, "PostableCommand", cmdName,
                    () => RevitCommandId.LookupPostableCommandId(postable), null, out var error);

                if (posted) FinderCommand.ClosePane(uiApp);
                else ShowError(error);
                return;
            }

            if (revitCmdId == null)
            {
                ShowError("This command can't be run from Quip");
                return;
            }

            RunInternalCommand(uiApp, revitCmdId);
        });
    }

    /// <summary>
    /// Internal commands (ID_...) are not postable, so three routes are tried
    /// in turn: the command's keyboard shortcut, its ribbon button, and
    /// finally PostCommand for add-in commands.
    /// </summary>
    private void RunInternalCommand(UIApplication uiApp, string revitCmdId)
    {
        bool closedForShortcut = false;

        // 1) Shortcut. If Revit has keys assigned, sending them goes through
        //    Revit's own dispatcher, identical to a real keypress. The pane
        //    has to be out of the way first so the keys reach Revit.
        var shortcut = KeyboardShortcutService.GetShortcutByCommandId(revitCmdId);
        if (!string.IsNullOrEmpty(shortcut))
        {
            FinderCommand.ClosePane(uiApp);
            closedForShortcut = true;

            if (KeystrokeSimulator.TrySend(shortcut, uiApp.MainWindowHandle, out var keysDiag))
            {
                CommandDiagnostics.LogAttempt("Internal", revitCmdId, null, false, uiApp, null, $"keys={keysDiag}");
                return;
            }
        }

        // 2) Ribbon. Invokes the live ribbon button's handler directly.
        if (RibbonCommandInvoker.TryExecute(revitCmdId, out var ribbonDiag))
        {
            CommandDiagnostics.LogAttempt("Internal", revitCmdId, null, false, uiApp, null, ribbonDiag);
            FinderCommand.ClosePane(uiApp);
            return;
        }

        // 3) PostCommand. Catches add-in commands (CustomCtrl_...) that Revit
        //    accepts but that aren't in the live ribbon tree.
        bool posted = TryPostCommand(uiApp, "Internal", revitCmdId,
            () => RevitCommandId.LookupCommandId(revitCmdId), ribbonDiag, out var error);

        if (posted)
        {
            FinderCommand.ClosePane(uiApp);
            return;
        }

        // Nothing worked. Make sure the user sees that, even if step 1 hid the pane.
        if (closedForShortcut) ReopenWithError(uiApp, error);
        else ShowError(error);
    }

    /// <summary>
    /// Looks a command up and posts it. Returns false with a readable reason
    /// when Revit won't take it. Every attempt is written to the diagnostics log.
    /// </summary>
    private static bool TryPostCommand(UIApplication uiApp, string source, string requestedId,
        Func<RevitCommandId?> lookup, string? extraDiagnostics, out string error)
    {
        RevitCommandId? cmdId = null;
        bool canPost = false;
        bool posted = false;
        Exception? failure = null;

        try
        {
            cmdId = lookup();
            canPost = cmdId != null && uiApp.CanPostCommand(cmdId);
            if (canPost)
            {
                uiApp.PostCommand(cmdId);
                posted = true;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        CommandDiagnostics.LogAttempt(source, requestedId, cmdId, canPost, uiApp, failure, extraDiagnostics);

        error = posted
            ? string.Empty
            : failure != null
                ? $"Command failed: {failure.Message}"
                : "Command not available in the current context";
        return posted;
    }
}
