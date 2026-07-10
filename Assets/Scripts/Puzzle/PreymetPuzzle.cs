using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Preymet (path-tracing) level, authored as a grid of symbols:
///
///   .  or 0   walkable cell
///   #  or X   blocked wall
///   S         start cell
///   G         goal cell
///
/// One row per line, top row first, cells separated by spaces. The player must
/// trace a path from S to G through orthogonally-adjacent walkable cells, never
/// reusing a cell, using exactly <see cref="targetTiles"/> tiles in total.
/// </summary>
[CreateAssetMenu(fileName = "PreymetLevel", menuName = "Shikaku/Preymet Puzzle")]
public class PreymetPuzzle : PuzzleLevel
{
    [Min(2)]
    [Tooltip("Exact number of tiles the path must use, including start and goal.")]
    public int targetTiles = 7;

    [Min(0f)]
    [Tooltip("Seconds allowed to solve this level. 0 = no time limit.")]
    public float timeLimitSeconds = 60f;

    [TextArea(3, 20)]
    [Tooltip("Symbols: . walkable, # wall, S start, G goal. Top row first.")]
    public string grid =
        "S . . .\n" +
        ". . . .\n" +
        ". . . .\n" +
        ". . . G";

    /// <summary>
    /// Parses <see cref="grid"/> into a walkable/blocked map plus the start and
    /// goal cells. Coordinates are (x = column, y = row) with y = 0 at bottom.
    /// </summary>
    public void Parse(out bool[,] blocked, out Vector2Int start, out Vector2Int goal,
        out int width, out int height)
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
        start = Vector2Int.zero;
        goal = new Vector2Int(width - 1, height - 1);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            int y = rows.Count - 1 - rowIndex; // first text line = top of board
            string[] tokens = rows[rowIndex];
            for (int x = 0; x < tokens.Length; x++)
            {
                switch (tokens[x].ToUpperInvariant())
                {
                    case "#":
                    case "X":
                        blocked[x, y] = true;
                        break;
                    case "S":
                        start = new Vector2Int(x, y);
                        break;
                    case "G":
                        goal = new Vector2Int(x, y);
                        break;
                    // "." / "0" / anything else => walkable
                }
            }
        }
    }
}
