using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

/// <summary>
/// Makes Puzzle Baron-style Amus levels: the wires fill every open tile, wind
/// around each other instead of running straight, and there is exactly one
/// answer. Plain C# with no Unity types, so it runs on a worker thread.
///
/// How it works:
///   1. Tile the board with dominoes (two-cell wires).
///   2. Join wires whose ends touch until the wanted number of pairs is left.
///      A wire may never touch itself: one running right beside its own
///      earlier part leaves room for a shortcut, which means a second answer.
///      When no join fits, a wire end re-routes by taking over part of a
///      neighbouring wire, which opens up new joins.
///   3. Ask <see cref="AmusSolver"/> for any other answer. None means the level
///      is unique; if there is one, cut a wire where the two answers differ
///      (one more pair) and ask again.
/// Among the unique levels found in the time given, the one closest to the
/// wanted pair count wins, then the one whose wires wander furthest from
/// straight routes.
/// </summary>
public static class AmusLevelGenerator
{
    // Pair symbols in reading order. No X (it means wall) and no 0 (open).
    private static readonly string[] Symbols =
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "Y", "Z",
        "1", "2", "3", "4", "5", "6", "7", "8", "9",
    };

    /// <summary>Most pairs a level can have (one symbol each).</summary>
    public static int MaxPairs => Symbols.Length;

    private const int MinWireCells = 3;          // no pair whose dots touch
    private const int UniqueCheckBudget = 30000; // solver conflicts per "is there another answer?" check
    private const int MaxRepairs = 4;            // cuts tried on one layout before starting over
    private const int EnoughCandidates = 12;     // stop early after this many unique levels
    private const int RewiresPerOpenCell = 4;    // re-route budget per layout, scaled by board size
    private const int RewiresBetweenScans = 8;   // re-routes tried before looking for joins again

    /// <summary>A finished level.</summary>
    public sealed class Level
    {
        public int Width;
        public int Height;
        public bool[] Blocked;  // per cell, y * Width + x, y = 0 at the bottom
        public int[][] Wires;   // per pair in symbol order (A, B, ...), from its first dot in reading order
        public string[] Names;  // each pair's symbol
        public string Grid;     // the level text, top row first
        public int Winding;     // extra steps the wires take compared with straight routes
    }

    /// <summary>Live counters for a progress display (written from the worker thread).</summary>
    public sealed class Progress
    {
        public volatile int Layouts;       // layouts tried
        public volatile int Checked;       // layouts that reached the answer check
        public volatile int SecondAnswers; // checks that found another answer
        public volatile int TooSlow;       // checks that ran out of budget
        public volatile int Unique;        // levels with exactly one answer
        public volatile int BestPairs = -1;
        public volatile int BestWinding = -1;
    }

    /// <summary>
    /// Searches until <paramref name="stop"/> fires (or enough levels were
    /// found) and returns the best unique level, or null if none was found.
    /// <paramref name="blocked"/> (one entry per cell) may be null for no walls.
    /// The pair count lands on <paramref name="pairs"/> when the board allows,
    /// otherwise as close as it can.
    /// </summary>
    public static Level Generate(int width, int height, bool[] blocked, int pairs, int seed,
        Progress progress, CancellationToken stop)
    {
        int cells = width * height;
        blocked ??= new bool[cells];
        pairs = Math.Max(2, Math.Min(pairs, MaxPairs));

        var random = new Random(seed);
        Level best = null;
        int bestMiss = int.MaxValue;
        int found = 0;

        while (!stop.IsCancellationRequested && found < EnoughCandidates)
        {
            List<List<int>> layout = RandomLayout(width, height, blocked, pairs, random, stop);
            if (progress != null) progress.Layouts++;
            if (layout == null) continue;

            for (int repair = 0; ; repair++)
            {
                int[][] wires = layout.ConvertAll(path => path.ToArray()).ToArray();
                var endpoints = new int[wires.Length * 2];
                for (int p = 0; p < wires.Length; p++)
                {
                    endpoints[p * 2] = wires[p][0];
                    endpoints[p * 2 + 1] = wires[p][^1];
                }

                AmusSolver.Outcome outcome = AmusSolver.Solve(width, height, blocked, endpoints, true,
                    UniqueCheckBudget, out int[][] other, out _, wires, stop);
                if (progress != null)
                {
                    progress.Checked++;
                    if (outcome == AmusSolver.Outcome.Solved) progress.SecondAnswers++;
                    if (outcome == AmusSolver.Outcome.GaveUp) progress.TooSlow++;
                }

                if (outcome == AmusSolver.Outcome.NoSolution)
                {
                    found++;
                    Level level = Finish(width, height, blocked, wires);
                    int miss = Math.Abs(wires.Length - pairs);
                    if (best == null || miss < bestMiss || (miss == bestMiss && level.Winding > best.Winding))
                    {
                        best = level;
                        bestMiss = miss;
                    }
                    if (progress != null)
                    {
                        progress.Unique = found;
                        progress.BestPairs = best.Wires.Length;
                        progress.BestWinding = best.Winding;
                    }
                    break;
                }

                // Out of time, or a second answer we can't cut away.
                if (outcome != AmusSolver.Outcome.Solved || repair >= MaxRepairs || layout.Count >= MaxPairs) break;
                if (!CutWhereAnswersDiffer(layout, other, cells, random)) break;
            }
        }

        return best;
    }

    #region Layout

    // Covers every open cell with wires of 3+ cells that never touch
    // themselves, or returns null if this try got stuck.
    private static List<List<int>> RandomLayout(int width, int height, bool[] blocked, int pairs, Random random,
        CancellationToken stop)
    {
        int cells = width * height;
        var board = new Board
        {
            Width = width,
            Height = height,
            Blocked = blocked,
            PathOf = new int[cells],
            Paths = new List<List<int>>(),
        };

        // 1. Dominoes, in random order; a cell with no free neighbour starts alone.
        var order = new List<int>(cells);
        for (int i = 0; i < cells; i++)
        {
            board.PathOf[i] = -1;
            if (!blocked[i]) order.Add(i);
        }
        Shuffle(order, random);

        var free = new List<int>(4);
        foreach (int cell in order)
        {
            if (board.PathOf[cell] >= 0) continue;
            board.Neighbours(cell, free);
            free.RemoveAll(n => board.PathOf[n] >= 0);

            var path = new List<int> { cell };
            board.PathOf[cell] = board.Paths.Count;
            if (free.Count > 0)
            {
                int partner = free[random.Next(free.Count)];
                path.Add(partner);
                board.PathOf[partner] = board.Paths.Count;
            }
            board.Paths.Add(path);
        }

        // 2. Join wires end to end, short ones (under 3 cells) first; once
        // none are short, stop at the pair count. Of a few random joins the
        // one making the shortest wire wins, so lengths stay even. When no
        // join fits, re-route a few wire ends and look again.
        var joins = new List<(int a, int endA, int b, int endB)>();
        var shorts = new List<int>();
        int rewiresLeft = RewiresPerOpenCell * order.Count;
        while (true)
        {
            if (stop.IsCancellationRequested) return null;

            shorts.Clear();
            int live = 0;
            for (int p = 0; p < board.Paths.Count; p++)
            {
                if (board.Paths[p] == null) continue;
                live++;
                if (board.Paths[p].Count < MinWireCells) shorts.Add(p);
            }
            if (live <= pairs && shorts.Count == 0) break;

            FindJoins(board, shorts.Count > 0, joins);
            if (joins.Count > 0)
            {
                (int a, int endA, int b, int endB) pick = joins[random.Next(joins.Count)];
                for (int t = 0; t < 2; t++)
                {
                    (int a, int endA, int b, int endB) other = joins[random.Next(joins.Count)];
                    if (board.Paths[other.a].Count + board.Paths[other.b].Count <
                        board.Paths[pick.a].Count + board.Paths[pick.b].Count)
                        pick = other;
                }
                board.Join(pick.a, pick.endA, pick.b, pick.endB);
                continue;
            }

            if (rewiresLeft <= 0) break;
            for (int r = 0; r < RewiresBetweenScans && rewiresLeft > 0; r++, rewiresLeft--)
            {
                // A short wire is the one that needs to grow; otherwise any wire.
                int moving = shorts.Count > 0 ? shorts[random.Next(shorts.Count)] : board.RandomLivePath(random);
                if (board.Paths[moving] != null) TryRewire(board, moving, random);
            }
        }

        var result = new List<List<int>>();
        foreach (List<int> path in board.Paths)
        {
            if (path == null) continue;
            if (path.Count < MinWireCells) return null;
            result.Add(path);
        }
        return result.Count >= 2 && result.Count <= MaxPairs ? result : null;
    }

    // Every join (wire a's end onto wire b's end) that keeps the joined wire
    // from touching itself. With 'shortsOnly', only joins that grow a short wire.
    private static void FindJoins(Board board, bool shortsOnly, List<(int a, int endA, int b, int endB)> joins)
    {
        joins.Clear();
        var neighbours = new List<int>(4);
        for (int a = 0; a < board.Paths.Count; a++)
        {
            List<int> path = board.Paths[a];
            if (path == null || (shortsOnly && path.Count >= MinWireCells)) continue;

            for (int side = 0; side < (path.Count == 1 ? 1 : 2); side++)
            {
                int end = side == 0 ? path[0] : path[^1];
                board.Neighbours(end, neighbours);
                foreach (int n in neighbours)
                {
                    int b = board.PathOf[n];
                    if (b == a) continue;
                    List<int> other = board.Paths[b];
                    if (other[0] != n && other[^1] != n) continue; // not an end
                    if (board.PieceFits(other, 0, other.Count - 1, n, a, end)) joins.Add((a, end, b, n));
                }
            }
        }
    }

    // Wire 'a' takes over part of a neighbouring wire: from the touched cell
    // to one end of that wire. What's left of the neighbour must still be 3+
    // cells long, and wire 'a' must still not touch itself.
    private static void TryRewire(Board board, int a, Random random)
    {
        List<int> path = board.Paths[a];
        int end = path.Count == 1 || random.Next(2) == 0 ? path[0] : path[^1];

        var neighbours = new List<int>(4);
        board.Neighbours(end, neighbours);
        if (neighbours.Count == 0) return;
        int touched = neighbours[random.Next(neighbours.Count)];
        int b = board.PathOf[touched];
        if (b == a) return;

        List<int> other = board.Paths[b];
        int at = other.IndexOf(touched);
        bool towardEnd = random.Next(2) == 0;
        for (int attempt = 0; attempt < 2; attempt++, towardEnd = !towardEnd)
        {
            // The piece runs from 'touched' to one end of the other wire.
            int first = towardEnd ? at : 0;
            int last = towardEnd ? other.Count - 1 : at;
            int left = other.Count - (last - first + 1);
            if (left > 0 && left < MinWireCells) continue;
            if (!board.PieceFits(other, first, last, touched, a, end)) continue;

            if (path[^1] != end) path.Reverse();
            if (towardEnd)
                for (int k = first; k <= last; k++) path.Add(other[k]);
            else
                for (int k = last; k >= first; k--) path.Add(other[k]);
            for (int k = first; k <= last; k++) board.PathOf[other[k]] = a;

            if (left == 0) board.Paths[b] = null;
            else board.Paths[b] = towardEnd ? other.GetRange(0, at) : other.GetRange(at + 1, other.Count - at - 1);
            return;
        }
    }

    // The other answer skips at least one step of ours. Cut one of our wires
    // at such a step (both halves 3+ cells) so each half becomes its own pair:
    // the other answer no longer fits, since it runs through the new dots.
    private static bool CutWhereAnswersDiffer(List<List<int>> layout, int[][] other, int cells, Random random)
    {
        var otherSteps = new HashSet<long>();
        foreach (int[] wire in other)
            for (int s = 1; s < wire.Length; s++)
                otherSteps.Add(StepKey(wire[s - 1], wire[s], cells));

        var cuts = new List<(int wire, int at)>();
        for (int w = 0; w < layout.Count; w++)
        {
            List<int> path = layout[w];
            for (int s = MinWireCells; s <= path.Count - MinWireCells; s++)
                if (!otherSteps.Contains(StepKey(path[s - 1], path[s], cells))) cuts.Add((w, s));
        }
        if (cuts.Count == 0) return false;

        (int wire, int at) cut = cuts[random.Next(cuts.Count)];
        List<int> whole = layout[cut.wire];
        layout[cut.wire] = whole.GetRange(0, cut.at);
        layout.Add(whole.GetRange(cut.at, whole.Count - cut.at));
        return true;
    }

    private static long StepKey(int a, int b, int cells) => a < b ? (long)a * cells + b : (long)b * cells + a;

    private static void Shuffle(List<int> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // The wires being laid out: each cell's wire index, and each wire's cells
    // in order (null once joined into another).
    private sealed class Board
    {
        public int Width;
        public int Height;
        public bool[] Blocked;
        public int[] PathOf;
        public List<List<int>> Paths;

        private readonly List<int> _around = new(4);

        public void Neighbours(int cell, List<int> into)
        {
            into.Clear();
            int x = cell % Width;
            int y = cell / Width;
            if (x > 0 && !Blocked[cell - 1]) into.Add(cell - 1);
            if (x < Width - 1 && !Blocked[cell + 1]) into.Add(cell + 1);
            if (y > 0 && !Blocked[cell - Width]) into.Add(cell - Width);
            if (y < Height - 1 && !Blocked[cell + Width]) into.Add(cell + Width);
        }

        // Can other[first..last] hang off wire a's end 'end' through the cell
        // 'touched'? Only if no other cell of the piece sits next to wire a.
        public bool PieceFits(List<int> other, int first, int last, int touched, int a, int end)
        {
            for (int k = first; k <= last; k++)
            {
                int cell = other[k];
                Neighbours(cell, _around);
                foreach (int n in _around)
                    if (PathOf[n] == a && !(cell == touched && n == end)) return false;
            }
            return true;
        }

        // Wire b is appended to wire a: a runs to endA, then b from endB on.
        public void Join(int a, int endA, int b, int endB)
        {
            List<int> first = Paths[a];
            List<int> second = Paths[b];
            if (first[^1] != endA) first.Reverse();
            if (second[0] != endB) second.Reverse();
            foreach (int cell in second) PathOf[cell] = a;
            first.AddRange(second);
            Paths[b] = null;
        }

        public int RandomLivePath(Random random)
        {
            while (true)
            {
                int p = random.Next(Paths.Count);
                if (Paths[p] != null) return p;
            }
        }
    }

    #endregion

    // Names the pairs A, B, ... in reading order (top row first, left to
    // right), each wire starting from its first dot in that order, which is
    // how AmusPuzzle.Parse will read the text back.
    private static Level Finish(int width, int height, bool[] blocked, int[][] wires)
    {
        int cells = width * height;
        var wireAt = new int[cells];
        for (int i = 0; i < cells; i++) wireAt[i] = -1;
        for (int p = 0; p < wires.Length; p++)
        {
            wireAt[wires[p][0]] = p;
            wireAt[wires[p][^1]] = p;
        }

        var named = new List<int[]>();
        var names = new List<string>();
        var symbolOf = new string[cells];
        var text = new StringBuilder();
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                int p = wireAt[cell];
                if (p >= 0 && symbolOf[cell] == null)
                {
                    int[] wire = wires[p];
                    if (wire[0] != cell) wire = Reversed(wire);
                    string symbol = Symbols[named.Count];
                    named.Add(wire);
                    names.Add(symbol);
                    symbolOf[wire[0]] = symbol;
                    symbolOf[wire[^1]] = symbol;
                }

                if (x > 0) text.Append(' ');
                text.Append(blocked[cell] ? "#" : symbolOf[cell] ?? ".");
            }
            if (y > 0) text.Append('\n');
        }

        int winding = 0;
        foreach (int[] wire in named)
        {
            int a = wire[0];
            int b = wire[^1];
            int straight = Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width);
            winding += wire.Length - 1 - straight;
        }

        return new Level
        {
            Width = width,
            Height = height,
            Blocked = (bool[])blocked.Clone(),
            Wires = named.ToArray(),
            Names = names.ToArray(),
            Grid = text.ToString(),
            Winding = winding,
        };
    }

    private static int[] Reversed(int[] wire)
    {
        var copy = (int[])wire.Clone();
        Array.Reverse(copy);
        return copy;
    }
}
