using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Models;
using PokerSimPlatform.Core.Simulation;

namespace PokerSimPlatform.Web.Services;

/// <summary>
/// Erzeugt beim ersten Start deterministische Beispieldaten (fester Seed), damit die Matrix-Seite
/// sofort etwas anzeigt. Läuft im Hintergrund und blockiert den Anwendungsstart nicht.
/// </summary>
public sealed class SeedHostedService : BackgroundService
{
    private readonly ResultStore _store;
    private readonly EquityCache _cache;
    private readonly PokerSimOptions _options;
    private readonly ILogger<SeedHostedService> _logger;

    public SeedHostedService(ResultStore store, EquityCache cache, Microsoft.Extensions.Options.IOptions<PokerSimOptions> options, ILogger<SeedHostedService> logger)
    {
        _store = store;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SeedOnStartup)
        {
            _logger.LogInformation("Seed deaktiviert (PokerSim:SeedOnStartup = false).");
            return Task.CompletedTask;
        }

        // Task.Run stellt sicher, dass auch der synchrone Teil nicht den Host-Start blockiert.
        return Task.Run(() =>
        {
            try
            {
                EnsureSeed(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Seed-Erzeugung abgebrochen.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Seed-Initialisierung fehlgeschlagen.");
            }
        }, stoppingToken);
    }

    public void EnsureSeed(CancellationToken ct)
    {
        var existing = _store.LoadAll();
        if (existing.Count > 0)
        {
            _logger.LogInformation("Seed übersprungen: {Count} vorhandene Ergebnisse.", existing.Count);
            return;
        }

        _logger.LogInformation("Erzeuge Seed-Datensatz (Seed {Seed}, {It} Iterationen)…", _options.RandomSeed, _options.SeedIterations);

        var opponentRange = SimulationInputValidator.DefaultOpponentRange;
        var all169 = StartingHandNotation.All169();
        var betting = new BettingContext(Pot: 1.5, CallCost: 1.0);

        var runner = new SimulationRunner(_logger, _cache);
        var preflop = runner.Run(new SimulationRequest(
            TableConfig.SixMax(opponentRange, _options.SeedIterations, flop: false),
            all169, opponentRange, 1, null, betting, _options.RandomSeed), null, ct);

        var flop = runner.Run(new SimulationRequest(
            TableConfig.SixMax(opponentRange, _options.SeedIterations, flop: true),
            all169, opponentRange, 1, null, betting, _options.RandomSeed), null, ct);

        _store.Append(preflop.Concat(flop));
        _logger.LogInformation("Seed-Datensatz geschrieben: {Count} Einträge.", preflop.Count + flop.Count);
    }
}
