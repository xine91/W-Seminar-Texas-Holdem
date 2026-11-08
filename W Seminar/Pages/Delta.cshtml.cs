using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public sealed class DeltaModel : PageModel
{
    private const string NashFileName = "chen_ankenman_jamfold_SB_BB.json";
    private readonly ScenarioResultStore _scenarioStore;
    private readonly IWebHostEnvironment _environment;

    public DeltaModel(ScenarioResultStore scenarioStore, IWebHostEnvironment environment)
    {
        _scenarioStore = scenarioStore;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public int? SelectedStack { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedRunId { get; set; }

    public IReadOnlyList<int> AvailableStacks { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<ScenarioRunRecord> AvailableRuns { get; private set; } = Array.Empty<ScenarioRunRecord>();
    public ScenarioRunRecord? SelectedRecord { get; private set; }
    public IReadOnlyList<DeltaRow> SbRows { get; private set; } = Array.Empty<DeltaRow>();
    public IReadOnlyList<DeltaRow> BbRows { get; private set; } = Array.Empty<DeltaRow>();
    public int SbMatchCount { get; private set; }
    public int BbMatchCount { get; private set; }
    public bool HasBbAnalysis { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        try
        {
            var records = _scenarioStore.LoadAll()
                .Where(r => r.Scenario == "B" && r.Parameters.Stack.HasValue)
                .OrderByDescending(r => r.TimestampUtc)
                .ToList();

            AvailableStacks = records
                .Select(r => (int)Math.Round(r.Parameters.Stack!.Value))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (AvailableStacks.Count == 0)
                return;

            var effectiveStack = SelectedStack.HasValue && AvailableStacks.Contains(SelectedStack.Value)
                ? SelectedStack.Value
                : AvailableStacks[0];
            SelectedStack = effectiveStack;

            AvailableRuns = records
                .Where(r => (int)Math.Round(r.Parameters.Stack!.Value) == effectiveStack)
                .OrderByDescending(r => r.TimestampUtc)
                .ToList();

            if (AvailableRuns.Count == 0)
                return;

            SelectedRecord = !string.IsNullOrWhiteSpace(SelectedRunId)
                ? AvailableRuns.FirstOrDefault(r => r.RunId == SelectedRunId)
                : null;
            SelectedRecord ??= AvailableRuns[0];

            var nashData = LoadNashData();
            var sbByHand = SelectedRecord.Cells.ToDictionary(c => c.Hand, StringComparer.OrdinalIgnoreCase);
            var bbByHand = (SelectedRecord.BbCells ?? Array.Empty<ScenarioMatrixCell>())
                .ToDictionary(c => c.Hand, StringComparer.OrdinalIgnoreCase);

            var hands = StartingHandNotation.All169();

            var sbRows = new List<DeltaRow>(hands.Count);
            foreach (var hand in hands)
            {
                var nashAction = IsActionAtStack(nashData.Push, hand, effectiveStack) ? "Push" : "Fold";
                var sim = sbByHand.TryGetValue(hand, out var cell)
                    ? cell
                    : new ScenarioMatrixCell(hand, 0, 0, "—", "—", 0);

                var simulationAction = sim.HeroAction;
                var actionMatch = string.Equals(nashAction, simulationAction, StringComparison.OrdinalIgnoreCase);
                var nashPreferredEv = nashAction == "Push" ? sim.Ev : -0.5;
                var altEv = nashAction == "Push" ? -0.5 : sim.Ev;
                var deltaEv = nashPreferredEv - altEv;

                sbRows.Add(new DeltaRow(hand, nashAction, simulationAction, actionMatch, sim.Ev, deltaEv));
            }

            SbRows = sbRows;
            SbMatchCount = sbRows.Count(row => row.ActionMatch);

            if (SelectedRecord.BbCells is { } bbCells)
            {
                HasBbAnalysis = true;
                var bbRows = new List<DeltaRow>(hands.Count);
                foreach (var hand in hands)
                {
                    var nashAction = IsActionAtStack(nashData.Call, hand, effectiveStack) ? "Call" : "Fold";
                    var sim = bbByHand.TryGetValue(hand, out var cell)
                        ? cell
                        : new ScenarioMatrixCell(hand, 0, 0, "—", "—", 0);

                    var simulationAction = sim.HeroAction;
                    var actionMatch = string.Equals(nashAction, simulationAction, StringComparison.OrdinalIgnoreCase);
                    var nashPreferredEv = nashAction == "Call" ? sim.Ev : -1.0;
                    var altEv = nashAction == "Call" ? -1.0 : sim.Ev;
                    var deltaEv = nashPreferredEv - altEv;

                    bbRows.Add(new DeltaRow(hand, nashAction, simulationAction, actionMatch, sim.Ev, deltaEv));
                }

                BbRows = bbRows;
                BbMatchCount = bbRows.Count(row => row.ActionMatch);
            }
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

    private NashData LoadNashData()
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", NashFileName);
        if (!System.IO.File.Exists(path))
            throw new IOException($"Nash-Datei nicht gefunden: {path}");

        var json = System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<NashData>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new JsonException("Nash-Datei enthält kein gültiges JSON-Objekt.");
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

            if (stack >= interval[0] && stack <= interval[1])
                return true;
        }

        return false;
    }

    public sealed record DeltaRow(
        string Hand,
        string NashAction,
        string SimulationAction,
        bool ActionMatch,
        double SimulationEv,
        double DeltaEvToNash);

    private sealed class NashData
    {
        public Dictionary<string, IReadOnlyList<IReadOnlyList<double>>> Push { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<IReadOnlyList<double>>> Call { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
