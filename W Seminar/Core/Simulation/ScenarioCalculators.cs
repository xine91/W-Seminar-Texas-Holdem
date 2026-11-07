using System;
using PokerSimPlatform.Core.Engine;

namespace PokerSimPlatform.Core.Simulation;

public sealed record PushFoldResult(
    double Equity,
    double EvPush,
    double EvFold,
    bool Push);

public sealed record BbCallResult(double Equity, double EvCall, bool Call);

public sealed record PushFoldIterationResult(
    ScenarioMatrixCell[,] SbCells,
    ScenarioMatrixCell[,] BbCells,
    IReadOnlyList<string> InputCallRange,
    IReadOnlyList<string> SbPushRange,
    IReadOnlyList<string> BbCallRange,
    double FoldProbability,
    int Steps,
    bool Converged,
    string StopReason,
    int Seed,
    IReadOnlyList<StrategyHistoryEntry> History);

public sealed record StrategyHistoryEntry(int Step, double FoldProbability, double PushShare);

public sealed record RfiResult(
    double Equity,
    double EvRaise,
    bool Raise);

public sealed record ScenarioMatrixCell(
    string Hand,
    double Equity,
    double Ev,
    string HeroAction,
    string OpponentAction,
    int Samples,
    double? ActionFrequency = null,
    double? EquityStdError = null);

public static class ScenarioCalculators
{
    public static ScenarioMatrixCell[,] CalculatePushFoldMatrix(
        IReadOnlyList<string> opponentRange,
        double stack,
        double foldProbability,
        int iterations,
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        var pool = EquityCalculator.ExpandRange(opponentRange);
        if (pool.Count == 0) throw new ArgumentException("Die BB Call-Range darf nicht leer sein.", nameof(opponentRange));
        var cells = new ScenarioMatrixCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();
        int baseSeed = seed ?? Random.Shared.Next();

        Parallel.For(0, 13, new ParallelOptions { CancellationToken = cancellationToken }, row =>
        {
            for (int column = 0; column < 13; column++)
            {
                string hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                var equity = new EquityCalculator(iterations, CellSeed(baseSeed, row, column))
                    .Calculate(hand, pool, 1, ct: cancellationToken);
                var result = CalculatePushFold(stack, foldProbability, equity.Equity);

                cells[row, column] = new ScenarioMatrixCell(
                    hand,
                    equity.Equity,
                    result.EvPush,
                    result.Push ? "Push" : "Fold",
                    "—",
                    equity.Samples);
            }
        });

        return cells;
    }

    public static ScenarioMatrixCell[,] CalculateBbCallMatrix(
        IReadOnlyList<string> sbPushRange,
        double stack,
        int iterations,
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (stack <= 0) throw new ArgumentOutOfRangeException(nameof(stack));
        var pool = EquityCalculator.ExpandRange(sbPushRange);
        if (pool.Count == 0) throw new ArgumentException("Die SB Push-Range darf nicht leer sein.", nameof(sbPushRange));

        var cells = new ScenarioMatrixCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();
        int baseSeed = seed ?? Random.Shared.Next();

        Parallel.For(0, 13, new ParallelOptions { CancellationToken = cancellationToken }, row =>
        {
            for (int column = 0; column < 13; column++)
            {
                string hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                var equity = new EquityCalculator(iterations, CellSeed(baseSeed, row, column))
                    .Calculate(hand, pool, 1, ct: cancellationToken);
                var result = CalculateBbCall(stack, equity.Equity);
                cells[row, column] = new ScenarioMatrixCell(
                    hand, equity.Equity, result.EvCall,
                    result.Call ? "Call" : "Fold", "—", equity.Samples);
            }
        });

        return cells;
    }

    public static PushFoldIterationResult IteratePushFold(
        IReadOnlyList<string> initialCallRange,
        int stack,
        int iterations,
        int maxSteps,
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (stack < 1) throw new ArgumentOutOfRangeException(nameof(stack));
        if (iterations < 50000) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (maxSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxSteps));

        var callRange = initialCallRange.Select(StartingHandNotation.Normalize).ToHashSet(StringComparer.Ordinal);
        if (callRange.Count == 0)
            throw new ArgumentException("Die anfängliche BB Call-Range darf nicht leer sein.", nameof(initialCallRange));

        int baseSeed = seed ?? Random.Shared.Next();
        var hands = StartingHandNotation.All169();
        var sb = hands.ToDictionary(hand => hand, _ => 1.0, StringComparer.Ordinal);
        var bb = hands.ToDictionary(hand => hand, hand => callRange.Contains(hand) ? 1.0 : 0.0, StringComparer.Ordinal);
        var history = new List<StrategyHistoryEntry>();
        double previousFold = 1 - new WeightedHandRange(bb).TotalWeight / 1326.0;
        double previousPush = 1;

