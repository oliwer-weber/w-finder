using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.Views;

// Quick actions: the pills in the action bar (Tab on a row).
//
// These are "tidy up the project" actions you often do several of in a row,
// so they keep the pane open and confirm with a toast. The exceptions are the
// ones that hand over to Revit (Edit Family, Set Shortcut), which close it.
public partial class FinderPaneView
{
    private void OnQuickActionRequested(BrowserItem item, QuickAction action)
    {
        switch (action.Kind)
        {
            case QuickActionKind.Rename:
                App.ViewModel.OpenInlineRename(item);
                break;

            case QuickActionKind.Delete:
                DeleteItem(item);
                break;

            case QuickActionKind.Duplicate:
                DuplicateItem(item, ViewDuplicateOption.Duplicate);
                break;

            case QuickActionKind.DuplicateWithDetailing:
                DuplicateItem(item, ViewDuplicateOption.WithDetailing);
                break;

            case QuickActionKind.DuplicateDependent:
                DuplicateItem(item, ViewDuplicateOption.AsDependent);
                break;

            case QuickActionKind.ExcelExport:
                App.ViewModel.OpenExcelExportInput(item);
                break;

            case QuickActionKind.AssignShortcut:
                OpenRevitShortcutDialogWithSearch(item);
                break;

            case QuickActionKind.RemoveShortcut:
                RemoveShortcut(item);
                break;

            case QuickActionKind.EditFamily:
                EditFamily(item);
                break;
        }
    }

