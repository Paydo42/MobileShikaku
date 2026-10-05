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

    [TextArea(2, 12)]
    [Tooltip("Written by Shikaku > Amus > Check Levels; used by the Lock helper. One line per pair: its symbol, ':', then steps R/L/U/D from its first endpoint. Empty = the game solves the level itself when it loads.")]
    public string solution = "";

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

    #region Solution text

    /// <summary>
    /// Writes wires (each from its pair's first endpoint <c>a</c> to <c>b</c>)
    /// in the <see cref="solution"/> format.
    /// </summary>
    public static string EncodeSolution(List<Pair> pairs, List<Vector2Int>[] wires)
    {
        var text = new System.Text.StringBuilder();
        for (int p = 0; p < pairs.Count; p++)
        {
            text.Append(pairs[p].symbol).Append(':');
            for (int i = 1; i < wires[p].Count; i++)
            {
                Vector2Int step = wires[p][i] - wires[p][i - 1];
                text.Append(step.x > 0 ? 'R' : step.x < 0 ? 'L' : step.y > 0 ? 'U' : 'D');
            }
            if (p < pairs.Count - 1) text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads <see cref="solution"/> back into one wire per pair (in
    /// <paramref name="pairs"/> order, each from <c>a</c> to <c>b</c>). False if
    /// it's empty or no longer fits the grid: a wire that leaves the board,
    /// crosses a wall or another wire, or misses its endpoint, a missing pair,
    /// or (with <paramref name="fill"/>) an open cell left uncovered.
    /// </summary>
    public bool TryDecodeSolution(List<Pair> pairs, bool[,] blocked, int width, int height, bool fill,
        out List<Vector2Int>[] wires)
    {
        wires = null;
        if (string.IsNullOrWhiteSpace(solution)) return false;

        var steps = new Dictionary<string, string>();
        foreach (string rawLine in solution.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            steps[line.Substring(0, colon).Trim().ToUpperInvariant()] = line.Substring(colon + 1).Trim();
        }

        var used = new bool[width, height];
        var result = new List<Vector2Int>[pairs.Count];
        for (int p = 0; p < pairs.Count; p++)
        {
            used[pairs[p].a.x, pairs[p].a.y] = true;
            used[pairs[p].b.x, pairs[p].b.y] = true;
        }

        for (int p = 0; p < pairs.Count; p++)
        {
            if (!steps.TryGetValue(pairs[p].symbol, out string path)) return false;

            var wire = new List<Vector2Int> { pairs[p].a };
            Vector2Int cell = pairs[p].a;
            for (int i = 0; i < path.Length; i++)
            {
                cell += path[i] switch
                {
                    'R' => Vector2Int.right,
                    'L' => Vector2Int.left,
                    'U' => Vector2Int.up,
                    'D' => Vector2Int.down,
                    _ => Vector2Int.zero,
                };
                bool last = i == path.Length - 1;
                if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height) return false;
                if (blocked[cell.x, cell.y]) return false;
                if (last ? cell != pairs[p].b : used[cell.x, cell.y]) return false;
                used[cell.x, cell.y] = true;
                wire.Add(cell);
            }
            if (wire[^1] != pairs[p].b) return false;
            result[p] = wire;
        }

        if (fill)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (!blocked[x, y] && !used[x, y]) return false;
        }

        wires = result;
        return true;
    }

    #endregion
}
