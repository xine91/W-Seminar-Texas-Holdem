using System;
using System.Collections.Generic;
using System.Linq;

namespace PokerSimPlatform.Core.Engine;

/// <summary>
/// Generiert die 169 Starthand-Kombinationen in standardisierter Poker-Notation.
/// Notation: "AKs" (suited), "72o" (offsuit), "TT" (Pair).
/// 
/// Aufbau:
/// - 13 Paare (22, 33, ..., AA)
/// - 78 Suited (A♠K♠, A♠Q♠, ..., 3♠2♠)
/// - 78 Offsuit (A♠K♣, A♠Q♣, ..., 3♠2♣)
/// Summe: 169
/// </summary>
public static class StartingHandNotation
{
    private static readonly char[] Ranks = { '2', '3', '4', '5', '6', '7', '8', '9', 'T', 'J', 'Q', 'K', 'A' };
    private const int MinRank = 2;
    private const int MaxRank = 14;

    /// <summary>
    /// Liefert alle 169 Starthand-Bezeichner in kanonischer Sortierung.
    /// Sortierung: Pairs zuerst (AA, KK, ..., 22), dann suited/offsuit nach Rang.
    /// </summary>
    public static IReadOnlyList<string> All169()
    {
        var list = new List<string>(169);
        // Pairs: 22, 33, ..., AA (absteigend)
        for (int r = MaxRank; r >= MinRank; r--)
        {
            list.Add($"{Ranks[r - MinRank]}{Ranks[r - MinRank]}");
        }
        // Suited & Offsuit: A-K, A-Q, ..., 3-2 (absteigend)
        for (int hi = MaxRank; hi > MinRank; hi--)
        {
            for (int lo = hi - 1; lo >= MinRank; lo--)
            {
                list.Add($"{Ranks[hi - MinRank]}{Ranks[lo - MinRank]}s");
                list.Add($"{Ranks[hi - MinRank]}{Ranks[lo - MinRank]}o");
            }
        }
        return list;
    }

    /// <summary>
    /// Bringt eine Notation in kanonische Form (höherer Rang zuerst, Großbuchstaben, s/o klein),
    /// z. B. "kas" → "AKs", "tt" → "TT". Wirft ArgumentException bei ungültiger Notation.
    /// </summary>
    public static string Normalize(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation))
            throw new ArgumentException("Leere Notation.", nameof(notation));
        notation = notation.Trim();
        if (notation.Length is not (2 or 3))
            throw new ArgumentException($"Ungültige Notation: '{notation}'", nameof(notation));

        int r1 = CharToRank(notation[0]);
        int r2 = CharToRank(notation[1]);
        if (notation.Length == 2)
        {
            if (r1 != r2) throw new ArgumentException($"'{notation}' ist kein gültiges Paar.", nameof(notation));
            return $"{Ranks[r1 - MinRank]}{Ranks[r1 - MinRank]}";
        }

        char suffix = char.ToLowerInvariant(notation[2]);
        if (suffix != 's' && suffix != 'o')
            throw new ArgumentException($"Notation muss mit 's' oder 'o' enden: '{notation}'", nameof(notation));
        if (r1 == r2)
            throw new ArgumentException($"'{notation}' ist ein Paar; Paare haben kein s/o-Suffix.", nameof(notation));

        int hi = Math.Max(r1, r2), lo = Math.Min(r1, r2);
        return $"{Ranks[hi - MinRank]}{Ranks[lo - MinRank]}{suffix}";
    }

    /// <summary>
    /// Parst eine Starthand-Notation in 1 oder 2 konkrete Karten-Tupel.
    /// Beispiel: "AKs" → [(A♠,K♠), (A♥,K♥), (A♦,K♦), (A♣,K♣)] (4 Kombinationen).
    /// </summary>
    public static IReadOnlyList<(Card Card1, Card Card2)> Expand(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation) || notation.Length < 2)
            throw new ArgumentException($"Ungültige Notation: '{notation}'", nameof(notation));

        notation = notation.Trim();
        if (notation.Length == 2)
        {
            // Pair
            int r1 = CharToRank(notation[0]);
            int r2 = CharToRank(notation[1]);
            if (r1 != r2)
                throw new ArgumentException($"'{notation}' ist kein gültiges Paar.");
            return ExpandPair(r1);
        }
        else
        {
            int r1 = CharToRank(notation[0]);
            int r2 = CharToRank(notation[1]);
            char suitSuffix = notation[2];
            bool suited = suitSuffix == 's' || suitSuffix == 'S';
            bool offsuit = suitSuffix == 'o' || suitSuffix == 'O';
            if (!suited && !offsuit)
                throw new ArgumentException($"Notation muss mit 's' oder 'o' enden: '{notation}'");
            if (r1 == r2)
                throw new ArgumentException($"'{notation}' ist ein Paar; Paare haben kein s/o-Suffix.");

            return suited ? ExpandSuited(r1, r2) : ExpandOffsuit(r1, r2);
        }
    }

    private static IReadOnlyList<(Card, Card)> ExpandPair(int rank)
    {
        // C(4,2) = 6 Kombinationen
        var result = new List<(Card, Card)>(6);
        for (int s1 = 0; s1 < 4; s1++)
            for (int s2 = s1 + 1; s2 < 4; s2++)
                result.Add((new Card(s1, rank), new Card(s2, rank)));
        return result;
    }

    private static IReadOnlyList<(Card, Card)> ExpandSuited(int high, int low)
    {
        // 4 Suits: beide Karten gleiche Farbe
        var result = new List<(Card, Card)>(4);
        for (int s = 0; s < 4; s++)
            result.Add((new Card(s, high), new Card(s, low)));
        return result;
    }

    private static IReadOnlyList<(Card, Card)> ExpandOffsuit(int high, int low)
    {
        // 4*3 = 12 Kombinationen (verschiedene Farben)
        var result = new List<(Card, Card)>(12);
        for (int s1 = 0; s1 < 4; s1++)
            for (int s2 = 0; s2 < 4; s2++)
                if (s1 != s2)
                    result.Add((new Card(s1, high), new Card(s2, low)));
        return result;
    }

    private static int CharToRank(char c) => c switch
    {
        '2' => 2, '3' => 3, '4' => 4, '5' => 5, '6' => 6,
        '7' => 7, '8' => 8, '9' => 9, 'T' => 10, 't' => 10,
        'J' => 11, 'j' => 11, 'Q' => 12, 'q' => 12,
        'K' => 13, 'k' => 13, 'A' => 14, 'a' => 14,
        _ => throw new ArgumentException($"Ungültiger Rangcharakter: '{c}'")
    };
}
