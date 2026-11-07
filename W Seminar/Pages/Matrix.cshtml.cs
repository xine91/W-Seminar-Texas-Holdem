using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Models;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class MatrixModel : PageModel
{
    private readonly ResultStore _store;

    public MatrixModel(ResultStore store)
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

    [BindProperty(SupportsGet = true)]
    public string SelectedMetric { get; set; } = "equity";

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

    public string FormatTooltip(MatrixCell c) =>
        $"{c.Hand} | N={c.TotalSamples} | Sims={c.TotalSimulations} | Eq={c.AvgEquity:P1} ±{c.EquityCi95:P1} | R={c.RaiseRate:P0} C={c.CallRate:P0} F={c.FoldRate:P0}" +
        (c.LowSample ? " | wenige Samples!" : "");

    public double GetIntensity(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return 0;
        return SelectedMetric switch
        {
            "raise" => cell.RaiseRate,
            "call" => cell.CallRate,
            "fold" => cell.FoldRate,
            _ => cell.AvgEquity
        };
    }

    public string GetColorRgb(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return "230,230,230";
        var v = GetIntensity(cell);
        // Grün-hoch / Rot-niedrig
        if (SelectedMetric == "fold")
        {
            // invertiert: hoch = rot
            if (v >= 0.5) return $"220,53,69";
            if (v >= 0.2) return $"253,194,107";
            return $"40,167,69";
        }
        if (v >= 0.55) return $"40,167,69";
        if (v >= 0.35) return $"255,193,7";
        return $"220,53,69";
    }

    public string FormatCellValue(MatrixCell cell)
    {
        if (cell.TotalSamples == 0) return "—";
        return SelectedMetric switch
        {
            "raise" => cell.RaiseRate.ToString("P0"),
            "call" => cell.CallRate.ToString("P0"),
            "fold" => cell.FoldRate.ToString("P0"),
            _ => cell.AvgEquity.ToString("P1") + (cell.LowSample ? " *" : "")
        };
    }
}
