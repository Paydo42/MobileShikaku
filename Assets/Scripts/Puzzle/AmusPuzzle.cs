using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An Amus (wire-connect) level, authored as a grid of symbols:
///
///   .  or 0   open cell
///   #  or X   blocked wall
///   A..Z      endpoint of a colour pair; each letter must appear exactly twice
///
/// One row per line, top row first, cells separated by spaces. The player drags
/// a wire between the two endpoints of each letter; wires may not share cells.
/// For the classic look put one endpoint of each pair in the left column and
/// the other in the right column, but any two cells work.
/// </summary>
[CreateAssetMenu(fileName = "AmusLevel", menuName = "Shikaku/Amus Puzzle")]
public class AmusPuzzle : PuzzleLevel
{
    [Tooltip("Require every open cell to be covered by a wire, not just all pairs connected.")]
    public bool mustFillBoard = false;

    [Min(0f)]
    [Tooltip("Seconds allowed to solve this level. 0 = no time limit.")]
    public float timeLimitSeconds = 0f;

    [TextArea(3, 20)]
    [Tooltip("Symbols: . open, # wall, letters = endpoint pairs (each letter exactly twice). Top row first.")]
    public string grid =
        "A . . B\n" +
        "B . . A\n" +
        "C . . D\n" +
        "D . . C";

    /// <summary>One colour pair: the letter that marks it and its two endpoint cells.</summary>
    public struct Pair
    {
        public string symbol;
        public Vector2Int a;
        public Vector2Int b;
    }

    /// <summary>
    /// Parses <see cref="grid"/> into a wall map plus the endpoint pairs, in
    /// first-appearance order. Coordinates are (x = column, y = row) with y = 0
    /// at bottom. A symbol that doesn't appear exactly twice is logged and
    /// dropped, so a typo in the text can't break the board.
    /// </summary>
    public List<Pair> Parse(out bool[,] blocked, out int width, out int height)
    {
        var rows = new List<string[]>();
        foreach (string rawLine in grid.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            rows.Add(line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        height = Mathf.Max(1, rows.Count);
        width = 1;
        foreach (var row in rows) width = Mathf.Max(width, row.Length);

        blocked = new bool[width, height];
        var endpoints = new Dictionary<string, List<Vector2Int>>();
        var symbolOrder = new List<string>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            int y = rows.Count - 1 - rowIndex; // first text line = top of board
            string[] tokens = rows[rowIndex];
            for (int x = 0; x < tokens.Length; x++)
            {
                string token = tokens[x].ToUpperInvariant();
                switch (token)
                {
                    case ".":
                    case "0":
                        break;
                    case "#":
                    case "X":
                        blocked[x, y] = true;
                        break;
                    default:
                        if (!endpoints.TryGetValue(token, out List<Vector2Int> cells))
                        {
                            cells = new List<Vector2Int>();
                            endpoints.Add(token, cells);
                            symbolOrder.Add(token);
                        }
                        cells.Add(new Vector2Int(x, y));
                        break;
                }
            }
        }

        var pairs = new List<Pair>();
        foreach (string symbol in symbolOrder)
        {
            List<Vector2Int> cells = endpoints[symbol];
            if (cells.Count != 2)
            {
                Debug.LogError(
                    $"AmusPuzzle '{name}': symbol '{symbol}' appears {cells.Count} time(s), needs exactly 2. Skipped.",
                    this);
                continue;
            }
            pairs.Add(new Pair { symbol = symbol, a = cells[0], b = cells[1] });
        }

        return pairs;
    }
}
