using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class ScenarioMatrixModel : PageModel
{
    private readonly ScenarioResultStore _store;

    public ScenarioMatrixModel(ScenarioResultStore store)
    {
        _store = store;
    }

    public IReadOnlyList<char> Ranks { get; } = MatrixAggregator.RanksOrder();
    public ScenarioMatrixCell[,]? Cells { get; private set; }
    public ScenarioMatrixCell[,]? BbCells { get; private set; }
    public ScenarioRunRecord? SelectedRecord { get; private set; }
    public IReadOnlyList<ScenarioRunRecord> FilteredRecords { get; private set; } = Array.Empty<ScenarioRunRecord>();
    public IReadOnlyList<string> AvailableRanges { get; private set; } = Array.Empty<string>();
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string SelectedScenario { get; set; } = "Alle";

    [BindProperty(SupportsGet = true)]
    public string? SelectedRunId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedRange { get; set; }

    public void OnGet()
    {
        try
        {
            var records = _store.LoadAll()
                .OrderByDescending(record => record.TimestampUtc)
                .ToList();

            IEnumerable<ScenarioRunRecord> filtered = records;
            if (!string.IsNullOrWhiteSpace(SelectedScenario) && SelectedScenario != "Alle")
                filtered = filtered.Where(record => record.Scenario == SelectedScenario);
            AvailableRanges = filtered
                .Select(record => record.Parameters.OpponentRange)
                .Distinct()
                .OrderBy(range => range)
                .ToList();
            if (!string.IsNullOrWhiteSpace(SelectedRange))
                filtered = filtered.Where(record => record.Parameters.OpponentRange == SelectedRange);

            FilteredRecords = filtered.ToList();

            SelectedRecord = !string.IsNullOrWhiteSpace(SelectedRunId)
                ? FilteredRecords.FirstOrDefault(record => record.RunId == SelectedRunId)
                : FilteredRecords.FirstOrDefault();

            if (SelectedRecord == null)
                return;

            Cells = BuildMatrix(SelectedRecord.Cells);
            if (SelectedRecord.Scenario == "B" && SelectedRecord.BbCells is { } bbCells)
                BbCells = BuildMatrix(bbCells);
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"JSON-Datei konnte nicht gelesen werden: {ex.Message}";
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Ergebnisdatei konnte nicht gelesen werden: {ex.Message}";
        }
    }

    public string GetColorRgb(ScenarioMatrixCell cell)
        => cell.HeroAction is "Push" or "Raise" or "Call"
            ? "40,167,69"
            : cell.HeroAction == "Fold" ? "220,53,69" : "160,120,0";

    public string FormatCellValue(ScenarioMatrixCell cell)
        => $"{cell.Equity:P0} {cell.Ev:+0.00;-0.00;0.00}";

    public string FormatTimestamp(DateTime timestamp)
        => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static ScenarioMatrixCell[,] BuildMatrix(IReadOnlyList<ScenarioMatrixCell> source)
    {
        var byHand = source.ToDictionary(cell => cell.Hand, StringComparer.OrdinalIgnoreCase);
        var matrix = new ScenarioMatrixCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();

        for (int row = 0; row < 13; row++)
        {
            for (int column = 0; column < 13; column++)
            {
                var hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                matrix[row, column] = byHand.TryGetValue(hand, out var cell)
                    ? cell
                    : new ScenarioMatrixCell(hand, 0, 0, "—", "—", 0);
            }
        }

        return matrix;
    }
}
