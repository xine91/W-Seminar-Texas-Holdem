using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PokerSimPlatform.Core.Engine;

/// <summary>Zwei konkrete Karten (als Deck-IDs).</summary>
public readonly record struct HandIds(int A, int B)
{
    public static HandIds From(Card c1, Card c2)
    {
        int a = Deck.ToId(c1), b = Deck.ToId(c2);
        return a < b ? new HandIds(a, b) : new HandIds(b, a);
    }
}

public sealed class WeightedHandRange
{
    internal HandIds[] Hands { get; }
    internal double[] CumulativeWeights { get; }
    public double TotalWeight => CumulativeWeights.Length == 0 ? 0 : CumulativeWeights[^1];

    public WeightedHandRange(IReadOnlyDictionary<string, double> frequencies)
    {
        var hands = new List<HandIds>();
        var cumulative = new List<double>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        double total = 0;
        foreach (var (notation, weight) in frequencies)
        {
            if (!double.IsFinite(weight) || weight < 0 || weight > 1)
                throw new ArgumentOutOfRangeException(nameof(frequencies), "Frequenzen müssen zwischen 0 und 1 liegen.");
            var hand = StartingHandNotation.Normalize(notation);
            if (!seen.Add(hand)) throw new ArgumentException($"Doppelte Hand: {hand}", nameof(frequencies));
            if (weight == 0) continue;
            foreach (var (first, second) in StartingHandNotation.Expand(hand))
            {
                hands.Add(HandIds.From(first, second));
                total += weight;
                cumulative.Add(total);
            }
        }
        if (hands.Count == 0) throw new ArgumentException("Die gewichtete Range ist leer.", nameof(frequencies));
        Hands = hands.ToArray();
        CumulativeWeights = cumulative.ToArray();
    }
}

/// <summary>
/// Ergebnis einer Equity-Berechnung. Equity ist der erwartete Pot-Anteil des Helden (0..1),
/// Ties werden gleichmäßig gesplittet. StdError ist der Standardfehler des Mittelwerts.
/// </summary>
public sealed record EquityResult(double Equity, double StdError, int Samples, bool IsExact = false)
{
    /// <summary>Halbe Breite des 95%-Konfidenzintervalls (Normalapproximation).</summary>
    public double CiHalfWidth => 1.96 * StdError;
    public double CiLow => Math.Max(0, Equity - CiHalfWidth);
    public double CiHigh => Math.Min(1, Equity + CiHalfWidth);
    public double Variance => StdError * StdError * Math.Max(1, Samples);
}

/// <summary>
/// Monte-Carlo-Equity gegen eine Gegnerrange (Heads-Up und Multi-Way) mit optional bekanntem Board.
///
/// - Hero-Notation wird über alle (zum Board passenden) Farbkombinationen gemittelt.
/// - Bekanntes Board (0/3/4/5 Karten) wird fixiert; nur fehlende Karten werden simuliert.
/// - Gegnerhände werden per Rejection-Sampling gezogen (bis konfliktfrei) – keine verworfenen Iterationen.
/// - Ergebnis pro Iteration = Pot-Anteil (Sieg 1, Split 1/k, Niederlage 0) → korrekte Multi-Way-Equity.
/// - Bei vollem Board und genau einem Gegner wird exakt über alle Gegnerhände enumeriert.
/// - Optional: Abbruch, sobald das 95%-KI schmal genug ist (dynamische Iterationszahl).
/// Eine Instanz ist NICHT threadsafe – pro Thread/Hand eine eigene Instanz mit eigenem Seed verwenden.
/// </summary>
public sealed class EquityCalculator
{
    private readonly int _iterations;
    private readonly Random _rng;
    private readonly double _targetHalfWidth;
    private readonly int _minIterations;

