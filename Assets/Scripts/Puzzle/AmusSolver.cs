using System.Collections.Generic;

/// <summary>
/// Finds a solution to an Amus board: one wire per pair, no two wires sharing a
/// cell and, with <c>fill</c> on, every open cell covered. Plain C# with no
/// Unity types, so it can run on a worker thread and be tested outside Unity.
///
/// The board is turned into a logic formula and handed to a small built-in SAT
/// solver (the standard way to crack Numberlink-style puzzles; a plain
/// depth-first search stalls on the 12x12+ boards):
///   * every cell has one colour; each endpoint has its pair's colour;
///   * a "link" between two neighbouring cells means they're consecutive on a
///     wire, and passes the colour along;
///   * an endpoint has exactly one link, every other cell exactly two (with
///     fill off: zero or two, and coloured only if linked).
/// That formula also allows stray closed loops, so any loop in an answer is
/// forbidden and the search runs again until the answer is loop-free.
///
/// Cells are indexed <c>y * width + x</c>, matching the board's coordinates.
/// </summary>
public static class AmusSolver
{
    public enum Outcome
    {
        Solved,
        NoSolution,
        GaveUp,
    }

    /// <summary>
    /// Solves a board. <paramref name="blocked"/> has one entry per cell;
    /// <paramref name="endpoints"/> holds each pair's two cells in turn
    /// (a0, b0, a1, b1, ...). On success <paramref name="wires"/> holds each
    /// pair's cells in order from its first endpoint to its second.
    /// <paramref name="conflictBudget"/> bounds the work; running out reports
    /// <see cref="Outcome.GaveUp"/>.
    /// </summary>
    public static Outcome Solve(int width, int height, bool[] blocked, int[] endpoints, bool fill,
        int conflictBudget, out int[][] wires, out int conflicts)
    {
        wires = null;
        conflicts = 0;

        int cells = width * height;
        int pairs = endpoints.Length / 2;
        if (pairs == 0) return Outcome.NoSolution;

        var endpointOf = new int[cells];
        for (int i = 0; i < cells; i++) endpointOf[i] = -1;
        for (int p = 0; p < pairs; p++)
        {
            endpointOf[endpoints[p * 2]] = p;
            endpointOf[endpoints[p * 2 + 1]] = p;
        }

        // Variables: one per (open cell, colour), then one per link.
        var colourVar = new int[cells * pairs];
        int vars = 0;
        for (int i = 0; i < cells; i++)
            for (int k = 0; k < pairs; k++)
                colourVar[i * pairs + k] = blocked[i] ? -1 : vars++;

        var linkA = new List<int>();
        var linkB = new List<int>();
        var incident = new List<int>[cells]; // link indices touching each cell
        for (int i = 0; i < cells; i++) incident[i] = new List<int>(4);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (blocked[i]) continue;
                if (x + 1 < width && !blocked[i + 1]) AddLink(i, i + 1);
                if (y + 1 < height && !blocked[i + width]) AddLink(i, i + width);
            }
        }
        int firstLinkVar = vars;
        vars += linkA.Count;

        void AddLink(int a, int b)
        {
            incident[a].Add(linkA.Count);
            incident[b].Add(linkA.Count);
            linkA.Add(a);
            linkB.Add(b);
        }

        int Colour(int cell, int k) => colourVar[cell * pairs + k];
        int Link(int l) => firstLinkVar + l;

        var sat = new Sat(vars);
        var buffer = new List<int>();

        for (int i = 0; i < cells; i++)
        {
            if (blocked[i]) continue;

            var links = new List<int>(incident[i].Count);
            foreach (int l in incident[i]) links.Add(Link(l));

            int endpoint = endpointOf[i];
            if (endpoint >= 0)
            {
                for (int k = 0; k < pairs; k++)
                    sat.AddClause(k == endpoint ? Sat.Pos(Colour(i, k)) : Sat.Neg(Colour(i, k)));
                ExactlyOne(sat, links, buffer);
                continue;
            }

            // At most one colour; with fill, at least one too.
            for (int k = 0; k < pairs; k++)
                for (int k2 = k + 1; k2 < pairs; k2++)
                    sat.AddClause(Sat.Neg(Colour(i, k)), Sat.Neg(Colour(i, k2)));

            if (fill)
            {
                buffer.Clear();
                for (int k = 0; k < pairs; k++) buffer.Add(Sat.Pos(Colour(i, k)));
                sat.AddClause(buffer);
                ExactlyTwo(sat, links, buffer);
            }
            else
            {
                // Zero or two links; coloured exactly when linked.
                AtMostTwo(sat, links);
                NotExactlyOne(sat, links, buffer);
                for (int k = 0; k < pairs; k++)
                {
                    buffer.Clear();
                    buffer.Add(Sat.Neg(Colour(i, k)));
                    foreach (int v in links) buffer.Add(Sat.Pos(v));
                    sat.AddClause(buffer);
                }
            }
        }

        // A link carries the colour across, both ways.
        for (int l = 0; l < linkA.Count; l++)
        {
            int v = Link(l);
            int a = linkA[l];
            int b = linkB[l];
            for (int k = 0; k < pairs; k++)
            {
                sat.AddClause(Sat.Neg(v), Sat.Neg(Colour(a, k)), Sat.Pos(Colour(b, k)));
                sat.AddClause(Sat.Neg(v), Sat.Neg(Colour(b, k)), Sat.Pos(Colour(a, k)));
            }
            if (!fill)
            {
                foreach (int end in new[] { a, b })
                {
                    buffer.Clear();
                    buffer.Add(Sat.Neg(v));
                    for (int k = 0; k < pairs; k++) buffer.Add(Sat.Pos(Colour(end, k)));
                    sat.AddClause(buffer);
                }
            }
        }

        // Solve; forbid any closed loop in the answer and go again.
        while (true)
        {
            bool? answer = sat.Solve(conflictBudget - conflicts, ref conflicts);
            if (answer == null) return Outcome.GaveUp;
            if (answer == false) return Outcome.NoSolution;

            // Copy the answer out first: adding a loop ban resets the solver.
            var linkOn = new bool[linkA.Count];
            for (int l = 0; l < linkOn.Length; l++) linkOn[l] = sat.IsTrue(Link(l));
            var bans = new List<List<int>>();

            bool LinkOn(int l) => linkOn[l];
            int Next(int cell, int previous)
            {
                foreach (int l in incident[cell])
                {
                    if (!LinkOn(l)) continue;
                    int other = linkA[l] == cell ? linkB[l] : linkA[l];
                    if (other != previous) return other;
                }
                return -1;
            }

            var covered = new bool[cells];
            wires = new int[pairs][];
            for (int p = 0; p < pairs; p++)
            {
                var wire = new List<int>();
                int previous = -1;
                int cell = endpoints[p * 2];
                while (cell >= 0 && wire.Count <= cells)
                {
                    wire.Add(cell);
                    covered[cell] = true;
                    if (cell == endpoints[p * 2 + 1]) break;
                    int next = Next(cell, previous);
                    previous = cell;
                    cell = next;
                }
                wires[p] = wire.ToArray();
            }

            for (int i = 0; i < cells; i++)
            {
                if (blocked[i] || covered[i]) continue;

                // Linked but not on any wire: part of a loop. Walk it and ban it.
                var ban = new List<int>();
                int previous = -1;
                int cell = i;
                while (true)
                {
                    covered[cell] = true;
                    int next = Next(cell, previous);
                    if (next < 0) break; // unlinked (fill off): simply empty
                    foreach (int l in incident[cell])
                    {
                        int other = linkA[l] == cell ? linkB[l] : linkA[l];
                        if (other == next) ban.Add(Sat.Neg(Link(l)));
                    }
                    previous = cell;
                    cell = next;
                    if (cell == i) break;
                }
                if (ban.Count > 0) bans.Add(ban);
            }

            if (bans.Count == 0) return Outcome.Solved;
            foreach (List<int> ban in bans) sat.AddClause(ban);
            wires = null;
        }
    }

    #region Cardinality helpers

    private static void ExactlyOne(Sat sat, List<int> vars, List<int> buffer)
    {
        buffer.Clear();
        foreach (int v in vars) buffer.Add(Sat.Pos(v));
        sat.AddClause(buffer);
        for (int a = 0; a < vars.Count; a++)
            for (int b = a + 1; b < vars.Count; b++)
                sat.AddClause(Sat.Neg(vars[a]), Sat.Neg(vars[b]));
    }

    private static void ExactlyTwo(Sat sat, List<int> vars, List<int> buffer)
    {
        AtMostTwo(sat, vars);

        // At least two: leaving any single one out, one of the rest is still on.
        for (int skip = 0; skip < vars.Count; skip++)
        {
            buffer.Clear();
            for (int j = 0; j < vars.Count; j++)
                if (j != skip) buffer.Add(Sat.Pos(vars[j]));
            sat.AddClause(buffer);
        }
    }

    private static void AtMostTwo(Sat sat, List<int> vars)
    {
        for (int a = 0; a < vars.Count; a++)
            for (int b = a + 1; b < vars.Count; b++)
                for (int c = b + 1; c < vars.Count; c++)
                    sat.AddClause(Sat.Neg(vars[a]), Sat.Neg(vars[b]), Sat.Neg(vars[c]));
    }

    // Any one link on implies another is on too.
    private static void NotExactlyOne(Sat sat, List<int> vars, List<int> buffer)
    {
        for (int a = 0; a < vars.Count; a++)
        {
            buffer.Clear();
            buffer.Add(Sat.Neg(vars[a]));
            for (int b = 0; b < vars.Count; b++)
                if (b != a) buffer.Add(Sat.Pos(vars[b]));
            sat.AddClause(buffer);
        }
    }

    #endregion

    /// <summary>
    /// A compact CDCL SAT solver (two watched literals, first-UIP clause
    /// learning, VSIDS ordering, phase saving, Luby restarts). Literal
    /// <c>2v</c> is variable v true, <c>2v+1</c> is v false.
    /// </summary>
    private sealed class Sat
    {
        private readonly int _vars;
        private readonly List<int[]> _clauses = new();
        private readonly List<int>[] _watches; // per literal: clauses watching it
        private readonly sbyte[] _value;       // per variable: 1 true, -1 false, 0 unset
        private readonly int[] _level;
        private readonly int[] _reason;        // clause that forced it, -1 for decisions
        private readonly bool[] _phase;
        private readonly bool[] _seen;
        private readonly double[] _activity;
        private readonly int[] _heap;          // max-heap of variables by activity
        private readonly int[] _heapIndex;     // position in _heap, -1 when absent
        private int _heapCount;

        private readonly int[] _trail;
        private int _trailSize;
        private readonly List<int> _trailLimits = new();
        private int _propagated;
        private double _increment = 1.0;
        private bool _unsat;

        public static int Pos(int v) => v * 2;
        public static int Neg(int v) => v * 2 + 1;
        private static int VarOf(int literal) => literal >> 1;

        public Sat(int vars)
        {
            _vars = vars;
            _watches = new List<int>[vars * 2];
            for (int i = 0; i < _watches.Length; i++) _watches[i] = new List<int>();
            _value = new sbyte[vars];
            _level = new int[vars];
            _reason = new int[vars];
            _phase = new bool[vars];
            _seen = new bool[vars];
            _activity = new double[vars];
            _heap = new int[vars];
            _heapIndex = new int[vars];
            _trail = new int[vars];
            for (int v = 0; v < vars; v++)
            {
                _reason[v] = -1;
                _heapIndex[v] = -1;
                HeapInsert(v);
            }
        }

        public bool IsTrue(int v) => _value[v] > 0;

        private int LiteralValue(int literal)
        {
            int v = _value[literal >> 1];
            return (literal & 1) == 0 ? v : -v;
        }

        private int DecisionLevel => _trailLimits.Count;

        public void AddClause(params int[] literals) => AddClause(new List<int>(literals));

        /// <summary>Adds a clause. Safe between solves: it backs out to level 0 first.</summary>
        public void AddClause(List<int> literals)
        {
            if (_unsat) return;
            Backtrack(0);

            // Drop duplicates and literals already false; a tautology or an
            // already-true literal makes the clause redundant.
            var clause = new List<int>(literals.Count);
            foreach (int literal in literals)
            {
                int value = LiteralValue(literal);
                if (value > 0 || clause.Contains(literal ^ 1)) return;
                if (value < 0 || clause.Contains(literal)) continue;
                clause.Add(literal);
            }

            if (clause.Count == 0)
            {
                _unsat = true;
                return;
            }
            if (clause.Count == 1)
            {
                Enqueue(clause[0], -1);
                if (Propagate() >= 0) _unsat = true;
                return;
            }
            AttachClause(clause.ToArray());
        }

        private int AttachClause(int[] clause)
        {
            int index = _clauses.Count;
            _clauses.Add(clause);
            _watches[clause[0]].Add(index);
            _watches[clause[1]].Add(index);
            return index;
        }

        /// <summary>
        /// True with a model, false if unsatisfiable, null if the conflict
        /// budget ran out. <paramref name="conflicts"/> accumulates across calls.
        /// </summary>
        public bool? Solve(int budget, ref int conflicts)
        {
            if (_unsat) return false;
            if (Propagate() >= 0)
            {
                _unsat = true;
                return false;
            }

            int used = 0;
            int restart = 0;
            int restartLimit = 100 * Luby(restart);
            int sinceRestart = 0;

            while (true)
            {
                int conflict = Propagate();
                if (conflict >= 0)
                {
                    conflicts++;
                    used++;
                    sinceRestart++;
                    if (DecisionLevel == 0)
                    {
                        _unsat = true;
                        return false;
                    }

                    int[] learnt = Analyze(conflict, out int backLevel);
                    Backtrack(backLevel);
                    if (learnt.Length == 1) Enqueue(learnt[0], -1);
                    else Enqueue(learnt[0], AttachClause(learnt));
                    _increment /= 0.95;

                    if (used >= budget)
                    {
                        Backtrack(0);
                        return null;
                    }
                    continue;
                }

                if (sinceRestart >= restartLimit)
                {
                    Backtrack(0);
                    restartLimit = 100 * Luby(++restart);
                    sinceRestart = 0;
                    continue;
                }

                int next = PickBranch();
                if (next < 0) return true;
                _trailLimits.Add(_trailSize);
                Enqueue(_phase[next] ? Pos(next) : Neg(next), -1);
            }
        }

        private void Enqueue(int literal, int reason)
        {
            int v = literal >> 1;
            _value[v] = (sbyte)((literal & 1) == 0 ? 1 : -1);
            _level[v] = DecisionLevel;
            _reason[v] = reason;
            _trail[_trailSize++] = literal;
        }

        // Returns the index of a conflicting clause, or -1.
        private int Propagate()
        {
            while (_propagated < _trailSize)
            {
                int falseLiteral = _trail[_propagated++] ^ 1;
                List<int> watchers = _watches[falseLiteral];
                int kept = 0;
                int i = 0;
                while (i < watchers.Count)
                {
                    int index = watchers[i++];
                    int[] clause = _clauses[index];

                    // Keep the falsified watch in slot 1.
                    if (clause[0] == falseLiteral)
                    {
                        clause[0] = clause[1];
                        clause[1] = falseLiteral;
                    }

                    if (LiteralValue(clause[0]) > 0)
                    {
                        watchers[kept++] = index;
                        continue;
                    }

                    bool moved = false;
                    for (int k = 2; k < clause.Length; k++)
                    {
                        if (LiteralValue(clause[k]) < 0) continue;
                        clause[1] = clause[k];
                        clause[k] = falseLiteral;
                        _watches[clause[1]].Add(index);
                        moved = true;
                        break;
                    }
                    if (moved) continue;

                    watchers[kept++] = index;
                    if (LiteralValue(clause[0]) < 0)
                    {
                        while (i < watchers.Count) watchers[kept++] = watchers[i++];
                        watchers.RemoveRange(kept, watchers.Count - kept);
                        _propagated = _trailSize;
                        return index;
                    }
                    Enqueue(clause[0], index);
                }
                watchers.RemoveRange(kept, watchers.Count - kept);
            }
            return -1;
        }

        // First-UIP learning: walk back along the trail until a single literal
        // of the current level explains the conflict.
        private int[] Analyze(int conflict, out int backLevel)
        {
            var learnt = new List<int> { -1 };
            int pending = 0;
            int literal = -1;
            int index = _trailSize - 1;
            int clauseIndex = conflict;

            do
            {
                int[] clause = _clauses[clauseIndex];
                for (int k = literal < 0 ? 0 : 1; k < clause.Length; k++)
                {
                    int q = clause[k];
                    int v = q >> 1;
                    if (_seen[v] || _level[v] == 0) continue;

                    _seen[v] = true;
                    Bump(v);
                    if (_level[v] >= DecisionLevel) pending++;
                    else learnt.Add(q);
                }

                while (!_seen[_trail[index] >> 1]) index--;
                literal = _trail[index--];
                clauseIndex = _reason[literal >> 1];
                _seen[literal >> 1] = false;
                pending--;
            } while (pending > 0);

            learnt[0] = literal ^ 1;

            backLevel = 0;
            int best = 1;
            for (int k = 1; k < learnt.Count; k++)
            {
                int v = learnt[k] >> 1;
                _seen[v] = false;
                if (_level[v] > backLevel)
                {
                    backLevel = _level[v];
                    best = k;
                }
            }
            // The deepest remaining literal becomes the second watch.
            if (learnt.Count > 1) (learnt[1], learnt[best]) = (learnt[best], learnt[1]);
            return learnt.ToArray();
        }

        private void Backtrack(int level)
        {
            if (DecisionLevel <= level) return;

            int keep = _trailLimits[level];
            for (int i = _trailSize - 1; i >= keep; i--)
            {
                int v = _trail[i] >> 1;
                _phase[v] = _value[v] > 0;
                _value[v] = 0;
                _reason[v] = -1;
                HeapInsert(v);
            }
            _trailSize = keep;
            _propagated = keep;
            _trailLimits.RemoveRange(level, _trailLimits.Count - level);
        }

        private int PickBranch()
        {
            while (_heapCount > 0)
            {
                int v = HeapPopMax();
                if (_value[v] == 0) return v;
            }
            return -1;
        }

        private void Bump(int v)
        {
            _activity[v] += _increment;
            if (_activity[v] > 1e100)
            {
                for (int i = 0; i < _vars; i++) _activity[i] *= 1e-100;
                _increment *= 1e-100;
            }
            if (_heapIndex[v] >= 0) HeapUp(_heapIndex[v]);
        }

        // 1, 1, 2, 1, 1, 2, 4, 1, 1, 2, ... (MiniSat's restart schedule).
        private static int Luby(int i)
        {
            int size = 1;
            int sequence = 0;
            while (size < i + 1)
            {
                sequence++;
                size = 2 * size + 1;
            }
            while (size - 1 != i)
            {
                size = (size - 1) >> 1;
                sequence--;
                i %= size;
            }
            return 1 << sequence;
        }

        #region Heap

        private void HeapInsert(int v)
        {
            if (_heapIndex[v] >= 0) return;
            _heap[_heapCount] = v;
            _heapIndex[v] = _heapCount;
            HeapUp(_heapCount++);
        }

        private int HeapPopMax()
        {
            int top = _heap[0];
            _heapIndex[top] = -1;
            int last = _heap[--_heapCount];
            if (_heapCount > 0)
            {
                _heap[0] = last;
                _heapIndex[last] = 0;
                HeapDown(0);
            }
            return top;
        }

        private void HeapUp(int i)
        {
            int v = _heap[i];
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_activity[_heap[parent]] >= _activity[v]) break;
                _heap[i] = _heap[parent];
                _heapIndex[_heap[i]] = i;
                i = parent;
            }
            _heap[i] = v;
            _heapIndex[v] = i;
        }

        private void HeapDown(int i)
        {
            int v = _heap[i];
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= _heapCount) break;
                if (child + 1 < _heapCount && _activity[_heap[child + 1]] > _activity[_heap[child]]) child++;
                if (_activity[_heap[child]] <= _activity[v]) break;
                _heap[i] = _heap[child];
                _heapIndex[_heap[i]] = i;
                i = child;
            }
            _heap[i] = v;
            _heapIndex[v] = i;
        }

        #endregion
    }
}
