using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Shikaku > Amus > Check Levels (fill every tile): every Amus level should
/// need every open tile filled, with no wire touching itself (the game's
/// rule; see <see cref="AmusPuzzle.TouchesItself"/>). Per level:
///   * can be filled   -> Must Fill Board on, the answer baked into the level
///                        for the Lock helper, and a check for a second answer
///                        (Puzzle Baron levels have exactly one);
///   * can't be filled -> listed as BROKEN, saying whether the no-touching
///                        rule is what stops it, and kept connect-only (with
///                        a connect-only answer baked) so it stays winnable
///                        until it's fixed;
///   * grid typos      -> listed as BROKEN: rows of different lengths, and
///                        symbols that don't appear exactly twice (the game
///                        drops those pairs).
/// Every solve has a time limit, and Cancel on the progress bar works mid-solve.
/// Every baked answer is re-read and checked before it's kept.
/// Edit > Undo reverts the whole run.
/// </summary>
public static class AmusLevelTools
{
    private const float FillSeconds = 20f;
    private const float SecondAnswerSeconds = 10f;
    private const string Title = "Checking Amus levels";

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

        int filled = 0, checkedCount = 0;
        var broken = new List<string>();
        var secondAnswer = new List<string>();
        var report = new StringBuilder();
        bool cancelled = false;
        try
        {
            for (int i = 0; i < levels.Count && !cancelled; i++)
            {
                AmusPuzzle level = levels[i];
                var job = new Job(level, $"{level.name} ({i + 1}/{levels.Count})", i / (float)levels.Count);
                string typo = FindTypos(level);
                var watch = Stopwatch.StartNew();
                string result;
                bool isBroken = typo.Length > 0;

                switch (job.Run(true, null, FillSeconds, "fill check", ref cancelled, out int[][] answer))
                {
                    case AmusSolver.Outcome.Solved:
                        if (!job.Bake(answer, true))
                        {
                            isBroken = true;
                            result = "ERROR: the solver's answer failed its own check (left as is)";
                            break;
                        }
                        level.mustFillBoard = true;
                        filled++;
                        switch (job.Run(true, answer, SecondAnswerSeconds, "one answer?", ref cancelled, out _))
                        {
                            case AmusSolver.Outcome.NoSolution:
                                result = "OK: fills every tile, exactly one answer";
                                break;
                            case AmusSolver.Outcome.Solved:
                                result = "OK: fills every tile, but has 2+ answers";
                                secondAnswer.Add(level.name);
                                break;
                            default:
                                result = "OK: fills every tile (second-answer check ran out of time)";
                                break;
                        }
                        break;

                    case AmusSolver.Outcome.NoSolution:
                        isBroken = true;

                        // Is it the no-touching rule that rules it out?
                        string cause = job.Run(true, null, FillSeconds, "touching check", ref cancelled, out _, true)
                            == AmusSolver.Outcome.Solved
                            ? "every way to fill it has a wire running alongside itself"
                            : "can't fill every tile";

                        switch (job.Run(false, null, FillSeconds, "connect check", ref cancelled, out int[][] connected))
                        {
                            case AmusSolver.Outcome.Solved when job.Bake(connected, false):
                                level.mustFillBoard = false;
                                result = $"BROKEN: {cause} (kept connect-only until fixed)";
                                break;
                            case AmusSolver.Outcome.NoSolution:
                                level.solution = "";
                                result = $"BROKEN: {cause}, and the pairs can't even all be connected without a wire touching itself (unwinnable)";
                                break;
                            default:
                                result = $"BROKEN: {cause} (connect check ran out of time; left as is)";
                                break;
                        }
                        break;

                    default:
                        if (cancelled) continue; // stopped before this level was decided; the loop ends
                        isBroken = true;
                        result = $"BROKEN?: couldn't decide within {FillSeconds:0} s whether it can be filled (left as is)";
                        break;
                }

                checkedCount++;
                if (isBroken)
                {
                    string why = result.StartsWith("OK") ? "grid typo" : result.Replace("BROKEN: ", "").Replace("BROKEN?: ", "");
                    broken.Add($"  {level.name}: {why}{typo}");
                }
                EditorUtility.SetDirty(level);
                report.AppendLine($"{level.name}: {result}   [{job.Width}x{job.Height}, {job.PairCount} pairs, {watch.ElapsedMilliseconds} ms]{typo}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        var log = new StringBuilder();
        log.AppendLine($"[AmusLevelTools] {filled} of {levels.Count} levels fill every tile; {broken.Count} broken.");
        if (cancelled) log.AppendLine($"Cancelled after {checkedCount} levels; the rest were not checked.");
        log.AppendLine();
        if (broken.Count > 0)
        {
            log.AppendLine("BROKEN, fix these and run Check Levels again:");
            foreach (string line in broken) log.AppendLine(line);
            log.AppendLine();
        }
        if (secondAnswer.Count > 0)
        {
            log.AppendLine("Playable, but with 2+ answers (easier than Puzzle Baron levels, which have one; " +
                           "Shikaku > Amus > Level Generator makes one-answer levels):");
            log.AppendLine("  " + string.Join(", ", secondAnswer));
            log.AppendLine();
        }
        log.AppendLine("All levels:");
        log.Append(report);
        Debug.Log(log.ToString());

        string names = broken.Count == 0
            ? "None."
            : string.Join(", ", broken.Select(line => line.Trim().Split(':')[0]));
        EditorUtility.DisplayDialog("Amus levels checked",
            $"{filled} of {levels.Count} levels fill every tile.\n\n" +
            $"Broken ({broken.Count}): {names}\n\n" +
            $"{secondAnswer.Count} levels have more than one answer.\n" +
            (cancelled ? "\nCancelled part-way; later levels were not checked.\n" : "") +
            "\nThe reasons are in the Console.", "OK");
    }

    // One level's grid in the solver's flat form, plus the solving and baking.
    private sealed class Job
    {
        public readonly int Width;
        public readonly int Height;
        public int PairCount => _pairs.Count;

        private readonly AmusPuzzle _level;
        private readonly List<AmusPuzzle.Pair> _pairs;
        private readonly bool[,] _blocked;
        private readonly bool[] _flatBlocked;
        private readonly int[] _endpoints;
        private readonly string _label;
        private readonly float _progress;

        public Job(AmusPuzzle level, string label, float progress)
        {
            _level = level;
            _label = label;
            _progress = progress;
            _pairs = level.Parse(out _blocked, out Width, out Height);

            _flatBlocked = new bool[Width * Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _flatBlocked[y * Width + x] = _blocked[x, y];

            _endpoints = new int[_pairs.Count * 2];
            for (int p = 0; p < _pairs.Count; p++)
            {
                _endpoints[p * 2] = _pairs[p].a.y * Width + _pairs[p].a.x;
                _endpoints[p * 2 + 1] = _pairs[p].b.y * Width + _pairs[p].b.x;
            }
        }

        // Solves on a worker thread while the progress bar stays live, so
        // Cancel works and the editor never looks frozen. 'allowSelfTouch'
        // lifts the no-touching rule, for explaining a level with no answer.
        public AmusSolver.Outcome Run(bool fill, int[][] exclude, float seconds, string step, ref bool cancelled,
            out int[][] wires, bool allowSelfTouch = false)
        {
            wires = null;
            if (cancelled) return AmusSolver.Outcome.GaveUp;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            CancellationToken token = timeout.Token;
            int width = Width, height = Height;
            bool[] blocked = _flatBlocked;
            int[] endpoints = _endpoints;
            Task<(AmusSolver.Outcome outcome, int[][] wires)> task = Task.Run(() =>
            {
                AmusSolver.Outcome outcome = AmusSolver.Solve(width, height, blocked, endpoints, fill,
                    int.MaxValue, out int[][] found, out _, exclude, token, allowSelfTouch);
                return (outcome, found);
            });

            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                string info = $"{_label}: {step}, {watch.Elapsed.TotalSeconds:0} s";
                if (EditorUtility.DisplayCancelableProgressBar(Title, info, _progress) && !cancelled)
                {
                    cancelled = true;
                    timeout.Cancel();
                }
                Thread.Sleep(30);
            }

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception, _level);
                return AmusSolver.Outcome.GaveUp;
            }
            wires = task.Result.wires;
            return task.Result.outcome;
        }

        // Writes the answer into the level, after reading it back and checking
        // it against the grid so a bad answer can never be baked in.
        public bool Bake(int[][] cells, bool fill)
        {
            var wires = new List<Vector2Int>[_pairs.Count];
            for (int p = 0; p < _pairs.Count; p++)
            {
                wires[p] = new List<Vector2Int>(cells[p].Length);
                foreach (int cell in cells[p]) wires[p].Add(new Vector2Int(cell % Width, cell / Width));
            }

            string previous = _level.solution;
            _level.solution = AmusPuzzle.EncodeSolution(_pairs, wires);
            if (_level.TryDecodeSolution(_pairs, _blocked, Width, Height, fill, out _)) return true;

            _level.solution = previous;
            Debug.LogError($"[AmusLevelTools] {_level.name}: the solver's answer failed its own check; not baked.", _level);
            return false;
        }
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

        return notes.Count == 0 ? "" : "\n      TYPO: " + string.Join("; ", notes);
    }
}
