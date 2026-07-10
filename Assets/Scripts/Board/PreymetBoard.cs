using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Owns a Preymet (path-tracing) board: builds tiles from a prefab, manages the
/// path the player traces, and reports a win.
///
/// Rules enforced while tracing (so any traced path is automatically legal):
///   * Orthogonal steps only (no diagonals).
///   * No revisiting a cell (the path can't cross itself).
///   * Walls (blocked cells) can't be entered.
/// The level is solved when the path runs from Start to Goal using exactly the
/// level's target number of tiles.
/// </summary>
[DisallowMultipleComponent]
public class PreymetBoard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PreymetTile tilePrefab;
    [SerializeField] private Transform boardParent;
    [Tooltip("Fallback level used only when testing this scene directly.")]
    [SerializeField] private LevelDatabase fallbackDatabase;
    [Tooltip("Optional: force a single puzzle (overrides everything). Handy for testing.")]
    [SerializeField] private PreymetPuzzle puzzle;

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
    [SerializeField] private Color startColor = new(0.55f, 0.85f, 0.55f);
    [SerializeField] private Color goalColor = new(0.95f, 0.70f, 0.40f);
    [SerializeField] private Color pathColor = new(0.50f, 0.70f, 1.00f);

    [Header("Events")]
    public UnityEvent OnSolved;
    [Tooltip("Fired when the time limit runs out before solving.")]
    public UnityEvent OnFailed;

    private int _width;
    private int _height;
    private bool[,] _blocked;
    private Vector2Int _start;
    private Vector2Int _goal;
    private int _target;

    private PreymetTile[,] _tiles;
    private readonly List<Vector2Int> _path = new();
    private float _timeLimit;
    private float _timeRemaining;
    private bool _finished; // solved or failed; locks the board

    /// <summary>Exact tile count the path must reach (for the HUD).</summary>
    public int TargetTiles => _target;

    /// <summary>Current number of tiles in the traced path (for the HUD).</summary>
    public int PathLength => _path.Count;

    /// <summary>Whether this level has a countdown (for the HUD).</summary>
    public bool HasTimeLimit => _timeLimit > 0f;

    /// <summary>Seconds left on the countdown (for the HUD).</summary>
    public float TimeRemaining => _timeRemaining;

    private void Start() => Build();

    private void Update()
    {
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
            Debug.LogError("PreymetBoard: Tile prefab is not assigned.", this);
            return;
        }

        if (puzzle == null) puzzle = SelectPuzzle();

        puzzle.Parse(out _blocked, out _start, out _goal, out _width, out _height);
        _target = Mathf.Max(2, puzzle.targetTiles);
        _timeLimit = Mathf.Max(0f, puzzle.timeLimitSeconds);
        _timeRemaining = _timeLimit;
        _tiles = new PreymetTile[_width, _height];

        if (autoCellSize)
            cellSize = BoardLayout.FitCellSize(Camera.main, _width, _height, boardScreenFill, maxCellSize);

        Transform parent = boardParent != null ? boardParent : transform;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                var coord = new Vector2Int(x, y);
                PreymetTile tile = Instantiate(tilePrefab, CellToWorld(coord), Quaternion.identity, parent);
                tile.Init(coord, LabelFor(coord));
                BoardLayout.FitTileToCell(tile.transform, cellSize, autoFitTiles, tileFill);
                _tiles[x, y] = tile;
            }
        }

        RefreshColors();
    }

    private string LabelFor(Vector2Int c)
    {
        if (c == _start) return "S";
        if (c == _goal) return "G";
        return string.Empty;
    }

    private PreymetPuzzle SelectPuzzle()
    {
        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;

        if (db != null && db.Count > 0)
        {
            if (db.Get(LevelSession.SelectedLevel) is PreymetPuzzle chosen)
                return chosen;
        }

        var sample = ScriptableObject.CreateInstance<PreymetPuzzle>();
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

    #region Path API (called by PreymetController)

    /// <summary>
    /// Begin (or resume) a path at a cell. Returns true if a drag should start:
    /// pressing the Start cell begins a fresh path; pressing a cell already on
    /// the path truncates back to it so the player can re-route from there.
    /// </summary>
    public bool BeginPath(Vector2Int cell)
    {
        if (_finished || !InBounds(cell)) return false;

        if (cell == _start)
        {
            _path.Clear();
            _path.Add(_start);
            RefreshColors();
            return true;
        }

        int existing = _path.IndexOf(cell);
        if (existing >= 0)
        {
            _path.RemoveRange(existing + 1, _path.Count - existing - 1);
            RefreshColors();
            return true;
        }

        return false;
    }

    /// <summary>Extend the path to an adjacent cell, or backtrack if stepping back.</summary>
    public void StepTo(Vector2Int cell)
    {
        if (_finished || _path.Count == 0 || !InBounds(cell) || _blocked[cell.x, cell.y]) return;

        Vector2Int head = _path[^1];
        if (cell == head) return;

        // Orthogonal neighbours only.
        if (Mathf.Abs(cell.x - head.x) + Mathf.Abs(cell.y - head.y) != 1) return;

        // Stepping onto the previous cell undoes the last move.
        if (_path.Count >= 2 && cell == _path[^2])
        {
            _path.RemoveAt(_path.Count - 1);
            RefreshColors();
            return;
        }

        if (_path.Contains(cell)) return; // would cross itself

        _path.Add(cell);
        RefreshColors();
        CheckSolved();
    }

    public void EndPath() => CheckSolved();

    #endregion

    #region Win check + rendering

    private void CheckSolved()
    {
        if (_finished) return;
        if (_path.Count != _target) return;
        if (_path[0] != _start || _path[^1] != _goal) return;

        _finished = true;
        OnSolved?.Invoke();
    }

    private void RefreshColors()
    {
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                _tiles[x, y].SetColor(ColorFor(new Vector2Int(x, y)));
    }

    private Color ColorFor(Vector2Int c)
    {
        if (_blocked[c.x, c.y]) return blockedColor;
        if (c == _start) return startColor;
        if (c == _goal) return goalColor;
        if (_path.Contains(c)) return pathColor;
        return emptyColor;
    }

    #endregion
}
