using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PokerSimPlatform.Core.Models;
using PokerSimPlatform.Core.Simulation;
using PokerSimPlatform.Web.Services;

namespace W_Seminar.Pages;

[IgnoreAntiforgeryToken]
public class SimulationModel : PageModel
{
    private readonly SimulationJobManager _jobs;
    private readonly ResultStore _store;
    private readonly ILogger<SimulationModel> _logger;

    public SimulationModel(SimulationJobManager jobs, ResultStore store, ILogger<SimulationModel> logger)
    {
        _jobs = jobs;
        _store = store;
        _logger = logger;
    }

    [BindProperty]
    public SimulationInput Input { get; set; } = new();

    /// <summary>Id des gestarteten Hintergrund-Jobs (Fortschritt kommt per SSE).</summary>
    public string? JobId { get; private set; }

    public string FilePath => _store.FilePath;

    public void OnGet() { }

    public IActionResult OnPost()
    {
        Input.Phase = "Preflop";
        Input.Board = null;

        if (!ModelState.IsValid)
            return Page();

        var validation = SimulationInputValidator.Validate(
            Input.HandFilter, Input.OpponentRange, Input.Board, Input.Phase, Input.TableSize, Input.NumOpponents);

        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.Field, error.Message);
            return Page();
        }

        var parsed = validation.Parsed!;
        bool flop = Input.Phase == "Flop";
        var config = Input.TableSize == 9
            ? TableConfig.NineMax(parsed.OpponentRange, Input.MonteCarloIterations, flop)
            : TableConfig.SixMax(parsed.OpponentRange, Input.MonteCarloIterations, flop);

        var betting = new BettingContext(
            Pot: Input.Pot,
            CallCost: Input.CallCost,
            EffectiveStack: Input.EffectiveStack,
            RakePercent: Input.RakePercent / 100.0,
            RakeCap: Input.RakeCap,
            ImpliedOddsFactor: Input.ImpliedOddsFactor,
            RaiseSize: Input.RaiseSize,
            FoldEquityOverride: Input.FoldEquityPercent.HasValue ? Input.FoldEquityPercent.Value / 100.0 : null);

        var request = new SimulationRequest(
            config, parsed.Hands, parsed.OpponentRange, Input.NumOpponents,
            parsed.Board.Count == 0 ? null : parsed.Board,
            betting, Input.Seed, Input.TargetCiHalfWidth);

        var job = _jobs.Enqueue(request);
        JobId = job.Id;
        _logger.LogInformation("[UI] Job {Id} gestartet: {Hands} Hände @ {Size}-max, Phase {Phase}",
            job.Id, parsed.Hands.Count, Input.TableSize, Input.Phase);
        return Page();
    }
}
