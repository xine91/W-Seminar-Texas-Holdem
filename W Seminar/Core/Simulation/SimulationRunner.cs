using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Models;

namespace PokerSimPlatform.Core.Simulation;

/// <summary>Parameter eines Batch-Laufs.</summary>
public sealed record SimulationRequest(
    TableConfig Config,
    IReadOnlyList<string> StartingHands,
    IReadOnlyList<string> OpponentRange,
    int NumOpponents = 1,
    IReadOnlyList<Card>? Board = null,
    BettingContext? Betting = null,
    int? Seed = null,
    double TargetCiHalfWidth = 0);

public readonly record struct SimulationProgress(int Current, int Total);

/// <summary>
/// Cache für Equity-Ergebnisse. Schlüssel: (Hand, Range, Board, Gegner, Iterationen, Ziel-KI, Seed).
/// Die Equity hängt nicht von der Position ab → wird pro Hand nur einmal berechnet
/// und für alle Positionen (und wiederholte Läufe mit gleichem Seed) wiederverwendet.
/// </summary>
public sealed class EquityCache
{
    private const int MaxEntries = 100_000;
    private readonly ConcurrentDictionary<string, EquityResult> _items = new();

    public int Count => _items.Count;

    public EquityResult GetOrAdd(string key, Func<EquityResult> factory)
    {
        if (_items.TryGetValue(key, out var hit)) return hit;
        var value = factory();
        if (_items.Count >= MaxEntries) _items.Clear();
        _items[key] = value;
        return value;
    }
}

/// <summary>
/// Batch-Simulator: berechnet Equity je Starthand (parallel, deterministisch über Seed),
/// leitet je Position die Entscheidung ab und liefert die Ergebnisse (Persistenz erfolgt außerhalb).
/// </summary>
public sealed class SimulationRunner
{
    private readonly DecisionPolicy _policy;
    private readonly ILogger? _logger;
    private readonly EquityCache _cache;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Tatsächlich verwendeter Basis-Seed (zur Reproduktion eines Laufs).</summary>
    public int Seed { get; private set; }

    public SimulationRunner(ILogger? logger = null, EquityCache? cache = null, DecisionPolicy? policy = null)
    {
        _logger = logger;
        _cache = cache ?? new EquityCache();
        _policy = policy ?? new DecisionPolicy();
    }

    public IReadOnlyList<SimulationResult> Run(
        SimulationRequest request,
        IProgress<SimulationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var config = request.Config;
        Seed = request.Seed ?? Random.Shared.Next();

        var hands = request.StartingHands.Select(StartingHandNotation.Normalize).Distinct().ToList();
        var opponentRange = request.OpponentRange.Select(StartingHandNotation.Normalize).Distinct().ToList();
        var pool = EquityCalculator.ExpandRange(opponentRange);
        double rangeWidth = pool.Count / 1326.0;
        string rangeKey = string.Join(",", opponentRange);

        var board = ResolveBoard(request);
        string phase = board.Count switch { 0 => "Preflop", 3 => "Flop", 4 => "Turn", _ => "Endgame" };
        string? boardText = board.Count == 0 ? null : string.Concat(board.Select(c => c.ToString()));

        var sw = Stopwatch.StartNew();
        _logger?.LogInformation(
            "[SIM {Session}] Start: {Positions} Positionen, {Hands} Hände, Phase {Phase}, max. {MC} Iterationen, Seed {Seed}",
            SessionId, config.Positions.Count, hands.Count, phase, config.MonteCarloIterations, Seed);

        var equities = new EquityResult?[hands.Count];
        int done = 0;
        int logStep = Math.Max(1, hands.Count / 10);

        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.For(0, hands.Count, options, i =>
        {
            var hand = hands[i];
            string key = $"{hand}|{rangeKey}|{boardText}|{request.NumOpponents}|{config.MonteCarloIterations}|{request.TargetCiHalfWidth}|{Seed}";
            try
            {
                equities[i] = _cache.GetOrAdd(key, () =>
                {
                    var calc = new EquityCalculator(config.MonteCarloIterations, HandSeed(Seed, hand), request.TargetCiHalfWidth);
                    return calc.Calculate(hand, pool, request.NumOpponents, board, ct);
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogWarning("[SIM {Session}] Hand {Hand} übersprungen: {Message}", SessionId, hand, ex.Message);
            }

            int current = Interlocked.Increment(ref done);
            progress?.Report(new SimulationProgress(current, hands.Count));
            if (current % logStep == 0)
                _logger?.LogInformation("[SIM {Session}] Fortschritt {Current}/{Total}", SessionId, current, hands.Count);
        });

        var results = new List<SimulationResult>(hands.Count * config.Positions.Count);
        foreach (var position in config.Positions)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                var eq = equities[i];
                if (eq == null) continue;

                var outcome = _policy.Decide(eq, position, request.Betting, board.Count, request.NumOpponents, rangeWidth);
                results.Add(new SimulationResult(
                    SessionId: SessionId,
                    Timestamp: DateTime.UtcNow,
                    Position: position,
                    StartingHand: hands[i],
                    Decision: outcome.Decision,
                    Equity: Math.Round(eq.Equity, 6),
                    NumSimulations: eq.Samples,
                    TableSize: config.TableSize,
                    NumOpponents: request.NumOpponents,
                    Phase: phase,
                    ExpectedValue: Math.Round(outcome.EvCall, 6),
                    Pot: request.Betting?.Pot ?? 0,
                    CallCost: request.Betting?.CallCost ?? 0,
                    Board: boardText,
                    EquityStdError: Math.Round(eq.StdError, 6),
                    ExpectedValueStdError: Math.Round(outcome.EvCallStdError, 6),
                    ExpectedValueRaise: Math.Round(outcome.EvRaise, 6),
                    OpponentRange: rangeKey));
            }
        }

        sw.Stop();
        _logger?.LogInformation("[SIM {Session}] Fertig: {Count} Ergebnisse in {Ms} ms (Cache: {Cache})",
            SessionId, results.Count, sw.ElapsedMilliseconds, _cache.Count);
        return results;
    }

    /// <summary>
    /// Bestimmt das feste Board: explizit übergeben, oder – bei IncludeFlop ohne Board – ein
    /// seed-deterministischer Zufallsflop (3 Karten); Turn und River werden dann simuliert.
    /// </summary>
    private IReadOnlyList<Card> ResolveBoard(SimulationRequest request)
    {
        var board = request.Board ?? Array.Empty<Card>();
        if (request.Config.IncludeFlop && board.Count == 0)
        {
            var deck = new Deck(new Random(unchecked(Seed * 31 + 7)));
            return new[] { deck.Draw(), deck.Draw(), deck.Draw() };
        }
        if (request.Config.IncludeFlop && board.Count != 3)
            throw new ArgumentException("Flop-Phase erfordert genau 3 Board-Karten.", nameof(request));
        return board;
    }

    /// <summary>Stabiler Seed pro Hand (string.GetHashCode ist pro Prozess randomisiert und daher ungeeignet).</summary>
    private static int HandSeed(int baseSeed, string hand)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in hand) h = (h ^ c) * 16777619;
            return (int)(h ^ (uint)baseSeed);
        }
    }
}