    /// <summary>
    /// Re-reads the project items after something was renamed, deleted or
    /// added, so the list matches the model. Must run on the Revit thread.
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
            FocusSearchBox();
        });
    }

    // ── Rename ─────────────────────────────────────────────────────

    private void OnInlineRenameConfirmed(BrowserItem item, string newName, string? sheetNumber)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return;

            var element = doc.GetElement(new ElementId(item.ElementId));
            if (element == null)
            {
                ShowError("That item no longer exists in the project");
                return;
            }

            try
            {
                if (sheetNumber != null && element is ViewSheet sheet)
                {
                    var trimmedNumber = sheetNumber.Trim();
                    var trimmedName = newName.Trim();
                    bool numberChanges = !string.IsNullOrEmpty(trimmedNumber) && trimmedNumber != sheet.SheetNumber;
                    bool nameChanges = !string.IsNullOrEmpty(trimmedName) && trimmedName != sheet.Name;

                    if (numberChanges)
                    {
                        bool taken = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Any(s => s.Id != sheet.Id && s.SheetNumber == trimmedNumber);

                        if (taken)
                        {
                            ShowError($"Sheet number \"{trimmedNumber}\" already exists");
                            return;
                        }
                    }

                    if (numberChanges || nameChanges)
                    {
                        using var tx = new Transaction(doc, "Quip: Rename Sheet");
                        tx.Start();
                        if (numberChanges) sheet.SheetNumber = trimmedNumber;
                        if (nameChanges) sheet.Name = trimmedName;
                        tx.Commit();
                    }
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
            }
            catch (Exception ex)
            {
                // Typically a duplicate name or characters Revit doesn't allow.
                ShowError($"Rename failed: {ex.Message}");
                return;
            }

            uidoc?.RefreshActiveView();
            RefreshAfterMutation(uiApp);
        });
    }

    // ── Delete ─────────────────────────────────────────────────────

    private void DeleteItem(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null) return;

            var elementId = new ElementId(item.ElementId);
            var element = doc.GetElement(elementId);
            if (element == null)
            {
                ShowError("That item no longer exists in the project");
                return;
            }

            // Revit refuses to delete the view you are looking at, so close it
            // first (which needs another view to fall back to).
            if (element is View && uidoc.ActiveView.Id == elementId)
            {
                var openViews = uidoc.GetOpenUIViews();
                if (openViews.Count <= 1)
                {
                    ShowError("Can't delete the only open view");
                    return;
                }

                openViews.FirstOrDefault(v => v.ViewId == elementId)?.Close();
            }

            try
            {
                using var tx = new Transaction(doc, "Quip: Delete");
                tx.Start();
                doc.Delete(elementId);
                tx.Commit();
            }
            catch (Exception ex)
            {
                ShowError($"Delete failed: {ex.Message}");
                return;
            }

            RefreshAfterMutation(uiApp);
            ShowSuccess($"Deleted \"{item.Name}\" (Ctrl+Z in Revit undoes it)");
        });
    }

    // ── Duplicate ──────────────────────────────────────────────────

    private void DuplicateItem(BrowserItem item, ViewDuplicateOption option)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var element = doc.GetElement(new ElementId(item.ElementId));

            try
            {
                if (element is ViewSheet sheet)
                {
                    // Sheets have their own duplicate call and options.
                    var sheetOption = option == ViewDuplicateOption.WithDetailing
                        ? SheetDuplicateOption.DuplicateSheetWithDetailing
                        : SheetDuplicateOption.DuplicateEmptySheet;

                    if (!sheet.CanBeDuplicated(sheetOption))
                    {
                        ShowError("Revit can't duplicate this sheet that way");
                        return;
                    }

                    using var tx = new Transaction(doc, "Quip: Duplicate Sheet");
                    tx.Start();
                    sheet.Duplicate(sheetOption);
                    tx.Commit();
                }
                else if (element is View view)
                {
                    if (!view.CanViewBeDuplicated(option))
                    {
                        ShowError("Revit can't duplicate this view that way");
                        return;
                    }

                    using var tx = new Transaction(doc, "Quip: Duplicate");
                    tx.Start();
                    view.Duplicate(option);
                    tx.Commit();
                }
                else
                {
                    ShowError("Only views, sheets and schedules can be duplicated");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Duplicate failed: {ex.Message}");
                return;
            }

            RefreshAfterMutation(uiApp);
            ShowSuccess($"Duplicated \"{item.Name}\"");
        });
    }

    // ── Edit family ────────────────────────────────────────────────

    private void EditFamily(BrowserItem item)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            try
            {
                var uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null)
                {
                    ShowError("Open a project first");
                    return;
                }

                var doc = uidoc.Document;
                var element = doc.GetElement(new ElementId(item.ElementId));
                if (element == null)
                {
                    ShowError("That family no longer exists in the project");
                    return;
                }

                Family? family = element switch
                {
                    FamilySymbol sym => sym.Family,
                    Family fam => fam,
                    FamilyInstance inst => inst.Symbol?.Family,
                    _ => null
                };

                if (family == null)
                {
                    ShowError($"Not a family element ({element.GetType().Name})");
                    return;
                }

                if (!family.IsEditable)
                {
                    ShowError("Family is not editable (in-place or system family)");
                    return;
                }

                // The API can't switch the UI to a family document it opened
                // in memory, so: save it to a temp .rfa, then open that file
                // the way File > Open would.
                var familyDoc = doc.EditFamily(family);
                if (familyDoc == null)
                {
                    ShowError("Revit could not open the family for editing");
                    return;
                }

                // Family names may contain characters Windows doesn't allow in file names.
                var safeName = string.Join("_", family.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), safeName + ".rfa");

                familyDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                familyDoc.Close(false);

                FinderCommand.ClosePane(uiApp);
                uiApp.OpenAndActivateDocument(tempPath);
            }
            catch (Exception ex)
            {
                ReopenWithError(uiApp, $"Edit failed: {ex.Message}");
            }
        });
    }

    // ── Keyboard shortcuts for commands ────────────────────────────

    /// <summary>
    /// Opens Revit's native Keyboard Shortcuts dialog and types the command name
    /// into its search box, so the user only has to assign keys and click OK.
    /// Revit's own OK handler does the hot-reload, so the new shortcut works
    /// immediately.
    /// </summary>
    private void OpenRevitShortcutDialogWithSearch(BrowserItem item)
    {
        var searchTerm = item.Name;

        RevitBackgroundTask.Raise(uiApp =>
        {
            bool posted = TryPostCommand(uiApp, "PostableCommand", nameof(PostableCommand.KeyboardShortcuts),
                () => RevitCommandId.LookupPostableCommandId(PostableCommand.KeyboardShortcuts), null, out var error);

            if (!posted)
            {
                ShowError(error);
                return;
            }

            FinderCommand.ClosePane(uiApp);

            // PostCommand only queues: the dialog opens after this method
            // returns. A background task waits for it to appear and focus its
            // search box, then types the search term.
            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(300);
                KeystrokeSimulator.SendText(searchTerm);
            });
        });
    }

    private void RemoveShortcut(BrowserItem item)
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

        if (KeyboardShortcutService.RemoveShortcut(commandId))
        {
            item.ShortcutKeys = "-";
            CommandCollector.Invalidate();
            App.ViewModel.ShowInlineSuccess("Shortcut removed (takes effect next Revit session)");
        }
        else
        {
            App.ViewModel.ShowInlineError("Failed to remove shortcut");
        }
    }

    // ── Schedule export ────────────────────────────────────────────

    private void OnExcelExportConfirmed(BrowserItem item, string filename)
    {
        var sanitized = string.Join("_", filename.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Schedule";
        if (!sanitized.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            sanitized += ".xlsx";

        var fullPath = System.IO.Path.Combine(SettingsService.Current.DefaultExportPath, sanitized);

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            if (doc.GetElement(new ElementId(item.ElementId)) is not ViewSchedule schedule)
            {
                ShowError("Element is not a schedule");
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
                            worksheet.Cell(excelRow, c + 1).Value = schedule.GetCellText(SectionType.Body, r, c);
                        excelRow++;
                    }
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(fullPath);

                ShowSuccess($"Exported to {fullPath}");
            }
            catch (Exception ex)
            {
                ShowError($"Export failed: {ex.Message}");
            }
        });
    }
}
