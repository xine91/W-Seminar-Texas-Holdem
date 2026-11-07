using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Models;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class EWMatrixModel : PageModel
{
    private readonly ResultStore _store;

    public EWMatrixModel(ResultStore store)
    {
        _store = store;
    }

    public IReadOnlyList<char> Ranks { get; } = MatrixAggregator.RanksOrder();
    public MatrixCell[,]? Cells { get; private set; }
    public TablePosition[] AllPositions { get; } = (TablePosition[])Enum.GetValues(typeof(TablePosition));

    [BindProperty(SupportsGet = true)]
    public string? SelectedPosition { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SelectedPhase { get; set; } = "Alle";

    public int TotalSamples { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedSession { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedRange { get; set; }

    public IReadOnlyList<string> AllSessions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> AllRanges { get; private set; } = Array.Empty<string>();

    public void OnGet()
    {
        var all = _store.LoadAll();
        AllSessions = all.Select(r => r.SessionId).Distinct().OrderByDescending(s => s).ToList();
        AllRanges = all.Select(r => r.OpponentRange).Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).Distinct().ToList();

        TablePosition? posFilter = null;
        if (!string.IsNullOrEmpty(SelectedPosition) && Enum.TryParse<TablePosition>(SelectedPosition, out var p))
            posFilter = p;

        var filtered = new ResultFilter(posFilter, SelectedPhase, SelectedSession, SelectedRange).Apply(all);
        Cells = MatrixAggregator.BuildMatrix(filtered);
        TotalSamples = filtered.Count;
    }

    public double GetIntensity(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return 0;
        return cell.AvgExpectedValue;
    }

    public string GetColorRgb(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return "230,230,230";
        var v = GetIntensity(cell);

        // Rot für negativ, Gelb für neutral, Grün für positiv
        if (v > 0.2) return "40,167,69";      // Grün - positiv
        if (v > -0.1) return "255,193,7";     // Gelb - neutral
        return "220,53,69";                   // Rot - negativ
    }

    public string FormatCellValue(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return "—";
        var val = cell.AvgExpectedValue;
        return (val >= 0 ? $"+{val:F2}" : $"{val:F2}") + (cell.LowSample ? " *" : "");
    }
}
