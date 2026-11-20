using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Turns a <see cref="CadReport"/> into the pages the pane shows for !dwg:
/// root (two menus) → list of DWGs → per-DWG detail.
/// Plain data only, so the ViewModel never sees the Revit API.
/// </summary>
public class CadReportPages
{
    private readonly CadReport _report;

    public CadReportPages(CadReport report) => _report = report;

    public ReportPage BuildRoot()
    {
        var page = new ReportPage
        {
            Breadcrumb = "CAD · "
                + $"{_report.Linked.Count} linked, {_report.Imported.Count} imported",
            Hint = "Enter: open · Esc: close · ↑↓: navigate",
        };

        page.Rows.Add(new ReportRow
        {
            Text = "Linked DWGs",
            Label = "Referenced, not embedded",
            Value = Plural(_report.Linked.Count, "file"),
            DrillKey = "linked",
            GroupKey = "CAD IN THIS PROJECT",
        });

        page.Rows.Add(new ReportRow
        {
            Text = "Imported DWGs",
            Label = _report.Imported.Count == 0
                ? "None — nothing to clean up"
                : "Embedded in the model — review these",
            Value = Plural(_report.Imported.Count, "file"),
            DrillKey = "imported",
            GroupKey = "CAD IN THIS PROJECT",
        });

        if (_report.UnplacedTypes.Count > 0)
        {
            page.Rows.Add(new ReportRow
            {
                Text = "Unplaced CAD types",
                Label = "Types still in the file with no instance placed",
                Value = Plural(_report.UnplacedTypes.Count, "type"),
                DrillKey = "unplaced",
                GroupKey = "CAD IN THIS PROJECT",
            });
        }

        return page;
    }

    /// <summary>Resolves a drill row into its child page. Wired up as the drill provider.</summary>
    public ReportPage? Drill(ReportRow row)
    {
        switch (row.DrillKey)
        {
            case "linked":
                return BuildList(_report.Linked, "Linked DWGs", "LINKED DWGS");
            case "imported":
                return BuildList(_report.Imported, "Imported DWGs", "IMPORTED DWGS");
            case "unplaced":
                return BuildUnplaced();
        }

        // "entry:<elementId>" — a single DWG's detail sheet.
        if (row.DrillKey != null && row.DrillKey.StartsWith("entry:"))
        {
            if (!long.TryParse(row.DrillKey.AsSpan(6), out var id)) return null;
            var entry = _report.Linked.Concat(_report.Imported)
                .FirstOrDefault(e => e.ElementId == id);
            return entry == null ? null : BuildDetail(entry);
        }

        return null;
    }

    private ReportPage BuildList(List<CadEntry> entries, string title, string groupKey)
    {
        var page = new ReportPage
        {
            Breadcrumb = $"CAD · {title} — {Plural(entries.Count, "file")}",
            BackLabel = "CAD overview",
            Hint = "Enter: details · Esc: back · Type: filter",
        };

        if (entries.Count == 0)
        {
            page.Rows.Add(new ReportRow
            {
                Text = $"No {title.ToLowerInvariant()} in this project",
                GroupKey = groupKey,
            });
            return page;
        }

        foreach (var e in entries)
        {
            page.Rows.Add(new ReportRow
            {
                Text = e.Name,
                // The most useful thing at a glance is where it sits, which is
                // exactly what hides view-specific CAD in drafting views.
                Label = e.Workset,
                Value = e.IsViewSpecific ? "view" : "model",
                DrillKey = "entry:" + e.ElementId,
                GroupKey = groupKey,
            });
        }

        return page;
    }

    private ReportPage BuildDetail(CadEntry e)
    {
        var page = new ReportPage
        {
            Breadcrumb = $"CAD · {e.Name}",
            BackLabel = e.IsLinked ? "Linked DWGs" : "Imported DWGs",
            Hint = "Enter on “Select in model” · Esc: back",
        };

        var group = e.Name.ToUpperInvariant();

        void Field(string label, string value) => page.Rows.Add(new ReportRow
        {
            Label = label,
            Text = value,
            GroupKey = group,
        });

        Field("DWG name", e.Name);
        Field("DWG created by", e.CreatedBy);
        Field("DWG id", e.ElementId.ToString());
        Field("DWG workset", e.Workset);
        Field("Hosted level", e.HostedLevel);
        Field("Offset from level (Z)", e.OffsetDisplay);

        page.Rows.Add(new ReportRow
        {
            Text = "Select in model",
            Label = e.IsLinked ? "Linked CAD" : "Imported CAD",
            RevitElementId = e.ElementId,
            GroupKey = group,
        });

        return page;
    }

    private ReportPage BuildUnplaced()
    {
        var page = new ReportPage
        {
            Breadcrumb = $"CAD · Unplaced types — {Plural(_report.UnplacedTypes.Count, "type")}",
            BackLabel = "CAD overview",
            Hint = "Esc: back · Type: filter",
        };

        foreach (var (name, id) in _report.UnplacedTypes)
        {
            page.Rows.Add(new ReportRow
            {
                Text = name,
                Label = "No instance placed",
                Value = id.ToString(),
                GroupKey = "UNPLACED CAD TYPES",
            });
        }

        return page;
    }

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
