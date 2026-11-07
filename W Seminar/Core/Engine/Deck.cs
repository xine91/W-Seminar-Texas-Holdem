using System;
using System.Collections.Generic;

namespace PokerSimPlatform.Core.Engine;

/// <summary>
/// 52-Karten-Deck auf Basis eines int-Arrays (Id = Suit * 13 + (Rank - 2)).
/// Gezogen wird per partiellem Fisher-Yates (nur so viele Swaps wie Karten gezogen werden).
/// Alle Zufallsquellen sind über Seed bzw. übergebenes Random deterministisch.
/// </summary>
public sealed class Deck
{
    private readonly int[] _cards = new int[52];
    private readonly Random _rng;
    private int _count = 52;   // Anzahl noch vorhandener (nicht entfernter) Karten
    private int _next;         // Anzahl bereits gezogener Karten (Präfix des Arrays)

    public Deck(int? seed = null) : this(seed.HasValue ? new Random(seed.Value) : new Random()) { }

    public Deck(Random rng)
    {
        _rng = rng;
        Reset();
    }

    public static int ToId(Card c) => c.Suit * 13 + (c.Rank - 2);
    public static Card FromId(int id) => new(id / 13, id % 13 + 2);

    /// <summary>Stellt das volle Deck wieder her (inkl. entfernter Karten).</summary>
    public void Reset()
    {
        for (int i = 0; i < 52; i++) _cards[i] = i;
        _count = 52;
        _next = 0;
    }

    /// <summary>Mischt neu; mit Seed deterministisch.</summary>
    public void Shuffle(int? seed = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : _rng;
        for (int i = _count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
        _next = 0;
    }

    /// <summary>Zieht eine zufällige Karte (partielles Fisher-Yates).</summary>
    public Card Draw()
    {
        if (_next >= _count)
            throw new InvalidOperationException("Deck ist leer.");
        int j = _next + _rng.Next(_count - _next);
        (_cards[_next], _cards[j]) = (_cards[j], _cards[_next]);
        return FromId(_cards[_next++]);
    }

    public int Remaining => _count - _next;

    public IReadOnlyList<Card> RemainingCards
    {
        get
        {
            var list = new List<Card>(Remaining);
            for (int i = _next; i < _count; i++) list.Add(FromId(_cards[i]));
            return list;
        }
    }

    /// <summary>Entfernt bestimmte Karten (z. B. bekannte Handkarten). Setzt den Ziehzeiger zurück.</summary>
    public void RemoveCards(IEnumerable<Card> cardsToRemove)
    {
        foreach (var card in cardsToRemove)
        {
            int id = ToId(card);
            for (int i = 0; i < _count; i++)
            {
                if (_cards[i] != id) continue;
                _cards[i] = _cards[_count - 1];
                _count--;
                break;
            }
        }
        _next = 0;
    }
}
