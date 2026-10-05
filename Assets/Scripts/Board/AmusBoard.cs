using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Owns an Amus (wire-connect) board: builds tiles from a prefab, manages one
/// wire per colour pair, and reports a win.
///
/// Each pair is drawn as a coloured dot on both of its endpoints joined by a
/// thick rounded trail through the cells the player dragged through, so the
/// board reads as dots being wired together rather than as tinted squares.
///
/// Rules enforced while dragging (so any board state is automatically legal):
///   * A wire starts at one of its two endpoints and grows in orthogonal steps.
///   * No cell can belong to two wires, and a wire can't enter another pair's
///     endpoint — wires never touch or cross.
///   * Dragging back over the wire's own cells truncates it to that cell.
///   * A wire that has reached its far endpoint is terminal: it can only be
///     backed out of or restarted, never extended.
/// Releasing mid-wire keeps the partial wire; grabbing an endpoint restarts
/// that colour from scratch. The level is solved when every pair is connected
/// (and, if the level asks for it, every open cell is covered).
///
/// Erase, connect and win effects live in <see cref="AmusFx"/>, added
/// automatically. The win effect plays before <see cref="OnSolved"/> fires,
/// because the win panel hides the board.
///
/// Helpers (driven by <see cref="AmusHelperUI"/>): <see cref="Undo"/> steps back
/// one drag or reset, <see cref="ResetBoard"/> clears every wire the player
/// drew, and <see cref="LockOnePath"/> lays one pair's wire on its correct path
/// and locks it. Locks come from one solution, so they never contradict each
/// other; it's read from the level's baked solution, or solved on a worker
/// thread when the level has none.
/// </summary>
[DisallowMultipleComponent]
public class AmusBoard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PreymetTile tilePrefab;
    [SerializeField] private Transform boardParent;
    [Tooltip("Fallback level used only when testing this scene directly.")]
    [SerializeField] private LevelDatabase fallbackDatabase;
    [Tooltip("Optional: force a single puzzle (overrides everything). Handy for testing.")]
    [SerializeField] private AmusPuzzle puzzle;

    [Header("Layout")]
    [Tooltip("World-space size of one cell. Ignored when Auto Cell Size is on.")]
    [SerializeField] private float cellSize = 1f;
    [Tooltip("Compute cell size from the camera so the whole board always fits on screen.")]
    [SerializeField] private bool autoCellSize = true;
    [Tooltip("Upper limit for auto cell size, so small boards don't get huge tiles.")]
    [SerializeField] private float maxCellSize = 1f;
    [Range(0.5f, 1f)]
    [Tooltip("Fraction of the screen the board may fill when auto-sizing.")]
    [SerializeField] private float boardScreenFill = 0.9f;
    [Tooltip("Scale each spawned tile to fit Cell Size. Off = use the prefab's own scale.")]
    [SerializeField] private bool autoFitTiles = true;
    [Range(0.5f, 1f)]
    [Tooltip("Fraction of the cell a tile fills (leaves a gap between tiles).")]
    [SerializeField] private float tileFill = 0.95f;

    [Header("Colours")]
    [SerializeField] private Color emptyColor = new(0.92f, 0.92f, 0.92f);
    [SerializeField] private Color blockedColor = new(0.20f, 0.20f, 0.24f);
    [Tooltip("One colour per pair, in the order the pairs appear in the level text. Wraps around if there are more pairs than colours.")]
    [SerializeField]
    private Color[] palette =
    {
        new(0.86f, 0.24f, 0.22f), // red
        new(0.25f, 0.45f, 0.90f), // blue
        new(0.95f, 0.80f, 0.25f), // yellow
        new(0.90f, 0.40f, 0.75f), // pink
        new(0.30f, 0.75f, 0.40f), // green
        new(0.95f, 0.55f, 0.25f), // orange
        new(0.35f, 0.80f, 0.85f), // cyan
        new(0.60f, 0.40f, 0.85f), // purple
    };
    [Tooltip("Also tint the tiles a wire crosses. Off = the trail alone shows the wire, which is the cleaner look.")]
    [SerializeField] private bool tintCells = false;
    [Range(0f, 1f)]
    [Tooltip("How far a tinted wire cell fades toward the empty colour. Only used when Tint Cells is on.")]
    [SerializeField] private float wireFade = 0.35f;
    [Tooltip("Show each pair's letter on its endpoints (helps colour-blind players).")]
    [SerializeField] private bool showEndpointLetters = true;

    [Header("Wire look")]
    [Tooltip("Optional: a prefab with a styled LineRenderer, one spawned per pair. Empty = build a plain rounded line.")]
    [SerializeField] private AmusWire wirePrefab;
    [Tooltip("Optional: material for the trails. Empty = a generated unlit one. Must respect vertex colour.")]
    [SerializeField] private Material wireMaterial;
    [Range(0.05f, 0.9f)]
    [Tooltip("Trail thickness, as a fraction of a cell.")]
    [SerializeField] private float wireWidth = 0.32f;
    [Range(0, 16)]
    [Tooltip("Vertices added to each corner and end of the trail. 0 = sharp corners, ~8 = fully rounded.")]
    [SerializeField] private int wireRoundness = 8;
    [Tooltip("Draw a dot on each endpoint. Off = colour the endpoint tile instead.")]
    [SerializeField] private bool showDots = true;
    [Range(0.1f, 1f)]
    [Tooltip("Endpoint dot diameter, as a fraction of a cell.")]
    [SerializeField] private float dotSize = 0.62f;
    [Tooltip("Sorting layer for trails and dots. Empty = leave on the default layer.")]
    [SerializeField] private string sortingLayer = "";
    [Tooltip("Draw order for the trails. Must be at least 2 above the tiles' order (the erase effect goes just under the trails); effects, dots and endpoint letters go just above it.")]
    [SerializeField] private int wireSortingOrder = 10;

    [Header("Feedback")]
    [Tooltip("Raise the step sound's pitch as more pairs get connected. Clips are assigned on the SoundManager.")]
    [SerializeField] private bool risingPitch = true;
    [Range(0f, 1f)]
    [Tooltip("Total pitch rise between no pairs connected and all connected.")]
    [SerializeField] private float pitchRise = 0.4f;
    [Tooltip("On levels that must be filled: the still-empty tiles pulse this colour when every pair is joined but the board isn't full yet.")]
    [SerializeField] private Color unfilledFlashColor = new(1f, 0.8f, 0.45f);

    [Header("Lock helper look")]
    [Tooltip("Stripe drawn down the middle of a locked wire.")]
    [SerializeField] private Color lockStripeColor = new(1f, 1f, 1f, 0.65f);
    [Range(0.05f, 1f)]
    [Tooltip("Stripe width, as a fraction of the wire's width.")]
    [SerializeField] private float lockStripeWidth = 0.35f;

    [Header("Events")]
    public UnityEvent OnSolved;
    [Tooltip("Fired when the time limit runs out before solving.")]
    public UnityEvent OnFailed;

    private int _width;
    private int _height;
    private bool[,] _blocked;
    private List<AmusPuzzle.Pair> _pairs;
    private List<Vector2Int>[] _wires; // one per pair; [0] is always an endpoint

    // Draw order above wireSortingOrder: +1..+3 are the win ball (see AmusFx),
    // which runs inside the cable, so dots and letters sit above it.
    private const int DotOrderOffset = 4;
    private const int LabelOrderOffset = 5;

    private PreymetTile[,] _tiles;
    private AmusWire[] _wireViews; // one per pair, index-matched to _wires
    private readonly Dictionary<Vector2Int, Transform> _dotAt = new();
    private AmusFx _fx;
    private readonly List<Vector3> _pointBuffer = new();
    private int _active = -1; // pair being dragged, -1 between drags
    private float _timeLimit;
    private float _timeRemaining;
    private bool _finished; // solved or failed; locks the board

    // Helpers.
    private const int SolverConflictBudget = 400000;
    private Transform _parent;
    private bool[] _locked;
    private AmusWire[] _lockViews;
    private List<Vector2Int>[] _solution; // one correct wire per pair, a -> b; null until known
    private Task<int[][]> _solveTask;
    private readonly Stack<List<Vector2Int>[]> _history = new(); // wires before each undoable move
    private List<Vector2Int>[] _dragStart; // wires when the current drag began
    private Coroutine _unfilledFlash;
    private bool _warnedUnfilled;

    /// <summary>Number of colour pairs in this level (for the HUD).</summary>
    public int TotalPairs => _pairs?.Count ?? 0;

    /// <summary>Pairs whose wire currently runs endpoint to endpoint (for the HUD).</summary>
    public int ConnectedPairs
    {
        get
        {
            int connected = 0;
            for (int p = 0; p < TotalPairs; p++)
                if (IsConnected(p)) connected++;
            return connected;
        }
    }

    /// <summary>Whether this level has a countdown (for the HUD).</summary>
    public bool HasTimeLimit => _timeLimit > 0f;

    /// <summary>Seconds left on the countdown (for the HUD).</summary>
    public float TimeRemaining => _timeRemaining;

    /// <summary>Solved or failed; the board no longer takes input.</summary>
    public bool IsFinished => _finished;

    private void Start() => Build();

    private void Update()
    {
        if (_solveTask != null && _solveTask.IsCompleted) TakeSolverResult();

        if (_finished || _timeLimit <= 0f) return;

        _timeRemaining -= Time.deltaTime;
        if (_timeRemaining <= 0f)
        {
            _timeRemaining = 0f;
            _finished = true;
            OnFailed?.Invoke();
        }
    }

    #region Build / layout

    private void Build()
    {
        if (tilePrefab == null)
        {
            Debug.LogError("AmusBoard: Tile prefab is not assigned.", this);
            return;
        }

        if (puzzle == null) puzzle = SelectPuzzle();

        _pairs = puzzle.Parse(out _blocked, out _width, out _height);
        _timeLimit = Mathf.Max(0f, puzzle.timeLimitSeconds);
        _timeRemaining = _timeLimit;

        _wires = new List<Vector2Int>[_pairs.Count];
        for (int p = 0; p < _pairs.Count; p++) _wires[p] = new List<Vector2Int>();
        _locked = new bool[_pairs.Count];
        _lockViews = new AmusWire[_pairs.Count];

        _tiles = new PreymetTile[_width, _height];

        if (autoCellSize)
            cellSize = BoardLayout.FitCellSize(Camera.main, _width, _height, boardScreenFill, maxCellSize);

        Transform parent = boardParent != null ? boardParent : transform;
        _parent = parent;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                var coord = new Vector2Int(x, y);
                PreymetTile tile = Instantiate(tilePrefab, CellToWorld(coord), Quaternion.identity, parent);
                tile.Init(coord, LabelFor(coord));
                tile.SetLabelOrder(wireSortingOrder + LabelOrderOffset); // above the trails and dots
                BoardLayout.FitTileToCell(tile.transform, cellSize, autoFitTiles, tileFill);
                _tiles[x, y] = tile;
            }
        }

        BuildWireViews(parent);
        if (showDots) BuildDots(parent);

        _fx = GetComponent<AmusFx>();
        if (_fx == null) _fx = gameObject.AddComponent<AmusFx>();
        _fx.Init(parent, wireMaterial != null ? wireMaterial : BoardArt.VertexColorMaterial,
            cellSize, cellSize * wireWidth, wireRoundness, sortingLayer, wireSortingOrder);

        LoadSolution();
        RefreshVisuals();
    }

    private void BuildWireViews(Transform parent)
    {
        _wireViews = new AmusWire[_pairs.Count];
        for (int p = 0; p < _pairs.Count; p++)
        {
            string objectName = $"Wire_{_pairs[p].symbol}";
            AmusWire wire = wirePrefab != null
                ? Instantiate(wirePrefab, parent)
                : AmusWire.Create(objectName, parent);

            wire.name = objectName;
            wire.Configure(wireMaterial, PairColor(p), cellSize * wireWidth, wireRoundness,
                sortingLayer, wireSortingOrder);
            _wireViews[p] = wire;
        }
    }

    // Endpoint dots never move or change colour, so they're spawned once and
    // then left alone.
    private void BuildDots(Transform parent)
    {
        for (int p = 0; p < _pairs.Count; p++)
        {
            SpawnDot(parent, _pairs[p].a, p);
            SpawnDot(parent, _pairs[p].b, p);
        }
    }

    private void SpawnDot(Transform parent, Vector2Int cell, int pair)
    {
        var go = new GameObject($"Dot_{_pairs[pair].symbol}_{cell.x}_{cell.y}");
        go.transform.SetParent(parent, false);
        go.transform.position = CellToWorld(cell);
        go.transform.localScale = Vector3.one * (cellSize * dotSize);

        var sprite = go.AddComponent<SpriteRenderer>();
        sprite.sprite = BoardArt.CircleSprite;
        sprite.color = PairColor(pair);
        if (!string.IsNullOrEmpty(sortingLayer)) sprite.sortingLayerName = sortingLayer;
        sprite.sortingOrder = wireSortingOrder + DotOrderOffset; // above the trails, under the letters

        _dotAt[cell] = go.transform;
    }

    private string LabelFor(Vector2Int c)
    {
        if (!showEndpointLetters) return string.Empty;
        int p = EndpointPairAt(c);
        return p >= 0 ? _pairs[p].symbol : string.Empty;
    }

    private AmusPuzzle SelectPuzzle()
    {
        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;

        if (db != null && db.Count > 0)
        {
            if (db.Get(LevelSession.SelectedLevel) is AmusPuzzle chosen)
                return chosen;
        }

        var sample = ScriptableObject.CreateInstance<AmusPuzzle>();
        sample.levelName = "Sample";
        return sample; // default grid above is a valid 4x4
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        float ox = (_width - 1) * 0.5f;
        float oy = (_height - 1) * 0.5f;
        return transform.position + new Vector3((cell.x - ox) * cellSize, (cell.y - oy) * cellSize, 0f);
    }

    public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
    {
        Vector3 local = world - transform.position;
        float ox = (_width - 1) * 0.5f;
        float oy = (_height - 1) * 0.5f;
        int x = Mathf.RoundToInt(local.x / cellSize + ox);
        int y = Mathf.RoundToInt(local.y / cellSize + oy);
        cell = new Vector2Int(x, y);
        return InBounds(cell);
    }

    private bool InBounds(Vector2Int c) => c.x >= 0 && c.x < _width && c.y >= 0 && c.y < _height;

    #endregion

    #region Wire queries

    /// <summary>Pair whose endpoint sits on this cell, or -1.</summary>
    private int EndpointPairAt(Vector2Int cell)
    {
        for (int p = 0; p < _pairs.Count; p++)
            if (_pairs[p].a == cell || _pairs[p].b == cell) return p;
        return -1;
    }

    /// <summary>
    /// Pair that owns this cell, or -1. Endpoints always belong to their pair,
    /// even before any wire touches them, so no other wire can pass through.
    /// </summary>
    private int OwnerOf(Vector2Int cell)
    {
        int endpoint = EndpointPairAt(cell);
        if (endpoint >= 0) return endpoint;

        for (int p = 0; p < _wires.Length; p++)
            if (_wires[p].Contains(cell)) return p;
        return -1;
    }

    private bool IsConnected(int p)
    {
        List<Vector2Int> wire = _wires[p];
        if (wire.Count < 2) return false;
        AmusPuzzle.Pair pair = _pairs[p];
        return (wire[0] == pair.a && wire[^1] == pair.b)
            || (wire[0] == pair.b && wire[^1] == pair.a);
    }

    #endregion

    #region Wire API (called by AmusController)

    /// <summary>
    /// Begin (or resume) a wire at a cell. Returns true if a drag should start:
    /// pressing an endpoint restarts that colour's wire from it; pressing a cell
    /// on an existing wire truncates back to it so the player can re-route.
    /// </summary>
    public bool BeginWire(Vector2Int cell)
    {
        if (_finished || _wires == null || !InBounds(cell)) return false;

        // Taken before anything changes, so EndWire can record the drag for undo.
        _dragStart = Snapshot();

        int endpoint = EndpointPairAt(cell);
        if (endpoint >= 0)
        {
            if (_locked[endpoint]) return RejectLocked();
            PlayErase(endpoint, 0);
            _wires[endpoint].Clear();
            _wires[endpoint].Add(cell);
            _active = endpoint;
            RefreshVisuals();
            PlayStepFeedback(backtrack: false);
            return true;
        }

        for (int p = 0; p < _wires.Length; p++)
        {
            int existing = _wires[p].IndexOf(cell);
            if (existing < 0) continue;
            if (_locked[p]) return RejectLocked();

            int removed = _wires[p].Count - existing - 1;
            PlayErase(p, existing);
            _wires[p].RemoveRange(existing + 1, removed);
            _active = p;
            RefreshVisuals();
            if (removed > 0) PlayStepFeedback(backtrack: true);
            return true;
        }

        return false;
    }

    /// <summary>Extend the active wire to an adjacent cell, or truncate if re-entering it.</summary>
    public void StepTo(Vector2Int cell)
    {
        if (_finished || _active < 0 || !InBounds(cell) || _blocked[cell.x, cell.y]) return;

        List<Vector2Int> wire = _wires[_active];
        Vector2Int head = wire[^1];
        if (cell == head) return;

        // Orthogonal neighbours only.
        if (Mathf.Abs(cell.x - head.x) + Mathf.Abs(cell.y - head.y) != 1) return;

        // Dragging back over the wire's own cells truncates it to that cell
        // (also covers plain single-step undo).
        int own = wire.IndexOf(cell);
        if (own >= 0)
        {
            PlayErase(_active, own);
            wire.RemoveRange(own + 1, wire.Count - own - 1);
            RefreshVisuals();
            PlayStepFeedback(backtrack: true);
            return;
        }

        // A connected wire is terminal: it can only be backed out of, never
        // extended past its endpoint.
        if (IsConnected(_active)) return;

        // Wires never touch: no cell of another wire, no foreign endpoint.
        int owner = OwnerOf(cell);
        if (owner >= 0 && owner != _active) return;

        wire.Add(cell);
        RefreshVisuals();
        PlayStepFeedback(backtrack: false);
        if (IsConnected(_active)) PlayConnect(_active); // this step reached the far endpoint
        CheckSolved();
    }

    public void EndWire()
    {
        _active = -1;

        // One drag = one undo step, and only if it actually changed something.
        if (_dragStart != null && !MatchesCurrent(_dragStart)) _history.Push(_dragStart);
        _dragStart = null;

        CheckSolved();
    }

    // A locked wire can't be picked up; a light tick says "no" without fuss.
    private bool RejectLocked()
    {
        _dragStart = null;
        Haptics.Tick(20);
        return false;
    }

    // Pitch climbs from 1 toward (1 + pitchRise) as more pairs connect, so the
    // player can hear progress toward the win.
    private void PlayStepFeedback(bool backtrack)
    {
        // Ahead of the sound check: a player with audio off should still feel
        // the wire move cell to cell.
        Haptics.Tick(backtrack ? 15 : 25);

        if (SoundManager.Instance == null) return;

        float pitch = 1f;
        if (risingPitch && TotalPairs > 0)
            pitch = 1f + pitchRise * Mathf.Clamp01(ConnectedPairs / (float)TotalPairs);

        if (backtrack) SoundManager.Instance.PlayPathBacktrack(pitch);
        else SoundManager.Instance.PlayPathStep(pitch);
    }

    private void PlayConnect(int p)
    {
        if (_fx == null) return;
        _dotAt.TryGetValue(_pairs[p].a, out Transform dotA);
        _dotAt.TryGetValue(_pairs[p].b, out Transform dotB);
        _fx.PlayConnect(_wireViews[p], PairColor(p), dotA, dotB,
            CellToWorld(_wires[p][0]), CellToWorld(_wires[p][^1]));
    }

    // Hands the stretch of wire p from fromIndex (which stays) to its tail to
    // the erase effect. Call before removing it.
    private void PlayErase(int p, int fromIndex)
    {
        List<Vector2Int> wire = _wires[p];
        if (_fx == null || wire.Count - fromIndex < 2) return;

        _pointBuffer.Clear();
        for (int i = fromIndex; i < wire.Count; i++)
            _pointBuffer.Add(CellToWorld(wire[i]));
        _fx.PlayErase(_pointBuffer, PairColor(p));
    }

    #endregion

    #region Helpers (undo / reset / lock), called by AmusHelperUI

    /// <summary>True while there's a drag or reset to step back from.</summary>
    public bool CanUndo => !_finished && _history.Count > 0;

    /// <summary>
    /// Steps back one drag or reset. Unlimited. Locked wires stay locked; the
    /// restored board is fitted around them.
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo) return false;

        List<Vector2Int>[] previous = _history.Pop();
        for (int p = 0; p < _wires.Length; p++) _wires[p] = new List<Vector2Int>(previous[p]);
        EnforceLocks(playFx: false);
        RefreshVisuals();
        PlayStepFeedback(backtrack: true);
        CheckSolved();
        return true;
    }

    /// <summary>True while any wire the player drew (not a locked one) is on the board.</summary>
    public bool CanReset
    {
        get
        {
            if (_finished || _wires == null) return false;
            for (int p = 0; p < _wires.Length; p++)
                if (!_locked[p] && _wires[p].Count >= 2) return true;
            return false;
        }
    }

    /// <summary>Clears every wire except locked ones. Undoable.</summary>
    public bool ResetBoard()
    {
        if (!CanReset) return false;

        _history.Push(Snapshot());
        for (int p = 0; p < _wires.Length; p++)
        {
            if (_locked[p]) continue;
            PlayErase(p, 0);
            _wires[p].Clear();
        }
        RefreshVisuals();
        PlayStepFeedback(backtrack: true);
        CheckSolved();
        return true;
    }

    /// <summary>
    /// True when a lock can be placed: the solution is known (a big board may
    /// still be solving it) and some pair isn't locked yet.
    /// </summary>
    public bool LockReady
    {
        get
        {
            if (_finished || _solution == null) return false;
            for (int p = 0; p < _locked.Length; p++)
                if (!_locked[p]) return true;
            return false;
        }
    }

    /// <summary>
    /// Lays one pair's wire on its correct path and locks it, cutting back any
    /// wire in the way. Picks the first pair the player doesn't already have
    /// right. False if nothing was locked: spend a helper credit only on true.
    /// Not an undo step; later undos are fitted around the lock instead.
    /// </summary>
    public bool LockOnePath()
    {
        if (!LockReady) return false;

        int pick = -1;
        for (int p = 0; p < _pairs.Count && pick < 0; p++)
            if (!_locked[p] && !SamePath(_wires[p], _solution[p])) pick = p;
        if (pick < 0) return false;

        PlayErase(pick, 0); // the player's own attempt at it, if any
        _locked[pick] = true;
        EnforceLocks(playFx: true);
        RefreshVisuals();
        PlayConnect(pick);
        CheckSolved();
        return true;
    }

    // Puts every locked pair on its solution path and cuts other wires back to
    // just before the first cell they'd share with one.
    private void EnforceLocks(bool playFx)
    {
        var lockedCells = new HashSet<Vector2Int>();
        for (int p = 0; p < _pairs.Count; p++)
        {
            if (!_locked[p]) continue;
            _wires[p] = new List<Vector2Int>(_solution[p]);
            foreach (Vector2Int cell in _solution[p]) lockedCells.Add(cell);
            ShowLockStripe(p);
        }

        for (int q = 0; q < _pairs.Count; q++)
        {
            if (_locked[q]) continue;
            List<Vector2Int> wire = _wires[q];
            int cut = wire.FindIndex(lockedCells.Contains);
            if (cut < 0) continue;
            if (playFx) PlayErase(q, Mathf.Max(0, cut - 1));
            wire.RemoveRange(cut, wire.Count - cut);
        }
    }

    // A thin light stripe down the middle marks a wire as locked.
    private void ShowLockStripe(int p)
    {
        if (_lockViews[p] == null)
        {
            _lockViews[p] = AmusWire.Create($"Lock_{_pairs[p].symbol}", _parent);
            _lockViews[p].Configure(wireMaterial != null ? wireMaterial : BoardArt.VertexColorMaterial,
                lockStripeColor, cellSize * wireWidth * lockStripeWidth, wireRoundness,
                sortingLayer, wireSortingOrder + 1);
        }

        _pointBuffer.Clear();
        foreach (Vector2Int cell in _solution[p]) _pointBuffer.Add(CellToWorld(cell));
        _lockViews[p].SetPoints(_pointBuffer);
    }

    private List<Vector2Int>[] Snapshot()
    {
        var copy = new List<Vector2Int>[_wires.Length];
        for (int p = 0; p < _wires.Length; p++) copy[p] = new List<Vector2Int>(_wires[p]);
        return copy;
    }

    private bool MatchesCurrent(List<Vector2Int>[] snapshot)
    {
        for (int p = 0; p < _wires.Length; p++)
        {
            if (snapshot[p].Count != _wires[p].Count) return false;
            for (int i = 0; i < _wires[p].Count; i++)
                if (snapshot[p][i] != _wires[p][i]) return false;
        }
        return true;
    }

    // Same cells in order, walked either way (the player may start from b).
    private static bool SamePath(List<Vector2Int> wire, List<Vector2Int> path)
    {
        if (wire.Count != path.Count) return false;

        bool forward = true;
        bool backward = true;
        for (int i = 0; i < wire.Count; i++)
        {
            if (wire[i] != path[i]) forward = false;
            if (wire[i] != path[path.Count - 1 - i]) backward = false;
        }
        return forward || backward;
    }

    // The lock helper's answer: the level's baked solution if it still fits
    // the grid, else solved on a worker thread so a big board can't stall a
    // frame. Locks come from this one solution, so they never contradict.
    private void LoadSolution()
    {
        if (puzzle.TryDecodeSolution(_pairs, _blocked, _width, _height, puzzle.mustFillBoard, out _solution))
            return;

        int width = _width;
        int height = _height;
        var blocked = new bool[width * height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                blocked[y * width + x] = _blocked[x, y];

        var endpoints = new int[_pairs.Count * 2];
        for (int p = 0; p < _pairs.Count; p++)
        {
            endpoints[p * 2] = _pairs[p].a.y * width + _pairs[p].a.x;
            endpoints[p * 2 + 1] = _pairs[p].b.y * width + _pairs[p].b.x;
        }
        bool fill = puzzle.mustFillBoard;

        _solveTask = Task.Run(() =>
        {
            AmusSolver.Outcome outcome = AmusSolver.Solve(width, height, blocked, endpoints, fill,
                SolverConflictBudget, out int[][] wires, out _);

            // A level flagged "fill" that can't be filled still gets hints
            // for joining the pairs.
            if (outcome == AmusSolver.Outcome.NoSolution && fill)
                outcome = AmusSolver.Solve(width, height, blocked, endpoints, false,
                    SolverConflictBudget, out wires, out _);

            return outcome == AmusSolver.Outcome.Solved ? wires : null;
        });
    }

    private void TakeSolverResult()
    {
        Task<int[][]> task = _solveTask;
        _solveTask = null;

        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception, this);
            return;
        }

        int[][] wires = task.Result;
        if (wires == null)
        {
            Debug.LogWarning($"AmusBoard: couldn't solve '{puzzle.name}', so the lock helper is off for it.", this);
            return;
        }

        _solution = new List<Vector2Int>[wires.Length];
        for (int p = 0; p < wires.Length; p++)
        {
            _solution[p] = new List<Vector2Int>(wires[p].Length);
            foreach (int cell in wires[p]) _solution[p].Add(new Vector2Int(cell % _width, cell / _width));
        }
    }

    private bool HasEmptyCell()
    {
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (!_blocked[x, y] && OwnerOf(new Vector2Int(x, y)) < 0) return true;
        return false;
    }

    private void FlashEmptyTiles()
    {
        if (!isActiveAndEnabled) return;
        if (_unfilledFlash != null) StopCoroutine(_unfilledFlash);
        _unfilledFlash = StartCoroutine(FlashEmptyTilesRoutine());
        Haptics.Tick(40);
    }

    // Two soft pulses on the tiles no wire covers yet.
    private IEnumerator FlashEmptyTilesRoutine()
    {
        const float duration = 0.8f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float pulse = Mathf.Abs(Mathf.Sin(t / duration * Mathf.PI * 2f));
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (_blocked[x, y] || OwnerOf(cell) >= 0) continue;
                    _tiles[x, y].SetColor(Color.Lerp(ColorFor(cell), unfilledFlashColor, pulse));
                }
            }
            yield return null;
        }

        _unfilledFlash = null;
        RefreshVisuals();
    }

    #endregion

    #region Win check + rendering

    private void CheckSolved()
    {
        if (_finished || TotalPairs == 0) return;

        for (int p = 0; p < TotalPairs; p++)
        {
            if (IsConnected(p)) continue;
            _warnedUnfilled = false;
            return;
        }

        if (puzzle.mustFillBoard && HasEmptyCell())
        {
            // Every pair is joined but it isn't a win yet: show the player why,
            // once per time they get here.
            if (!_warnedUnfilled)
            {
                _warnedUnfilled = true;
                FlashEmptyTiles();
            }
            return;
        }

        _finished = true;
        if (_fx != null && _fx.PlaysWin && isActiveAndEnabled) StartCoroutine(CelebrateThenReport());
        else OnSolved?.Invoke();
    }

    // Runs power through the finished wires, then reports the win. The board is
    // already locked (_finished), so the timer and input are off meanwhile.
    private IEnumerator CelebrateThenReport()
    {
        int n = TotalPairs;
        var paths = new List<Vector3>[n];
        var colors = new Color[n];
        var startDots = new Transform[n];
        var endDots = new Transform[n];

        for (int p = 0; p < n; p++)
        {
            List<Vector2Int> wire = _wires[p];
            paths[p] = new List<Vector3>(wire.Count);
            foreach (Vector2Int cell in wire) paths[p].Add(CellToWorld(cell));
            colors[p] = PairColor(p);
            _dotAt.TryGetValue(wire[0], out startDots[p]);
            _dotAt.TryGetValue(wire[^1], out endDots[p]);
        }

        yield return _fx.PlayWin(paths, colors, startDots, endDots);
        OnSolved?.Invoke();
    }

    private void RefreshVisuals()
    {
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                _tiles[x, y].SetColor(ColorFor(new Vector2Int(x, y)));

        for (int p = 0; p < _wireViews.Length; p++)
        {
            _pointBuffer.Clear();
            foreach (Vector2Int cell in _wires[p])
                _pointBuffer.Add(CellToWorld(cell));
            _wireViews[p].SetPoints(_pointBuffer);
        }
    }

    private Color PairColor(int p) => palette.Length > 0 ? palette[p % palette.Length] : Color.white;

    private Color ColorFor(Vector2Int c)
    {
        if (_blocked[c.x, c.y]) return blockedColor;

        // With dots on, the dot carries the endpoint's colour and the tile stays
        // neutral; with dots off the tile has to show it.
        int endpoint = EndpointPairAt(c);
        if (endpoint >= 0 && !showDots) return PairColor(endpoint);

        if (tintCells)
        {
            int owner = OwnerOf(c);
            if (owner >= 0) return Color.Lerp(PairColor(owner), emptyColor, wireFade);
        }

        return emptyColor;
    }

    #endregion
}
