using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Shikaku level, authored as a grid of numbers. Create one via
/// Create > Shikaku > Puzzle, then type the board into the Grid field:
///
///   * One row per line, the TOP row first (as you'd see it on screen).
///   * A number is a clue; 0 or . is an empty cell.
///   * Separate cells with spaces (commas/tabs also work).
///
/// Example (5x5):
///   0 0 3 0 0
///   4 0 3 0 0
///   0 0 0 2 0
///   4 0 2 0 0
///   3 0 0 4 0
///
/// For a solvable puzzle the clue numbers must sum to the number of cells.
/// </summary>
[CreateAssetMenu(fileName = "Level", menuName = "Shikaku/Puzzle")]
public class ShikakuPuzzle : ScriptableObject
{
    [Tooltip("Shown on the level-select button. Falls back to 'Level N' if empty.")]
    public string levelName = "New Level";

    [TextArea(3, 20)]
    [Tooltip("One row per line, top row first. Numbers = clues, 0 or . = empty.")]
    public string grid =
        "0 0 3 0 0\n" +
        "4 0 3 0 0\n" +
        "0 0 0 2 0\n" +
        "4 0 2 0 0\n" +
        "3 0 0 4 0";

    /// <summary>
    /// Parses <see cref="grid"/> into a [width, height] array of clue values
    /// (0 = no clue), with (x = column, y = row) and y = 0 at the bottom.
    /// </summary>
    public int[,] BuildClueGrid()
    {
        var rows = new List<string[]>();
        foreach (string rawLine in grid.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            rows.Add(line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        int height = Mathf.Max(1, rows.Count);
        int width = 1;
        foreach (var row in rows) width = Mathf.Max(width, row.Length);

        var result = new int[width, height];
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            // First text line is the top of the board, i.e. the highest y.
            int y = rows.Count - 1 - rowIndex;
            string[] tokens = rows[rowIndex];
            for (int x = 0; x < tokens.Length; x++)
            {
                if (int.TryParse(tokens[x], out int value) && value > 0)
                    result[x, y] = value;
            }
        }
        return result;
    }

    public int Width => BuildClueGrid().GetLength(0);
    public int Height => BuildClueGrid().GetLength(1);

    /// <summary>Built-in fallback level used when no database/level is available.</summary>
    public static ShikakuPuzzle CreateSample()
    {
        var p = CreateInstance<ShikakuPuzzle>();
        p.levelName = "Sample";
        return p; // default grid above is a valid, solvable 5x5
    }
}