    public EquityCalculator(int iterations = 2000, int? seed = null, double targetCiHalfWidth = 0, int minIterations = 200)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Mindestens 1 Iteration.");
        _iterations = iterations;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _targetHalfWidth = targetCiHalfWidth;
        _minIterations = Math.Max(2, minIterations);
    }

    public static IReadOnlyList<HandIds> ExpandRange(IReadOnlyList<string> range)
    {
        var set = new HashSet<HandIds>();
        var list = new List<HandIds>();
        foreach (var notation in range)
            foreach (var (c1, c2) in StartingHandNotation.Expand(notation))
            {
                var h = HandIds.From(c1, c2);
                if (set.Add(h)) list.Add(h);
            }
        return list;
    }

    public EquityResult Calculate(
        string heroNotation,
        IReadOnlyList<string> opponentRange,
        int numOpponents,
        IReadOnlyList<Card>? board = null,
        CancellationToken ct = default)
        => Calculate(heroNotation, ExpandRange(opponentRange), numOpponents, board, ct);

    public EquityResult Calculate(
        string heroNotation,
        IReadOnlyList<HandIds> opponentPool,
        int numOpponents,
        IReadOnlyList<Card>? board = null,
        CancellationToken ct = default)
    {
        if (numOpponents < 1 || numOpponents > 9) throw new ArgumentOutOfRangeException(nameof(numOpponents));
        if (opponentPool.Count == 0) throw new ArgumentException("Gegnerrange ist leer.", nameof(opponentPool));
        board ??= Array.Empty<Card>();
        if (board.Count is not (0 or 3 or 4 or 5))
            throw new ArgumentException("Board muss 0, 3, 4 oder 5 Karten enthalten.", nameof(board));

        var boardIds = board.Select(Deck.ToId).ToArray();
        if (boardIds.Distinct().Count() != boardIds.Length)
            throw new ArgumentException("Board enthält doppelte Karten.", nameof(board));

        var heroHands = StartingHandNotation.Expand(heroNotation)
            .Select(h => HandIds.From(h.Card1, h.Card2))
            .Where(h => Array.IndexOf(boardIds, h.A) < 0 && Array.IndexOf(boardIds, h.B) < 0)
            .ToArray();
        if (heroHands.Length == 0)
            throw new InvalidOperationException($"Hand '{heroNotation}' ist durch das Board vollständig blockiert.");

        return board.Count == 5 && numOpponents == 1
            ? ExactHeadsUp(heroHands, opponentPool, boardIds)
            : MonteCarlo(heroHands, opponentPool, numOpponents, boardIds, ct);
    }

    public EquityResult CalculateWeighted(string heroNotation, WeightedHandRange opponentRange, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opponentRange);
        var heroHands = StartingHandNotation.Expand(StartingHandNotation.Normalize(heroNotation))
            .Select(h => HandIds.From(h.Card1, h.Card2)).ToArray();
        return MonteCarlo(heroHands, opponentRange.Hands, 1, Array.Empty<int>(), ct, opponentRange);
    }

    private EquityResult MonteCarlo(HandIds[] heroHands, IReadOnlyList<HandIds> pool, int numOpponents, int[] boardIds, CancellationToken ct, WeightedHandRange? weightedRange = null)
    {
        var used = new bool[52];
        var touched = new int[52];
        Span<Card> buf = stackalloc Card[7];
        Span<int> oppScores = stackalloc int[numOpponents];
        Span<int> fullBoard = stackalloc int[5];

        double sum = 0, sumSq = 0;
        int n = 0;

        for (int i = 0; i < _iterations; i++)
        {
            if ((i & 63) == 0) ct.ThrowIfCancellationRequested();
            int t = 0;

            var hero = heroHands[_rng.Next(heroHands.Length)];
            Mark(used, touched, ref t, hero.A);
            Mark(used, touched, ref t, hero.B);
            foreach (var b in boardIds) Mark(used, touched, ref t, b);

            for (int o = 0; o < numOpponents; o++)
            {
                var opp = weightedRange == null ? SampleOpponent(pool, used) : SampleWeightedOpponent(weightedRange, used);
                Mark(used, touched, ref t, opp.A);
                Mark(used, touched, ref t, opp.B);
                _oppCache[o] = opp;
            }

            // Board vervollständigen
            for (int k = 0; k < boardIds.Length; k++) fullBoard[k] = boardIds[k];
            for (int k = boardIds.Length; k < 5; k++)
            {
                int id;
                do { id = _rng.Next(52); } while (used[id]);
                Mark(used, touched, ref t, id);
                fullBoard[k] = id;
            }
            for (int k = 0; k < 5; k++) buf[2 + k] = Deck.FromId(fullBoard[k]);

            buf[0] = Deck.FromId(hero.A);
            buf[1] = Deck.FromId(hero.B);
            int heroScore = HandEvaluator.Score(buf);

            int maxOpp = int.MinValue;
            for (int o = 0; o < numOpponents; o++)
            {
                buf[0] = Deck.FromId(_oppCache[o].A);
                buf[1] = Deck.FromId(_oppCache[o].B);
                int s = HandEvaluator.Score(buf);
                oppScores[o] = s;
                if (s > maxOpp) maxOpp = s;
            }

            double share = 0;
            if (heroScore > maxOpp) share = 1;
            else if (heroScore == maxOpp)
            {
                int tied = 1;
                for (int o = 0; o < numOpponents; o++) if (oppScores[o] == heroScore) tied++;
                share = 1.0 / tied;
            }

            sum += share;
            sumSq += share * share;
            n++;

            for (int k = 0; k < t; k++) used[touched[k]] = false;

            if (_targetHalfWidth > 0 && n >= _minIterations && n % 100 == 0 &&
                1.96 * StdError(sum, sumSq, n) <= _targetHalfWidth)
                break;
        }

        return new EquityResult(sum / n, StdError(sum, sumSq, n), n);
    }

    private readonly HandIds[] _oppCache = new HandIds[9];

    private static void Mark(bool[] used, int[] touched, ref int t, int id)
    {
        used[id] = true;
        touched[t++] = id;
    }

    private static double StdError(double sum, double sumSq, int n)
    {
        if (n < 2) return 0;
        double mean = sum / n;
        double variance = Math.Max(0, (sumSq - n * mean * mean) / (n - 1));
        return Math.Sqrt(variance / n);
    }

    /// <summary>
    /// Zieht eine konfliktfreie Gegnerhand. Rejection-Sampling ist bei gleichverteilter Auswahl
    /// unverzerrt (bedingte Gleichverteilung); als Fallback wird exakt über gültige Hände gewählt.
    /// </summary>
    private HandIds SampleOpponent(IReadOnlyList<HandIds> pool, bool[] used)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var pick = pool[_rng.Next(pool.Count)];
            if (!used[pick.A] && !used[pick.B]) return pick;
        }

        int valid = 0;
        for (int i = 0; i < pool.Count; i++)
            if (!used[pool[i].A] && !used[pool[i].B]) valid++;
        if (valid == 0)
            throw new InvalidOperationException("Gegnerrange ist durch bekannte Karten vollständig blockiert.");

        int target = _rng.Next(valid);
        for (int i = 0; i < pool.Count; i++)
        {
            if (used[pool[i].A] || used[pool[i].B]) continue;
            if (target-- == 0) return pool[i];
        }
        throw new InvalidOperationException("Unerreichbar.");
    }

    private HandIds SampleWeightedOpponent(WeightedHandRange range, bool[] used)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            double target = _rng.NextDouble() * range.TotalWeight;
            int index = Array.BinarySearch(range.CumulativeWeights, target);
            if (index < 0) index = ~index;
            var hand = range.Hands[Math.Min(index, range.Hands.Length - 1)];
            if (!used[hand.A] && !used[hand.B]) return hand;
        }

        double total = 0;
        for (int i = 0; i < range.Hands.Length; i++)
        {
            var hand = range.Hands[i];
            if (!used[hand.A] && !used[hand.B])
                total += range.CumulativeWeights[i] - (i == 0 ? 0 : range.CumulativeWeights[i - 1]);
        }
        if (total <= 0) throw new InvalidOperationException("Die gewichtete Gegnerrange ist durch bekannte Karten vollständig blockiert.");

        double remaining = _rng.NextDouble() * total;
        HandIds? lastValid = null;
        for (int i = 0; i < range.Hands.Length; i++)
        {
            var hand = range.Hands[i];
            if (used[hand.A] || used[hand.B]) continue;
            lastValid = hand;
            remaining -= range.CumulativeWeights[i] - (i == 0 ? 0 : range.CumulativeWeights[i - 1]);
            if (remaining < 0) return hand;
        }
        return lastValid!.Value;
    }

    /// <summary>Exakte Equity bei komplettem Board: Mittelwert über alle Hero-Farbvarianten und alle Gegnerhände.</summary>
    private static EquityResult ExactHeadsUp(HandIds[] heroHands, IReadOnlyList<HandIds> pool, int[] boardIds)
    {
        Span<Card> heroBuf = stackalloc Card[7];
        Span<Card> oppBuf = stackalloc Card[7];
        for (int k = 0; k < 5; k++)
        {
            heroBuf[2 + k] = Deck.FromId(boardIds[k]);
            oppBuf[2 + k] = heroBuf[2 + k];
        }

        double equitySum = 0;
        int heroCount = 0;
        int comparisons = 0;

        foreach (var hero in heroHands)
        {
            heroBuf[0] = Deck.FromId(hero.A);
            heroBuf[1] = Deck.FromId(hero.B);
            int heroScore = HandEvaluator.Score(heroBuf);

            double handSum = 0;
            int valid = 0;
            foreach (var opp in pool)
            {
                if (opp.A == hero.A || opp.A == hero.B || opp.B == hero.A || opp.B == hero.B) continue;
                if (Array.IndexOf(boardIds, opp.A) >= 0 || Array.IndexOf(boardIds, opp.B) >= 0) continue;
                oppBuf[0] = Deck.FromId(opp.A);
                oppBuf[1] = Deck.FromId(opp.B);
                int oppScore = HandEvaluator.Score(oppBuf);
                handSum += heroScore > oppScore ? 1 : heroScore == oppScore ? 0.5 : 0;
                valid++;
            }

            if (valid == 0) continue;
            equitySum += handSum / valid;
            comparisons += valid;
            heroCount++;
        }

        if (heroCount == 0)
            throw new InvalidOperationException("Keine gültige Gegnerhand für diese Hand/Board-Kombination.");
        return new EquityResult(equitySum / heroCount, 0, comparisons, IsExact: true);
    }
}
