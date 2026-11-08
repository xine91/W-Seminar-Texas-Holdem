using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class ScenarioBModel : PageModel
{
    private readonly ScenarioResultStore _scenarioStore;

    public ScenarioBModel(ScenarioResultStore scenarioStore)
    {
        _scenarioStore = scenarioStore;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public PushFoldResult? Result { get; private set; }
    public ScenarioMatrixCell? HeroCell { get; private set; }
    public PushFoldIterationResult? IterationResult { get; private set; }
    public ScenarioMatrixCell[,]? Cells { get; private set; }
    public ScenarioMatrixCell[,]? BbCells { get; private set; }
    public IReadOnlyList<char> Ranks { get; } = MatrixAggregator.RanksOrder();
    public string? ErrorMessage { get; private set; }
    public string? SavedRunId { get; private set; }
    public string ScenarioFilePath => _scenarioStore.FilePath;

    public void OnGet() { }

    public void OnPost()
    {
        if (!ModelState.IsValid)
            return;

        try
        {
            var range = ParseRange(Input.OpponentRange);
            var heroHand = StartingHandNotation.Normalize(Input.HeroHand);
            var iteration = ScenarioCalculators.IteratePushFold(
                range, Input.Stack, Input.Iterations, Input.MaxSteps, Input.Seed, HttpContext.RequestAborted);

            IterationResult = iteration;
            Cells = iteration.SbCells;
            BbCells = iteration.BbCells;
            var heroCell = Cells.Cast<ScenarioMatrixCell>().Single(cell => cell.Hand == heroHand);
            HeroCell = heroCell;
            Result = ScenarioCalculators.CalculatePushFold(Input.Stack, iteration.FoldProbability, heroCell.Equity);

            SavedRunId = _scenarioStore.Append(new ScenarioRunRecord(
                Guid.NewGuid().ToString("N"),
                DateTime.UtcNow,
                "B",
                new ScenarioRunParameters(
                    string.Join(",", iteration.InputCallRange),
                    Input.Iterations,
                    iteration.Seed,
                    Stack: Input.Stack,
                    FoldProbability: iteration.FoldProbability,
                    SbPushRange: string.Join(",", iteration.SbPushRange),
                    InitialCallRange: string.Join(",", range),
                    ResultCallRange: string.Join(",", iteration.BbCallRange)),
                Cells.Cast<ScenarioMatrixCell>().ToArray(),
                BbCells.Cast<ScenarioMatrixCell>().ToArray(),
                BestResponseSteps: iteration.Steps,
                Converged: iteration.Converged,
                StopReason: iteration.StopReason,
                History: iteration.History));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string[] ParseRange(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string GetColorRgb(ScenarioMatrixCell cell)
        => cell.HeroAction is "Push" or "Call" ? "40,167,69" : cell.HeroAction == "Fold" ? "220,53,69" : "160,120,0";

    public string FormatCellValue(ScenarioMatrixCell cell)
        => $"{cell.Equity:P0} {cell.Ev:+0.00;-0.00;0.00}";

    public sealed class InputModel
    {
        [Required]
        public string HeroHand { get; set; } = "AKs";

        [Required]
        public string OpponentRange { get; set; } = "AA,KK,QQ,JJ,TT,99,88,77,66,55,AJs,KQs,AKo";

        [Range(1, 1000)]
        public int Stack { get; set; } = 10;

        [Range(50000, 200000)]
        public int Iterations { get; set; } = 50000;

        [Range(1, 300)]
        public int MaxSteps { get; set; } = 50;

        public int? Seed { get; set; } = 7;
    }
}
