using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
/// that colour from scratch. The level is solved when every pair is connected,
/// no wire touches itself, and (if the level asks for it) every open cell is
/// covered.
///
/// No touching: a wire may not run alongside itself, double back against
/// itself or wrap around a cell (two of its cells side by side that aren't
/// consecutive steps). The player is free to draw that, but those parts merge
/// into one solid block, and the level can't be finished until the wire is
/// re-routed; when it's otherwise complete, the offending wires blink.
///
/// Erase, connect and win effects live in <see cref="AmusFx"/>, added
/// automatically. The win effect plays before <see cref="OnSolved"/> fires,
/// because the win panel hides the board.
///
/// Helpers (driven by <see cref="AmusHelperUI"/>): <see cref="ShowTip"/> pulses
/// one unfinished wire and marks the tile it should go to next,
/// <see cref="StartRevealPick"/> lets the player tap a tile to reveal its colour
/// and lock it to that colour, <see cref="ResetBoard"/> clears every wire the
/// player drew, and <see cref="LockCorrectWires"/> locks the part of each wire
/// the player has right (all of it, or the right stretch of an unfinished one)
/// and erases the rest. "Right" is judged against one solution, so the helpers
/// never contradict each other; it's read from the level's baked solution, or
/// solved on a worker thread when the level has none.
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
    private Color[] palette = (Color[])DefaultPalette.Clone();

    /// <summary>
    /// The standard pair colours, most distinct first: the first eight are the
    /// originals, the rest keep pairs 9-16 from repeating them.
    /// </summary>
    public static readonly Color[] DefaultPalette =
    {
        new(0.86f, 0.24f, 0.22f), // red
        new(0.25f, 0.45f, 0.90f), // blue
        new(0.95f, 0.80f, 0.25f), // yellow
        new(0.90f, 0.40f, 0.75f), // pink
        new(0.30f, 0.75f, 0.40f), // green
        new(0.95f, 0.55f, 0.25f), // orange
        new(0.35f, 0.80f, 0.85f), // cyan
        new(0.60f, 0.40f, 0.85f), // purple
        new(0.55f, 0.34f, 0.18f), // brown
        new(0.62f, 0.86f, 0.22f), // lime
        new(0.14f, 0.22f, 0.52f), // navy
        new(0.05f, 0.53f, 0.53f), // teal
        new(0.52f, 0.10f, 0.20f), // maroon
        new(0.78f, 0.08f, 0.50f), // magenta
        new(0.80f, 0.68f, 0.48f), // tan
        new(0.50f, 0.52f, 0.56f), // grey
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
    [Tooltip("Wires that touch themselves blink toward this colour when every pair is joined, showing why the level isn't finished.")]
    [SerializeField] private Color selfTouchFlashColor = new(0.12f, 0.12f, 0.16f);

    [Header("Lock helper look")]
    [Tooltip("Stripe drawn down the middle of a locked wire.")]
    [SerializeField] private Color lockStripeColor = new(1f, 1f, 1f, 0.65f);
    [Range(0.05f, 1f)]
    [Tooltip("Stripe width, as a fraction of the wire's width.")]
    [SerializeField] private float lockStripeWidth = 0.35f;

    [Header("Reveal helper look")]
    [Range(0.1f, 1f)]
    [Tooltip("How strongly a revealed tile shows its colour (1 = the full wire colour).")]
    [SerializeField] private float revealTint = 0.6f;

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
    private List<Vector2Int>[] _lockPath; // per pair: its locked stretch, from an endpoint along the answer; null if none
    private AmusWire[] _lockViews;
    private List<Vector2Int>[] _solution; // one correct wire per pair, a -> b; null until known
    private int[,] _answerOwner;          // per cell: its pair in _solution, -1 if none; null until known
    private int[,] _revealed;             // per cell: the pair a Reveal locked it to, -1 if none
    private System.Action _onRevealed;    // set while Reveal waits for the player to pick a tile
    private Task<int[][]> _solveTask;
    private readonly CancellationTokenSource _solveCancel = new(); // stops the solve if the board goes away
    private int _lastTouched = -1; // pair the player last picked up; Tip prefers it
    private Coroutine _unfilledFlash;
    private bool _warnedUnfinished; // the "why isn't this a win" flash already played

    // No-touching rule: the pieces that merge a wire where it touches itself.
    private readonly List<AmusWire> _touchViews = new();
    private readonly List<Color> _touchColors = new(); // each piece's own colour, to blink from
    private readonly List<Vector3> _piecePoints = new();
    private int _touchViewsUsed;
    private bool[] _touchingWire; // per pair, as last drawn
    private int[] _stepAt;        // scratch: each cell's step along the wire being checked, -1 off it
    private Coroutine _touchFlash;

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

    private void OnDestroy() => _solveCancel.Cancel();

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
        _lockPath = new List<Vector2Int>[_pairs.Count];
        _lockViews = new AmusWire[_pairs.Count];
        _revealed = NewCellMap(_width, _height);
        _touchingWire = new bool[_pairs.Count];
        _stepAt = new int[_width * _height];
        for (int i = 0; i < _stepAt.Length; i++) _stepAt[i] = -1;

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
    /// A fully locked wire can't be picked up; a partly locked one is always
    /// carried on from the end of its lock. While Reveal waits for a pick, the
    /// press picks the tile instead.
    /// </summary>
    public bool BeginWire(Vector2Int cell)
    {
        if (_finished || _wires == null || !InBounds(cell)) return false;

        if (_onRevealed != null)
        {
            Reveal(cell);
            return false; // a pick, not a drag
        }

        int endpoint = EndpointPairAt(cell);
        if (endpoint >= 0)
        {
            List<Vector2Int> locked = _lockPath[endpoint];
            if (locked != null)
            {
                // Grabbing the dot the lock starts from carries on after the
                // lock; the other dot can't start a second stretch.
                if (IsFullyLocked(endpoint) || cell != locked[0]) return RejectLocked();
                return ResumeAfterLock(endpoint);
            }

            PlayErase(endpoint, 0);
            _wires[endpoint].Clear();
            _wires[endpoint].Add(cell);
            _active = _lastTouched = endpoint;
            RefreshVisuals();
            PlayStepFeedback(backtrack: false);
            return true;
        }

        for (int p = 0; p < _wires.Length; p++)
        {
            int existing = _wires[p].IndexOf(cell);
            if (existing < 0) continue;
            if (IsFullyLocked(p)) return RejectLocked();
            if (existing < LockedCount(p) - 1) return ResumeAfterLock(p);

            int removed = _wires[p].Count - existing - 1;
            PlayErase(p, existing);
            _wires[p].RemoveRange(existing + 1, removed);
            _active = _lastTouched = p;
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
        // (also covers plain single-step undo), but never into its locked part.
        int own = wire.IndexOf(cell);
        if (own >= 0)
        {
            if (own < LockedCount(_active) - 1) return;
            PlayErase(_active, own);
            wire.RemoveRange(own + 1, wire.Count - own - 1);
            RefreshVisuals();
            PlayStepFeedback(backtrack: true);
            return;
        }

        // A connected wire is terminal: it can only be backed out of, never
        // extended past its endpoint.
        if (IsConnected(_active)) return;

        // Wires never touch: no cell of another wire, no foreign endpoint, and
        // no tile a Reveal locked to another colour.
        int owner = OwnerOf(cell);
        if (owner >= 0 && owner != _active) return;
        int revealed = _revealed[cell.x, cell.y];
        if (revealed >= 0 && revealed != _active) return;

        wire.Add(cell);
        RefreshVisuals();
        PlayStepFeedback(backtrack: false);
        if (IsConnected(_active)) PlayConnect(_active); // this step reached the far endpoint
        CheckSolved();
    }

    public void EndWire()
    {
        _active = -1;
        CheckSolved();
    }

    // A helper button can be tapped with a second finger mid-drag. The helpers
    // rewrite wires, so the drag ends there and later steps are ignored.
    private void CancelDrag() => _active = -1;

    // A locked wire can't be picked up; a light tick says "no" without fuss.
    private bool RejectLocked()
    {
        Haptics.Tick(20);
        return false;
    }

    // Starts a drag on a partly locked wire from the end of its lock, cutting
    // off anything drawn past it (like grabbing any wire mid-way).
    private bool ResumeAfterLock(int p)
    {
        int head = LockedCount(p) - 1;
        int removed = _wires[p].Count - head - 1;
        PlayErase(p, head);
        _wires[p].RemoveRange(head + 1, removed);
        _active = _lastTouched = p;
        RefreshVisuals();
        if (removed > 0) PlayStepFeedback(backtrack: true);
        return true;
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
    private void PlayErase(int p, int fromIndex) => PlayErase(p, fromIndex, _wires[p].Count - 1);

    // Same for any stretch, walked from 'keptIndex' (the cell that stays) to
    // 'tipIndex' in either direction along the wire.
    private void PlayErase(int p, int keptIndex, int tipIndex)
    {
        if (_fx == null || keptIndex < 0 || tipIndex < 0 || Mathf.Abs(tipIndex - keptIndex) < 1) return;

        List<Vector2Int> wire = _wires[p];
        int step = tipIndex > keptIndex ? 1 : -1;
        _pointBuffer.Clear();
        for (int i = keptIndex; i != tipIndex + step; i += step)
            _pointBuffer.Add(CellToWorld(wire[i]));
        _fx.PlayErase(_pointBuffer, PairColor(p));
    }

    #endregion

    #region Helpers (tip / reveal / reset / lock), called by AmusHelperUI

    /// <summary>
    /// True when Tip has something to show: the solution is known (a big board
    /// may still be solving it) and some wire isn't finished right yet.
    /// </summary>
    public bool TipReady => !_finished && TipPair() >= 0;

    /// <summary>
    /// The Tip helper. Picks one wire that isn't finished right (the one the
    /// player last picked up, else the first), pulses it, and marks the tile
    /// it should go to next, counted from the last cell it has right (from its
    /// dot, if it isn't started). Changes no wires. False if there's nothing to
    /// show: spend a helper credit only on true.
    /// </summary>
    public bool ShowTip()
    {
        int p = TipPair();
        if (_finished || p < 0) return false;

        // Read the answer from the dot the wire starts at (dot a when it has
        // none yet). Its first cell is that dot, so at least one cell is right.
        List<Vector2Int> wire = _wires[p];
        List<Vector2Int> answer = _solution[p];
        bool reversed = wire.Count > 0 && answer[0] != wire[0];
        int right = Mathf.Max(1, MatchingCells(wire, answer, fromTail: false));

        Vector2Int last = AnswerStep(answer, right - 1, reversed);
        Vector2Int next = AnswerStep(answer, right, reversed);
        _dotAt.TryGetValue(AnswerStep(answer, 0, reversed), out Transform dot);
        if (_fx != null) _fx.PlayTip(_wireViews[p], PairColor(p), dot, CellToWorld(last), CellToWorld(next));
        return true;
    }

    // The wire to tip: the one the player last picked up if it isn't finished
    // right, else the first that isn't; -1 when there's none (or no answer yet).
    private int TipPair()
    {
        if (_solution == null) return -1;
        if (_lastTouched >= 0 && !IsRight(_lastTouched)) return _lastTouched;
        for (int p = 0; p < _pairs.Count; p++)
            if (!IsRight(p)) return p;
        return -1;
    }

    // Wire p runs the whole answer, dot to dot.
    private bool IsRight(int p) => MatchingCells(_wires[p], _solution[p], fromTail: false) == _solution[p].Count;

    private static Vector2Int AnswerStep(List<Vector2Int> answer, int step, bool reversed) =>
        reversed ? answer[answer.Count - 1 - step] : answer[step];

    /// <summary>True when Reveal can be used: the solution is known (a big board may still be solving it).</summary>
    public bool RevealReady => !_finished && _answerOwner != null;

    /// <summary>True while Reveal waits for the player to tap a tile.</summary>
    public bool PickingReveal => _onRevealed != null && !_finished;

    /// <summary>
    /// The Reveal helper: the next tap on the board picks a tile instead of
    /// drawing. A tile whose colour isn't known yet shows the colour the
    /// answer gives it and is locked to it: another colour's wire through it
    /// is cut back, and only its own colour may cross it from then on. Then
    /// <paramref name="onRevealed"/> runs (spend the helper credit there).
    /// Taps on dots, walls or tiles already shown by a reveal or lock just
    /// tick, and the board keeps waiting.
    /// </summary>
    public void StartRevealPick(System.Action onRevealed)
    {
        if (!RevealReady) return;
        CancelDrag();
        _onRevealed = onRevealed;
    }

    /// <summary>Stops waiting for a Reveal pick; nothing is spent.</summary>
    public void CancelRevealPick() => _onRevealed = null;

    private void Reveal(Vector2Int cell)
    {
        if (!CanReveal(cell))
        {
            Haptics.Tick(20);
            return;
        }

        int p = _answerOwner[cell.x, cell.y];
        _revealed[cell.x, cell.y] = p;

        // Another colour's wire through it is cut back to just before it.
        for (int q = 0; q < _wires.Length; q++)
        {
            int at = q == p ? -1 : _wires[q].IndexOf(cell);
            if (at < 0) continue;
            PlayErase(q, at - 1);
            _wires[q].RemoveRange(at, _wires[q].Count - at);
        }

        RefreshVisuals();
        if (_fx != null) _fx.PlayReveal(CellToWorld(cell), PairColor(p));

        System.Action revealed = _onRevealed;
        _onRevealed = null;
        revealed();
        CheckSolved();
    }

    // A tile Reveal can show: open, not a dot, part of the answer, and its
    // colour not already shown by an earlier reveal or a lock.
    private bool CanReveal(Vector2Int cell)
    {
        if (_answerOwner == null || _blocked[cell.x, cell.y] || EndpointPairAt(cell) >= 0) return false;
        if (_revealed[cell.x, cell.y] >= 0) return false;

        int p = _answerOwner[cell.x, cell.y];
        return p >= 0 && (_lockPath[p] == null || !_lockPath[p].Contains(cell));
    }

    /// <summary>True while the player has drawn anything that isn't locked.</summary>
    public bool CanReset
    {
        get
        {
            if (_finished || _wires == null) return false;
            for (int p = 0; p < _wires.Length; p++)
                if (HasLooseCells(p)) return true;
            return false;
        }
    }

    /// <summary>Clears everything the player drew except locked stretches.</summary>
    public bool ResetBoard()
    {
        if (!CanReset) return false;

        CancelDrag();
        for (int p = 0; p < _wires.Length; p++)
        {
            if (!HasLooseCells(p)) continue;
            int keep = LockedCount(p);
            PlayErase(p, Mathf.Max(0, keep - 1));
            _wires[p].RemoveRange(keep, _wires[p].Count - keep);
        }
        RefreshVisuals();
        PlayStepFeedback(backtrack: true);
        CheckSolved();
        return true;
    }

    /// <summary>
    /// True when Lock has something to check: the solution is known (a big
    /// board may still be solving it) and the player has drawn something that
    /// isn't locked yet.
    /// </summary>
    public bool LockReady => _solution != null && CanReset;

    /// <summary>
    /// The Lock helper. Each wire's right part is locked: a wire joined end to
    /// end along the answer locks whole, and an unfinished or wrong one locks
    /// the stretch from its starting dot that follows the answer. Everything
    /// past that is erased. False if the press changed nothing: spend a
    /// helper credit only on true.
    /// </summary>
    public bool LockCorrectWires()
    {
        if (!LockReady) return false;

        CancelDrag();
        var newlyLocked = new List<int>();
        bool erased = false;

        for (int p = 0; p < _pairs.Count; p++)
        {
            if (!HasLooseCells(p)) continue;

            List<Vector2Int> wire = _wires[p];
            List<Vector2Int> right = RightPart(p, out bool fromTail);
            int keep = right?.Count ?? 0;

            if (keep > LockedCount(p))
            {
                _lockPath[p] = right;
                newlyLocked.Add(p);
            }

            if (keep < wire.Count)
            {
                // Erase the rest, from the last cell that stays out to the tip.
                if (fromTail) PlayErase(p, wire.Count - keep, 0);
                else PlayErase(p, Mathf.Max(0, keep - 1));
                erased = true;
            }
            _wires[p] = keep > 0 ? new List<Vector2Int>(right) : new List<Vector2Int>();
        }

        if (newlyLocked.Count == 0 && !erased) return false;

        foreach (int p in newlyLocked) ShowLockStripe(p);
        RefreshVisuals();
        if (newlyLocked.Count > 0) PlayLockFx(newlyLocked);
        else Haptics.Tick(30); // nothing was right; the erase says the rest
        CheckSolved();
        return true;
    }

    // The longest stretch of wire p, read from the dot it starts at, that
    // follows the answer; for a wire joined at both dots, read from whichever
    // dot gives more (unless a lock already fixes the direction). Null if
    // nothing past the dot itself is right.
    private List<Vector2Int> RightPart(int p, out bool fromTail)
    {
        List<Vector2Int> wire = _wires[p];
        int best = MatchingCells(wire, _solution[p], fromTail: false);
        fromTail = false;

        if (_lockPath[p] == null && IsConnected(p))
        {
            int fromOtherEnd = MatchingCells(wire, _solution[p], fromTail: true);
            if (fromOtherEnd > best)
            {
                best = fromOtherEnd;
                fromTail = true;
            }
        }

        if (best < 2)
        {
            fromTail = false;
            return null;
        }
        var part = new List<Vector2Int>(best);
        for (int i = 0; i < best; i++) part.Add(fromTail ? wire[wire.Count - 1 - i] : wire[i]);
        return part;
    }

    // How many cells of the wire, read from its first cell (or its last),
    // match the answer read from the same dot.
    private static int MatchingCells(List<Vector2Int> wire, List<Vector2Int> answer, bool fromTail)
    {
        if (wire.Count == 0) return 0;
        Vector2Int start = fromTail ? wire[^1] : wire[0];
        bool answerForward = answer[0] == start;
        if (!answerForward && answer[^1] != start) return 0;

        int n = 0;
        while (n < wire.Count && n < answer.Count)
        {
            Vector2Int cell = fromTail ? wire[wire.Count - 1 - n] : wire[n];
            Vector2Int expected = answerForward ? answer[n] : answer[answer.Count - 1 - n];
            if (cell != expected) break;
            n++;
        }
        return n;
    }

    private int LockedCount(int p) => _lockPath[p]?.Count ?? 0;

    private bool IsFullyLocked(int p) => _lockPath[p] != null && _lockPath[p].Count == _solution[p].Count;

    // Drawn cells past the wire's lock (or any wire at all, if unlocked).
    private bool HasLooseCells(int p) => _wires[p].Count > Mathf.Max(LockedCount(p), 1);

    private void PlayLockFx(List<int> pairs)
    {
        if (_fx == null) return;

        var wires = new List<AmusWire>(pairs.Count);
        var colors = new List<Color>(pairs.Count);
        var dots = new List<Transform>(pairs.Count * 2);
        foreach (int p in pairs)
        {
            wires.Add(_wireViews[p]);
            colors.Add(PairColor(p));

            // The far dot only pops if the lock reaches it.
            _dotAt.TryGetValue(_lockPath[p][0], out Transform start);
            dots.Add(start);
            if (IsFullyLocked(p) && _dotAt.TryGetValue(_lockPath[p][^1], out Transform end)) dots.Add(end);
        }
        _fx.PlayLock(wires, colors, dots);
    }

    // A thin light stripe down the middle marks the locked stretch.
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
        foreach (Vector2Int cell in _lockPath[p]) _pointBuffer.Add(CellToWorld(cell));
        _lockViews[p].SetPoints(_pointBuffer);
    }

    // The helpers' answer: the level's baked solution if it still fits the
    // grid, else solved on a worker thread so a big board can't stall a frame.
    // Every helper works from this one solution, so they never contradict.
    private void LoadSolution()
    {
        if (puzzle.TryDecodeSolution(_pairs, _blocked, _width, _height, puzzle.mustFillBoard,
                out List<Vector2Int>[] baked))
        {
            SetSolution(baked);
            return;
        }

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
        CancellationToken cancel = _solveCancel.Token;

        _solveTask = Task.Run(() =>
        {
            AmusSolver.Outcome outcome = AmusSolver.Solve(width, height, blocked, endpoints, fill,
                SolverConflictBudget, out int[][] wires, out _, cancel: cancel);

            // A level flagged "fill" that can't be filled still gets hints
            // for joining the pairs.
            if (outcome == AmusSolver.Outcome.NoSolution && fill)
                outcome = AmusSolver.Solve(width, height, blocked, endpoints, false,
                    SolverConflictBudget, out wires, out _, cancel: cancel);

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

        var solution = new List<Vector2Int>[wires.Length];
        for (int p = 0; p < wires.Length; p++)
        {
            solution[p] = new List<Vector2Int>(wires[p].Length);
            foreach (int cell in wires[p]) solution[p].Add(new Vector2Int(cell % _width, cell / _width));
        }
        SetSolution(solution);
    }

    // Keeps the answer, and notes which pair each tile belongs to in it.
    private void SetSolution(List<Vector2Int>[] solution)
    {
        _solution = solution;
        _answerOwner = NewCellMap(_width, _height);
        for (int p = 0; p < solution.Length; p++)
            foreach (Vector2Int cell in solution[p]) _answerOwner[cell.x, cell.y] = p;
    }

    private static int[,] NewCellMap(int width, int height)
    {
        var map = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                map[x, y] = -1;
        return map;
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
            _warnedUnfinished = false;
            return;
        }

        // Every pair is joined, but it's only a win with no wire touching
        // itself and, if the level asks, the board full. If not, show the
        // player why, once per time they get here.
        bool unfilled = puzzle.mustFillBoard && HasEmptyCell();
        bool touching = AnyWireTouchesItself();
        if (unfilled || touching)
        {
            if (!_warnedUnfinished)
            {
                _warnedUnfinished = true;
                if (unfilled) FlashEmptyTiles();
                if (touching) FlashSelfTouches();
                Haptics.Tick(40);
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

        DrawSelfTouches();
    }

    private Color PairColor(int p) => palette.Length > 0 ? palette[p % palette.Length] : Color.white;

    private Color ColorFor(Vector2Int c)
    {
        if (_blocked[c.x, c.y]) return blockedColor;

        // With dots on, the dot carries the endpoint's colour and the tile stays
        // neutral; with dots off the tile has to show it.
        int endpoint = EndpointPairAt(c);
        if (endpoint >= 0 && !showDots) return PairColor(endpoint);

        // A revealed tile shows the colour the answer gives it.
        int revealed = _revealed[c.x, c.y];
        if (revealed >= 0) return Color.Lerp(emptyColor, PairColor(revealed), revealTint);

        if (tintCells)
        {
            int owner = OwnerOf(c);
            if (owner >= 0) return Color.Lerp(PairColor(owner), emptyColor, wireFade);
        }

        return emptyColor;
    }

    #endregion

    #region No-touching rule

    private bool AnyWireTouchesItself()
    {
        foreach (List<Vector2Int> wire in _wires)
            if (AmusPuzzle.TouchesItself(wire)) return true;
        return false;
    }

    // Where a wire touches itself its parts merge: a bar joins each pair of
    // side-by-side cells that aren't consecutive steps, and every 2x2 block the
    // wire fully covers is filled in, so that stretch reads as one solid block.
    private void DrawSelfTouches()
    {
        StopTouchFlash(); // the wires changed; a blink in progress may be stale
        _touchViewsUsed = 0;
        float thickness = cellSize * wireWidth;

        for (int p = 0; p < _wires.Length; p++)
        {
            List<Vector2Int> wire = _wires[p];
            _touchingWire[p] = false;
            if (wire.Count < 4) continue; // too short to touch itself

            for (int i = 0; i < wire.Count; i++) _stepAt[wire[i].y * _width + wire[i].x] = i;

            Color color = PairColor(p);
            for (int i = 0; i < wire.Count; i++)
            {
                Vector2Int cell = wire[i];

                // Each side-by-side pair once: look right and up.
                for (int side = 0; side < 2; side++)
                {
                    Vector2Int next = cell + (side == 0 ? Vector2Int.right : Vector2Int.up);
                    int j = StepAt(next);
                    if (j < 0 || Mathf.Abs(i - j) == 1) continue;
                    _touchingWire[p] = true;
                    DrawTouchPiece(color, thickness, wireRoundness, CellToWorld(cell), CellToWorld(next));
                }

                // A 2x2 block all on this wire: fill the square between the four centres.
                if (StepAt(cell + Vector2Int.right) >= 0 && StepAt(cell + Vector2Int.up) >= 0 &&
                    StepAt(cell + Vector2Int.one) >= 0)
                {
                    Vector3 leftMiddle = CellToWorld(cell) + new Vector3(0f, cellSize * 0.5f, 0f);
                    DrawTouchPiece(color, cellSize, 0, leftMiddle, leftMiddle + new Vector3(cellSize, 0f, 0f));
                }
            }

            foreach (Vector2Int cell in wire) _stepAt[cell.y * _width + cell.x] = -1;
        }

        for (int k = _touchViewsUsed; k < _touchViews.Count; k++) _touchViews[k].SetPoints(null);
    }

    private int StepAt(Vector2Int cell) => InBounds(cell) ? _stepAt[cell.y * _width + cell.x] : -1;

    // One merge piece: a straight stroke in the wire's colour and material.
    // Bars use the wire's thickness and round ends; squares are a stroke one
    // cell thick with flat ends.
    private void DrawTouchPiece(Color color, float width, int roundness, Vector3 from, Vector3 to)
    {
        if (_touchViewsUsed == _touchViews.Count)
        {
            _touchViews.Add(AmusWire.Create($"Touch_{_touchViews.Count}", _parent));
            _touchColors.Add(color);
        }

        _touchColors[_touchViewsUsed] = color;
        AmusWire piece = _touchViews[_touchViewsUsed++];
        piece.Configure(wireMaterial, color, width, roundness, sortingLayer, wireSortingOrder);
        _piecePoints.Clear();
        _piecePoints.Add(from);
        _piecePoints.Add(to);
        piece.SetPoints(_piecePoints);
    }

    private void FlashSelfTouches()
    {
        if (!isActiveAndEnabled) return;
        StopTouchFlash();
        _touchFlash = StartCoroutine(FlashSelfTouchesRoutine());
    }

    // Two blinks on every wire that touches itself, merged parts included.
    private IEnumerator FlashSelfTouchesRoutine()
    {
        const float duration = 0.8f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float pulse = 0.7f * Mathf.Abs(Mathf.Sin(t / duration * Mathf.PI * 2f));
            for (int p = 0; p < _wireViews.Length; p++)
                if (_touchingWire[p]) _wireViews[p].SetColor(Color.Lerp(PairColor(p), selfTouchFlashColor, pulse));
            for (int k = 0; k < _touchViewsUsed; k++)
                _touchViews[k].SetColor(Color.Lerp(_touchColors[k], selfTouchFlashColor, pulse));
            yield return null;
        }

        _touchFlash = null;
        RestoreTouchColors();
    }

    private void StopTouchFlash()
    {
        if (_touchFlash == null) return;
        StopCoroutine(_touchFlash);
        _touchFlash = null;
        RestoreTouchColors();
    }

    private void RestoreTouchColors()
    {
        for (int p = 0; p < _wireViews.Length; p++)
            if (_touchingWire[p]) _wireViews[p].SetColor(PairColor(p));
        for (int k = 0; k < _touchViewsUsed; k++) _touchViews[k].SetColor(_touchColors[k]);
    }

    #endregion
}
