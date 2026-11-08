using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public sealed class VergleichModel : PageModel
{
    private const string NashFileName = "chen_ankenman_jamfold_SB_BB.json";
    private readonly ScenarioResultStore _scenarioStore;
    private readonly IWebHostEnvironment _environment;

    public VergleichModel(ScenarioResultStore scenarioStore, IWebHostEnvironment environment)
    {
        _scenarioStore = scenarioStore;
        _environment = environment;
    }

    public IReadOnlyList<char> Ranks { get; } = MatrixAggregator.RanksOrder();
    public IReadOnlyList<int> AvailableStacks { get; private set; } = Array.Empty<int>();
    public ScenarioRunRecord? SelectedSimulationRecord { get; private set; }
    public ComparisonCell[,] NashSbCells { get; private set; } = CreateEmptyComparisonMatrix();
    public ComparisonCell[,] NashBbCells { get; private set; } = CreateEmptyComparisonMatrix();
    public ScenarioMatrixCell[,]? SimulationSbCells { get; private set; }
    public ScenarioMatrixCell[,]? SimulationBbCells { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? SelectedStack { get; set; }

    public void OnGet()
    {
        try
        {
            var records = _scenarioStore.LoadAll()
                .Where(record => record.Scenario == "B" && record.Parameters.Stack.HasValue)
                .OrderByDescending(record => record.TimestampUtc)
                .ToList();

            AvailableStacks = records
                .Select(record => (int)Math.Round(record.Parameters.Stack!.Value))
                .Distinct()
                .OrderBy(stack => stack)
                .ToList();

            if (AvailableStacks.Count == 0)
                return;

            var effectiveStack = SelectedStack.HasValue && AvailableStacks.Contains(SelectedStack.Value)
                ? SelectedStack.Value
                : AvailableStacks[0];
            SelectedStack = effectiveStack;

            SelectedSimulationRecord = records.FirstOrDefault(record =>
                (int)Math.Round(record.Parameters.Stack!.Value) == effectiveStack);

            var nashData = LoadNashData();
            NashSbCells = BuildNashMatrix(nashData.Push, effectiveStack, "Push", "Fold");
            NashBbCells = BuildNashMatrix(nashData.Call, effectiveStack, "Call", "Fold");

            if (SelectedSimulationRecord is null)
                return;

            SimulationSbCells = BuildScenarioMatrix(SelectedSimulationRecord.Cells);
            if (SelectedSimulationRecord.BbCells is { } bbCells)
                SimulationBbCells = BuildScenarioMatrix(bbCells);
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"JSON konnte nicht gelesen werden: {ex.Message}";
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Datei konnte nicht gelesen werden: {ex.Message}";
        }
    }

    public string FormatTimestamp(DateTime timestamp)
        => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string GetCellCssClass(string action)
        => $"matrix-cell {action switch
        {
            "Push" or "Raise" or "Call" => "heat-high",
            "Fold" => "heat-low",
            _ => "heat-mid"
        }}";

    public string GetActionColorRgb(string action)
        => action switch
        {
            "Push" or "Raise" or "Call" => "40,167,69",
            "Fold" => "220,53,69",
            _ => "160,120,0"
        };

    private NashData LoadNashData()
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", NashFileName);
        if (!System.IO.File.Exists(path))
            throw new IOException($"Nash-Datei nicht gefunden: {path}");

        var json = System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<NashData>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new JsonException("Nash-Datei enthält kein gültiges JSON-Objekt.");
    }

    private ComparisonCell[,] BuildNashMatrix(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<double>>> rangesByHand,
        double stack,
        string activeAction,
        string passiveAction)
    {
        var matrix = new ComparisonCell[13, 13];

        for (int row = 0; row < 13; row++)
        {
            for (int column = 0; column < 13; column++)
            {
                var hand = MatrixAggregator.CellNotation(Ranks[row], Ranks[column]);
                var action = IsActionAtStack(rangesByHand, hand, stack)
                    ? activeAction
                    : passiveAction;

                matrix[row, column] = new ComparisonCell(hand, action);
            }
        }

        return matrix;
    }

    private static bool IsActionAtStack(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<double>>> rangesByHand,
        string hand,
        double stack)
    {
        if (!rangesByHand.TryGetValue(hand, out var intervals))
            return false;

        foreach (var interval in intervals)
        {
            if (interval.Count < 2)
                continue;

            var from = interval[0];
            var to = interval[1];
            if (stack >= from && stack <= to)
                return true;
        }

        return false;
    }

    private static ScenarioMatrixCell[,] BuildScenarioMatrix(IReadOnlyList<ScenarioMatrixCell> cells)
    {
        var byHand = cells.ToDictionary(cell => cell.Hand, StringComparer.OrdinalIgnoreCase);
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

    private static ComparisonCell[,] CreateEmptyComparisonMatrix()
    {
        var matrix = new ComparisonCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();
        for (int row = 0; row < 13; row++)
        {
            for (int column = 0; column < 13; column++)
            {
                var hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                matrix[row, column] = new ComparisonCell(hand, "—");
            }
        }

        return matrix;
    }

    public sealed record ComparisonCell(string Hand, string Action);

    private sealed class NashData
    {
        public Dictionary<string, IReadOnlyList<IReadOnlyList<double>>> Push { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<IReadOnlyList<double>>> Call { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