        for (int step = 1; step <= maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sbResponses = CalculateMixedMatrix(bb, sb, stack, iterations, baseSeed, true, cancellationToken);
            var bbResponses = CalculateMixedMatrix(sb, bb, stack, iterations, baseSeed, false, cancellationToken);
            double alpha = 1.0 / (step + 1);
            foreach (var cell in sbResponses)
                sb[cell.Hand] += alpha * (BestResponse(cell.Ev, -0.5, sb[cell.Hand]) - sb[cell.Hand]);
            foreach (var cell in bbResponses)
                bb[cell.Hand] += alpha * (BestResponse(cell.Ev, -1, bb[cell.Hand]) - bb[cell.Hand]);

            double foldProbability = 1 - new WeightedHandRange(bb).TotalWeight / 1326.0;
            double pushShare = new WeightedHandRange(sb).TotalWeight / 1326.0;
            history.Add(new StrategyHistoryEntry(step, foldProbability, pushShare));
            bool stable = step > 1 && Math.Abs(foldProbability - previousFold) < 0.005
                && Math.Abs(pushShare - previousPush) < 0.005;
            if (stable || step == maxSteps)
            {
                var sbCells = CalculateMixedMatrix(bb, sb, stack, iterations, baseSeed, true, cancellationToken);
                var bbCells = CalculateMixedMatrix(sb, bb, stack, iterations, baseSeed, false, cancellationToken);
                var bbHands = hands.Where(hand => bb[hand] > 0).ToArray();
                var sbHands = hands.Where(hand => sb[hand] > 0).ToArray();
                return new PushFoldIterationResult(sbCells, bbCells, bbHands, sbHands, bbHands,
                    foldProbability, step, stable, stable ? "AggregateStability" : "MaxSteps", baseSeed, history);
            }
            previousFold = foldProbability;
            previousPush = pushShare;
        }

        throw new InvalidOperationException("Die Best-Response-Berechnung wurde nicht abgeschlossen.");
    }

    private static double BestResponse(double ev, double foldEv, double currentFrequency)
        => ev > foldEv + 0.01 ? 1 : ev < foldEv - 0.01 ? 0 : currentFrequency;

    private static ScenarioMatrixCell[,] CalculateMixedMatrix(
        IReadOnlyDictionary<string, double> opponent, IReadOnlyDictionary<string, double> own,
        int stack, int iterations, int seed, bool isSb, CancellationToken cancellationToken)
    {
        var range = new WeightedHandRange(opponent);
        double foldProbability = isSb ? 1 - range.TotalWeight / 1326.0 : 0;
        var cells = new ScenarioMatrixCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();
        Parallel.For(0, 13, new ParallelOptions { CancellationToken = cancellationToken }, row =>
        {
            for (int column = 0; column < 13; column++)
            {
                string hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                var equity = new EquityCalculator(iterations, CellSeed(seed, row, column))
                    .CalculateWeighted(hand, range, cancellationToken);
                double ev = isSb ? CalculatePushFold(stack, foldProbability, equity.Equity).EvPush
                    : CalculateBbCall(stack, equity.Equity).EvCall;
                double threshold = isSb ? -0.5 : -1;
                string action = ev > threshold + 0.01 ? (isSb ? "Push" : "Call")
                    : ev < threshold - 0.01 ? "Fold" : "Indifferent";
                cells[row, column] = new ScenarioMatrixCell(hand, equity.Equity, ev, action, "—",
                    equity.Samples, own[hand], equity.StdError);
            }
        });
        return cells;
    }

    public static ScenarioMatrixCell[,] CalculateRfiMatrix(
        IReadOnlyList<string> opponentRange,
        double openSize,
        double deadMoney,
        double foldProbability,
        double realization,
        int iterations,
        int? seed = null)
    {
        var cells = new ScenarioMatrixCell[13, 13];
        var ranks = MatrixAggregator.RanksOrder();
        int baseSeed = seed ?? Random.Shared.Next();

        Parallel.For(0, 13, row =>
        {
            for (int column = 0; column < 13; column++)
            {
                string hand = MatrixAggregator.CellNotation(ranks[row], ranks[column]);
                var equity = new EquityCalculator(iterations, CellSeed(baseSeed, row, column))
                    .Calculate(hand, opponentRange, 1);
                var result = CalculateRfi(
                    openSize,
                    deadMoney,
                    foldProbability,
                    realization,
                    equity.Equity);

                cells[row, column] = new ScenarioMatrixCell(
                    hand,
                    equity.Equity,
                    result.EvRaise,
                    result.Raise ? "Raise" : "Fold",
                    "Call range",
                    equity.Samples);
            }
        });

        return cells;
    }

    public static PushFoldResult CalculatePushFold(double stack, double foldProbability, double equity)
    {
        ValidateProbability(foldProbability, nameof(foldProbability));
        ValidateProbability(equity, nameof(equity));
        if (stack <= 0) throw new ArgumentOutOfRangeException(nameof(stack));

        double evFold = -0.5;
        double evPush = foldProbability + (1 - foldProbability) * (2 * stack * equity - stack);
        return new PushFoldResult(equity, evPush, evFold, evPush > evFold);
    }

    public static BbCallResult CalculateBbCall(double stack, double bbEquity)
    {
        ValidateProbability(bbEquity, nameof(bbEquity));
        if (stack <= 0) throw new ArgumentOutOfRangeException(nameof(stack));

        double evCall = 2 * stack * bbEquity - stack;
        return new BbCallResult(bbEquity, evCall, evCall > -1);
    }

    public static RfiResult CalculateRfi(
        double openSize,
        double deadMoney,
        double foldProbability,
        double realization,
        double equity)
    {
        ValidateProbability(foldProbability, nameof(foldProbability));
        ValidateProbability(realization, nameof(realization));
        ValidateProbability(equity, nameof(equity));
        if (openSize < 0) throw new ArgumentOutOfRangeException(nameof(openSize));
        if (deadMoney < 0) throw new ArgumentOutOfRangeException(nameof(deadMoney));

        double evRaise = foldProbability * deadMoney
            + (1 - foldProbability)
            * (realization * equity * (deadMoney + 2 * openSize) - openSize);

        return new RfiResult(equity, evRaise, evRaise > 0);
    }

    private static void ValidateProbability(double value, string parameterName)
    {
        if (value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(parameterName, "Значение должно находиться в диапазоне от 0 до 1.");
    }

    private static int CellSeed(int seed, int row, int column)
        => unchecked(seed * 397 + row * 31 + column);
}
