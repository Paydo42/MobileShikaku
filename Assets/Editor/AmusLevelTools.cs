using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Shikaku > Amus > Check Levels (fill every tile): solves every Amus level
/// with the fill rule (every open tile covered by a wire) and, per level:
///   * can be filled   -> Must Fill Board on, and the solution is baked into the
///                        level for the Lock helper;
///   * can't be filled -> left connect-only (Must Fill Board off) so it stays
///                        winnable, with a connect-only solution baked;
///   * unsolvable, or too hard to decide in the budget -> reported, left alone.
/// Every baked solution is re-read and checked before it's kept. Also flags grid
/// typos: rows of different lengths, and symbols that don't appear exactly
/// twice (the game drops those pairs). Edit > Undo reverts the whole run.
/// </summary>
public static class AmusLevelTools
{
    private const int ConflictBudget = 2000000;

    [MenuItem("Shikaku/Amus/Check Levels (fill every tile)")]
    private static void CheckLevels()
    {
        List<AmusPuzzle> levels = AssetDatabase.FindAssets("t:AmusPuzzle")
            .Select(guid => AssetDatabase.LoadAssetAtPath<AmusPuzzle>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(level => level != null)
            .ToList();
        levels.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        if (levels.Count == 0)
        {
            EditorUtility.DisplayDialog("Check Amus Levels", "No Amus levels found.", "OK");
            return;
        }

        Undo.RecordObjects(levels.ToArray(), "Check Amus Levels");

        int fill = 0, connectOnly = 0, unsolvable = 0, undecided = 0, typos = 0;
        var report = new StringBuilder();
        bool cancelled = false;
        try
        {
            for (int i = 0; i < levels.Count; i++)
            {
                AmusPuzzle level = levels[i];
                if (EditorUtility.DisplayCancelableProgressBar("Checking Amus levels",
                        $"{level.name} ({i + 1}/{levels.Count})", i / (float)levels.Count))
                {
                    cancelled = true;
                    break;
                }

                string typo = FindTypos(level);
                if (typo.Length > 0) typos++;

                List<AmusPuzzle.Pair> pairs = level.Parse(out bool[,] blocked, out int width, out int height);
                var watch = Stopwatch.StartNew();
                string result;

                switch (Solve(level, pairs, blocked, width, height, true, out string solution))
                {
                    case AmusSolver.Outcome.Solved:
                        level.mustFillBoard = true;
                        level.solution = solution;
                        fill++;
                        result = "fill every tile: OK";
                        break;

                    case AmusSolver.Outcome.NoSolution:
                        switch (Solve(level, pairs, blocked, width, height, false, out solution))
                        {
                            case AmusSolver.Outcome.Solved:
                                level.mustFillBoard = false;
                                level.solution = solution;
                                connectOnly++;
                                result = "CAN'T BE FILLED: kept connect-only so it stays winnable";
                                break;
                            case AmusSolver.Outcome.NoSolution:
                                level.solution = "";
                                unsolvable++;
                                result = "UNSOLVABLE: even just connecting the pairs is impossible";
                                break;
                            default:
                                undecided++;
                                result = "can't be filled; the connect-only check ran out of time (left as is)";
                                break;
                        }
                        break;

                    default:
                        undecided++;
                        result = "ran out of time deciding (left as is; the game solves it at runtime)";
                        break;
                }

                EditorUtility.SetDirty(level);
                report.AppendLine($"{level.name}: {result}   [{width}x{height}, {pairs.Count} pairs, {watch.ElapsedMilliseconds} ms]{typo}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        string summary =
            $"{fill} levels now require filling every tile.\n" +
            $"{connectOnly} can't be filled, so they stay connect-only.\n" +
            $"{unsolvable} are unsolvable, {undecided} undecided.\n" +
            $"{typos} have likely typos in their grid." +
            (cancelled ? "\n\nCancelled part-way; levels after that were not checked." : "");
        Debug.Log($"[AmusLevelTools] {summary}\n\n{report}");
        EditorUtility.DisplayDialog("Amus levels checked", summary + "\n\nThe full per-level list is in the Console.", "OK");
    }

    // Solves one level and returns its solution text, re-read and checked
    // against the grid so a bad answer can never be baked in.
    private static AmusSolver.Outcome Solve(AmusPuzzle level, List<AmusPuzzle.Pair> pairs, bool[,] blocked,
        int width, int height, bool fill, out string solution)
    {
        solution = "";

        var flat = new bool[width * height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                flat[y * width + x] = blocked[x, y];

        var endpoints = new int[pairs.Count * 2];
        for (int p = 0; p < pairs.Count; p++)
        {
            endpoints[p * 2] = pairs[p].a.y * width + pairs[p].a.x;
            endpoints[p * 2 + 1] = pairs[p].b.y * width + pairs[p].b.x;
        }

        AmusSolver.Outcome outcome = AmusSolver.Solve(width, height, flat, endpoints, fill, ConflictBudget,
            out int[][] cells, out _);
        if (outcome != AmusSolver.Outcome.Solved) return outcome;

        var wires = new List<Vector2Int>[pairs.Count];
        for (int p = 0; p < pairs.Count; p++)
        {
            wires[p] = new List<Vector2Int>(cells[p].Length);
            foreach (int cell in cells[p]) wires[p].Add(new Vector2Int(cell % width, cell / width));
        }

        string text = AmusPuzzle.EncodeSolution(pairs, wires);
        string previous = level.solution;
        level.solution = text;
        bool valid = level.TryDecodeSolution(pairs, blocked, width, height, fill, out _);
        level.solution = previous;

        if (!valid)
        {
            Debug.LogError($"[AmusLevelTools] {level.name}: the solver's answer failed its own check; not baked.", level);
            return AmusSolver.Outcome.GaveUp;
        }

        solution = text;
        return outcome;
    }

    // Grid text that the parser accepts but that is almost certainly a typo.
    private static string FindTypos(AmusPuzzle level)
    {
        var rows = level.grid.Replace("\r", "").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var notes = new List<string>();
        List<int> lengths = rows.Select(row => row.Length).Distinct().ToList();
        if (lengths.Count > 1)
            notes.Add($"rows have different lengths ({string.Join("/", lengths)}); short rows get extra open cells");

        var counts = new Dictionary<string, int>();
        foreach (string[] row in rows)
        {
            foreach (string token in row)
            {
                string symbol = token.ToUpperInvariant();
                if (symbol is "." or "0" or "#" or "X") continue;
                counts[symbol] = counts.TryGetValue(symbol, out int n) ? n + 1 : 1;
            }
        }
        foreach (KeyValuePair<string, int> entry in counts)
            if (entry.Value != 2)
                notes.Add($"'{entry.Key}' appears {entry.Value}x (needs exactly 2; that pair is dropped)");

        return notes.Count == 0 ? "" : "\n      TYPO? " + string.Join("; ", notes);
    }
}
