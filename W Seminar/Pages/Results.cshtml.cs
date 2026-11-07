using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Models;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class ResultsModel : PageModel
{
    private readonly ResultStore _store;

    public ResultsModel(ResultStore store)
    {
        _store = store;
    }

    public IReadOnlyList<SimulationResult> Results { get; private set; } = Array.Empty<SimulationResult>();
    public TablePosition[] AllPositions { get; } = (TablePosition[])Enum.GetValues(typeof(TablePosition));
    public IReadOnlyList<string> AllSessions { get; private set; } = Array.Empty<string>();
    public int TotalCount { get; private set; }
    public string FilePath => _store.FilePath;

    [BindProperty(SupportsGet = true)] public string? FilterPosition { get; set; }
    [BindProperty(SupportsGet = true)] public string FilterPhase { get; set; } = "Alle";
    [BindProperty(SupportsGet = true)] public string? FilterSession { get; set; }
    [BindProperty(SupportsGet = true)] public int MaxRows { get; set; } = 200;

    public void OnGet()
    {
        var all = _store.LoadAll();
        AllSessions = all.Select(r => r.SessionId).Distinct().OrderByDescending(s => s).ToList();
        TotalCount = all.Count;

        if (!string.IsNullOrEmpty(FilterPosition) && Enum.TryParse<TablePosition>(FilterPosition, out var pos))
            all = all.Where(r => r.Position == pos).ToList();
        if (FilterPhase != "Alle")
            all = all.Where(r => r.Phase == FilterPhase).ToList();
        if (!string.IsNullOrEmpty(FilterSession))
            all = all.Where(r => r.SessionId == FilterSession).ToList();

        Results = all.OrderByDescending(r => r.Timestamp).Take(MaxRows).ToList();
    }

    public IActionResult OnGetDownload()
    {
        var all = _store.LoadAll();
        if (all.Count == 0) return NotFound();
        var bytes = System.Text.Encoding.UTF8.GetBytes(_store.ToJsonArray(all));
        return File(bytes, "application/json", $"simulation_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
    }

    public IActionResult OnGetDownloadCsv()
    {
        var all = _store.LoadAll();
        if (all.Count == 0) return NotFound();
        using var sw = new StringWriter();
        ResultStore.WriteCsv(sw, all);
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sw.ToString())).ToArray();
        return File(bytes, "text/csv", $"simulation_results_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }
}
