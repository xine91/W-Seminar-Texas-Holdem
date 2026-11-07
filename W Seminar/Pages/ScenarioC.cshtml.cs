using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Simulation;

namespace W_Seminar.Pages;

public class ScenarioCModel : PageModel
{
    private readonly ScenarioResultStore _scenarioStore;

    public ScenarioCModel(ScenarioResultStore scenarioStore)
    {
        _scenarioStore = scenarioStore;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public RfiResult? Result { get; private set; }
    public ScenarioMatrixCell[,]? Cells { get; private set; }
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
            var range = Input.OpponentCallRange
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var equity = new EquityCalculator(Input.Iterations, Input.Seed)
                .Calculate(Input.HeroHand, range, 1);

            Result = ScenarioCalculators.CalculateRfi(
                Input.OpenSize,
                Input.DeadMoney,
                Input.FoldProbability,
                Input.Realization,
                equity.Equity);

            Cells = ScenarioCalculators.CalculateRfiMatrix(
                range,
                Input.OpenSize,
                Input.DeadMoney,
                Input.FoldProbability,
                Input.Realization,
                Input.Iterations,
                Input.Seed);

            SavedRunId = _scenarioStore.Append(new ScenarioRunRecord(
                Guid.NewGuid().ToString("N"),
                DateTime.UtcNow,
                "C",
                new ScenarioRunParameters(
                    string.Join(",", range),
                    Input.Iterations,
                    Input.Seed,
                    FoldProbability: Input.FoldProbability,
                    OpenSize: Input.OpenSize,
                    DeadMoney: Input.DeadMoney,
                    Realization: Input.Realization),
                Cells.Cast<ScenarioMatrixCell>().ToArray()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            ErrorMessage = ex.Message;
        }
    }

    public string GetColorRgb(ScenarioMatrixCell cell)
        => cell.Ev > 0 ? "40,167,69" : cell.Ev > -0.1 ? "255,193,7" : "220,53,69";

    public string FormatCellValue(ScenarioMatrixCell cell)
        => $"{cell.Equity:P0} {cell.Ev:+0.00;-0.00;0.00}";

    public sealed class InputModel
    {
        [Required]
        public string HeroHand { get; set; } = "AKs";

        [Required]
        public string OpponentCallRange { get; set; } = "AA,KK,QQ,JJ,TT,99,88,77,66,55,AJs,KQs,AKo";

        [Range(0, double.MaxValue)]
        public double OpenSize { get; set; } = 2.5;

        [Range(0, double.MaxValue)]
        public double DeadMoney { get; set; } = 1.5;

        [Range(0, 1)]
        public double FoldProbability { get; set; } = 0.5;

        [Range(0, 1)]
        public double Realization { get; set; } = 0.9;

        [Range(50, 200000)]
        public int Iterations { get; set; } = 2000;

        public int? Seed { get; set; } = 7;
    }
}
