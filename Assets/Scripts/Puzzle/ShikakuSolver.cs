using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Backtracking Shikaku solver. Given the clue grid, finds a set of
/// non-overlapping rectangles that partition the board, each containing exactly
/// one clue with area equal to it. Used by the hint / solve-all helpers, so
/// levels don't need hand-authored solutions.
/// </summary>
public static class ShikakuSolver
{
    /// <summary>One solution rectangle per clue, or null if unsolvable.</summary>
    public static List<RectInt> Solve(int[,] clues)
    {
        int w = clues.GetLength(0);
        int h = clues.GetLength(1);

        var cluePos = new List<Vector2Int>();
        int sum = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (clues[x, y] > 0)
                {
                    cluePos.Add(new Vector2Int(x, y));
                    sum += clues[x, y];
                }
            }
        }

        // A full partition needs the clue values to sum to the cell count.
        if (cluePos.Count == 0 || sum != w * h) return null;

        // All legal rectangles per clue: right area, in bounds, no second clue.
        var candidates = new List<List<RectInt>>();
        foreach (Vector2Int p in cluePos)
        {
            int value = clues[p.x, p.y];
            var list = new List<RectInt>();

            for (int rw = 1; rw <= Mathf.Min(value, w); rw++)
            {
                if (value % rw != 0) continue;
                int rh = value / rw;
                if (rh > h) continue;

                for (int x0 = Mathf.Max(0, p.x - rw + 1); x0 <= p.x && x0 + rw <= w; x0++)
                {
                    for (int y0 = Mathf.Max(0, p.y - rh + 1); y0 <= p.y && y0 + rh <= h; y0++)
                    {
                        var r = new RectInt(x0, y0, rw, rh);
                        if (!ContainsOtherClue(clues, r, p)) list.Add(r);
                    }
                }
            }

            if (list.Count == 0) return null; // this clue has no legal rectangle
            candidates.Add(list);
        }

        // Most-constrained clues first keeps the search tiny.
        var order = new List<int>();
        for (int i = 0; i < cluePos.Count; i++) order.Add(i);
        order.Sort((a, b) => candidates[a].Count.CompareTo(candidates[b].Count));

        var occupied = new bool[w, h];
        var chosen = new RectInt[cluePos.Count];

        bool Search(int depth)
        {
            if (depth == order.Count) return true;

            int clueIndex = order[depth];
            foreach (RectInt r in candidates[clueIndex])
            {
                if (Overlaps(occupied, r)) continue;

                Mark(occupied, r, true);
                chosen[clueIndex] = r;
                if (Search(depth + 1)) return true;
                Mark(occupied, r, false);
            }
            return false;
        }

        return Search(0) ? new List<RectInt>(chosen) : null;
    }

    private static bool ContainsOtherClue(int[,] clues, RectInt r, Vector2Int own)
    {
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                if (clues[x, y] > 0 && (x != own.x || y != own.y))
                    return true;
        return false;
    }

    private static bool Overlaps(bool[,] occupied, RectInt r)
    {
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                if (occupied[x, y]) return true;
        return false;
    }

    private static void Mark(bool[,] occupied, RectInt r, bool value)
    {
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                occupied[x, y] = value;
    }
}
