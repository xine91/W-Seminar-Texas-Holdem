using System;
using System.Collections.Generic;
using System.Linq;
using PokerSimPlatform.Core.Engine;

namespace PokerSimPlatform.Core.Simulation;

public sealed record ParsedSimulationInput(
    IReadOnlyList<string> Hands,
    IReadOnlyList<string> OpponentRange,
    IReadOnlyList<Card> Board);

public sealed record InputError(string Field, string Message);

public sealed record InputValidationResult(ParsedSimulationInput? Parsed, IReadOnlyList<InputError> Errors)
{
    public bool IsValid => Errors.Count == 0 && Parsed != null;
}

/// <summary>
/// Parst und validiert Notationen (Handfilter, Gegnerrange, Board) und sammelt ALLE Fehler,
/// damit der Benutzer nicht Fehler für Fehler korrigieren muss. Notationen werden kanonisiert
/// (z. B. "kas" → "AKs") und dedupliziert.
/// </summary>
public static class SimulationInputValidator
{
    public static readonly IReadOnlyList<string> DefaultOpponentRange = new[]
    {
        "AA","KK","QQ","JJ","TT","99","88","77","66","55",
        "AKs","AQs","AJs","ATs","KQs","KJs","QJs",
        "AKo","AQo","AJo","KQo"
    };

    public static InputValidationResult Validate(
        string? handFilter, string? opponentRange, string? board,
        string phase, int tableSize, int numOpponents)
    {
        var errors = new List<InputError>();

        var hands = ParseNotations(handFilter, "Input.HandFilter", "Handfilter", errors)
                    ?? StartingHandNotation.All169().ToList();
        var range = ParseNotations(opponentRange, "Input.OpponentRange", "Gegnerrange", errors)
                    ?? DefaultOpponentRange.ToList();

        if (numOpponents < 1 || numOpponents > Math.Min(8, tableSize - 1))
            errors.Add(new InputError("Input.NumOpponents", $"Anzahl Gegner muss zwischen 1 und {Math.Min(8, tableSize - 1)} liegen."));

        var boardCards = ParseBoard(board, errors);
        int count = boardCards.Count;
        switch (phase)
        {
            case "Preflop":
                if (count != 0) errors.Add(new InputError("Input.Board", "Preflop erlaubt kein Board."));
                break;
            case "Flop":
                if (count != 0 && count != 3)
                    errors.Add(new InputError("Input.Board", $"Flop braucht 3 Karten (oder leer für zufälligen Flop), gefunden: {count}."));
                break;
            case "Endgame":
                if (count != 5)
                    errors.Add(new InputError("Input.Board", $"Endgame braucht genau 5 Karten, gefunden: {count}."));
                break;
            default:
                errors.Add(new InputError("Input.Phase", $"Unbekannte Phase '{phase}'."));
                break;
        }

        return errors.Count > 0
            ? new InputValidationResult(null, errors)
            : new InputValidationResult(new ParsedSimulationInput(hands, range, boardCards), errors);
    }

    private static List<string>? ParseNotations(string? text, string field, string label, List<InputError> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var result = new List<string>();
        var tokens = text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            try
            {
                var normalized = StartingHandNotation.Normalize(token);
                if (!result.Contains(normalized)) result.Add(normalized);
            }
            catch (ArgumentException ex)
            {
                errors.Add(new InputError(field, $"{label}: ungültige Notation '{token}' – {ex.Message}"));
            }
        }
        return result;
    }

    public static List<Card> ParseBoard(string? board, List<InputError> errors)
    {
        var cards = new List<Card>();
        if (string.IsNullOrWhiteSpace(board)) return cards;

        var compact = new string(board.Where(ch => !char.IsWhiteSpace(ch) && ch != ',').ToArray());
        if (compact.Length % 2 != 0)
        {
            errors.Add(new InputError("Input.Board", "Board muss aus Paaren Rang+Farbe bestehen (z. B. AsKhQd)."));
            return cards;
        }

        for (int i = 0; i < compact.Length; i += 2)
        {
            var text = compact.Substring(i, 2);
            try { cards.Add(Card.Parse(text)); }
            catch (ArgumentException ex)
            {
                errors.Add(new InputError("Input.Board", $"Ungültige Karte '{text}': {ex.Message}"));
            }
        }

        if (cards.Distinct().Count() != cards.Count)
            errors.Add(new InputError("Input.Board", "Board enthält doppelte Karten."));
        return cards;
    }
}
