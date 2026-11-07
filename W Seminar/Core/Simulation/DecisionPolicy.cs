using System;
using System.Collections.Generic;
using PokerSimPlatform.Core.Engine;
using PokerSimPlatform.Core.Models;

namespace PokerSimPlatform.Core.Simulation;

/// <summary>Ergebnis der Policy: Entscheidung plus die zugrunde liegenden EW-Werte.</summary>
public sealed record PolicyOutcome(
    PlayerDecision Decision,
    double EvCall,
    double EvRaise,
    double EvCallStdError);

/// <summary>
/// Entscheidungs-Policy.
/// 1) Mit Pot und CallCost (EV-Modus): Entscheidung per Erwartungswert
///    (Pot Odds, Equity Realization, Rake, Implied Odds, Fold Equity, Stack Depth).
/// 2) Ohne diese Angaben: positionsabhängige Equity-Schwellen.
///
/// Hinweis: Realization, Implied Odds und Fold Equity sind bewusst vereinfachte Heuristiken
/// (keine vollständige Postflop-Lösung) und über <see cref="BettingContext"/> parametrierbar.
/// </summary>
public sealed class DecisionPolicy
{
    public double RaiseThreshold { get; init; } = 0.55;
    public double CallThreshold { get; init; } = 0.30;

    // Fallback-Schwellen ohne Pot-Angaben – Spreizung UTG ↔ BTN größer als zuvor.
    private static readonly Dictionary<TablePosition, (double RaiseT, double CallT)> PositionThresholds = new()
    {
        { TablePosition.UTG,  (0.68, 0.42) },
        { TablePosition.UTG1, (0.65, 0.40) },
        { TablePosition.MP,   (0.61, 0.37) },
        { TablePosition.MP1,  (0.58, 0.34) },
        { TablePosition.HJ,   (0.55, 0.31) },
        { TablePosition.CO,   (0.51, 0.27) },
        { TablePosition.BTN,  (0.46, 0.23) },
        { TablePosition.SB,   (0.56, 0.33) },
        { TablePosition.BB,   (0.46, 0.19) },
    };

    // Equity-Realization (Anteil der Roh-Equity, der tatsächlich realisiert wird): Position hat Vorteil.
    private static readonly Dictionary<TablePosition, double> BaseRealization = new()
    {
        { TablePosition.UTG, 0.90 }, { TablePosition.UTG1, 0.90 }, { TablePosition.MP, 0.92 },
        { TablePosition.MP1, 0.93 }, { TablePosition.HJ, 0.95 }, { TablePosition.CO, 0.97 },
        { TablePosition.BTN, 1.00 }, { TablePosition.SB, 0.88 }, { TablePosition.BB, 0.93 },
    };

    /// <summary>Realization sinkt mit der Zahl noch ausstehender Karten (Preflop am stärksten, River = 1).</summary>
    public static double Realization(TablePosition position, int boardCount)
    {
        double baseR = BaseRealization.TryGetValue(position, out var r) ? r : 0.95;
        double remainingShare = (5 - Math.Clamp(boardCount, 0, 5)) / 5.0;
        return Math.Pow(baseR, remainingShare);
    }

    /// <summary>Schätzung der Fold Equity pro Gegner: weite Ranges folgen einem Raise seltener.</summary>
    public static double EstimateFoldEquity(double rangeWidth)
        => Math.Clamp(0.15 + 0.45 * Math.Sqrt(Math.Clamp(rangeWidth, 0, 1)), 0.05, 0.65);

    public PlayerDecision DecideByEquity(double equity, TablePosition position)
    {
        var (raiseT, callT) = PositionThresholds.TryGetValue(position, out var t)
            ? t : (RaiseThreshold, CallThreshold);

        if (equity >= raiseT) return PlayerDecision.Raise;
        if (equity >= callT) return PlayerDecision.Call;
        return PlayerDecision.Fold;
    }

    public PolicyOutcome Decide(
        EquityResult equity,
        TablePosition position,
        BettingContext? context,
        int boardCount,
        int numOpponents,
        double opponentRangeWidth)
    {
        if (context == null || !context.IsEvMode)
            return new PolicyOutcome(DecideByEquity(equity.Equity, position), 0, 0, 0);

        double realization = Realization(position, boardCount);
        double eqR = Math.Clamp(equity.Equity * realization, 0, 1);
        double pot = context.Pot;
        double stack = Math.Max(context.EffectiveStack, 0);
        double callCost = Math.Min(context.CallCost, stack);
        double implied = boardCount >= 5 ? 0 : context.ImpliedOddsFactor * Math.Max(0, stack - callCost);

        // Call
        double rakeCall = Rake(context, pot + callCost);
        double evCall = eqR * (pot - rakeCall + implied) - (1 - eqR) * callCost;
        double slope = realization * (pot - rakeCall + implied + callCost);
        double evCallSe = slope * equity.StdError;

        // Raise (inkl. Fold Equity)
        double invest = context.RaiseSize > 0 ? context.RaiseSize : callCost + pot;
        invest = Math.Clamp(invest, callCost, Math.Max(stack, callCost));
        double extraFromVillain = Math.Max(0, invest - callCost);
        double rakeRaise = Rake(context, pot + invest + extraFromVillain);
        double evCalled = eqR * (pot + extraFromVillain - rakeRaise + implied) - (1 - eqR) * invest;
        double feSingle = context.FoldEquityOverride ?? EstimateFoldEquity(opponentRangeWidth);
        double feAll = Math.Pow(Math.Clamp(feSingle, 0, 1), Math.Max(1, numOpponents));
        double evRaise = feAll * (pot - Rake(context, pot)) + (1 - feAll) * evCalled;

        PlayerDecision decision;
        if (evRaise > evCall && evRaise > 0) decision = PlayerDecision.Raise;
        else if (evCall > 0) decision = PlayerDecision.Call;
        else decision = PlayerDecision.Fold;

        return new PolicyOutcome(decision, evCall, evRaise, evCallSe);
    }

    private static double Rake(BettingContext c, double finalPot)
    {
        if (c.RakePercent <= 0) return 0;
        double rake = c.RakePercent * finalPot;
        return c.RakeCap > 0 ? Math.Min(rake, c.RakeCap) : rake;
    }
}
