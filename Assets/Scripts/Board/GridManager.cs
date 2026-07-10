using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Owns the Shikaku board: builds tiles from a prefab, manages the rectangles
/// the player draws, validates them against Shikaku rules, and reports a win.
///
/// Shikaku rules enforced:
///   * The board is partitioned into axis-aligned rectangles.
///   * Each rectangle contains exactly one clue.
///   * A rectangle's area equals its clue's number.
///   * The puzzle is solved when every cell belongs to a valid rectangle.
/// </summary>
[DisallowMultipleComponent]
public class GridManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Tile tilePrefab;
    [Tooltip("Optional parent for spawned tiles. Defaults to this transform.")]
    [SerializeField] private Transform boardParent;
    [Tooltip("Optional LineRenderer drawn as a marquee outline around the current selection.")]
    [SerializeField] private LineRenderer selectionBox;
    [Tooltip("Fallback levels used only when testing the game scene directly.")]
    [SerializeField] private LevelDatabase fallbackDatabase;
    [Tooltip("Optional: force a single puzzle (overrides everything). Handy for testing.")]
    [SerializeField] private ShikakuPuzzle puzzle;

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
    [SerializeField] private Color clueColor = new(0.80f, 0.86f, 1.00f);
    [SerializeField] private Color previewColor = new(1.00f, 0.95f, 0.55f);
    [SerializeField] private Color validColor = new(0.60f, 0.85f, 0.60f);
    [SerializeField] private Color invalidColor = new(0.90f, 0.55f, 0.55f);

    [Header("Feedback")]
    [Tooltip("Played when a rectangle is placed.")]
    [SerializeField] private AudioClip placeSound;
    [Tooltip("Played when a placed rectangle is correct (falls back to place sound).")]
    [SerializeField] private AudioClip validSound;
    [Tooltip("Played when a rectangle is erased.")]
    [SerializeField] private AudioClip eraseSound;
    [Tooltip("Scale-pop the tiles of a rectangle when it's placed.")]
    [SerializeField] private bool animateSelection = true;
    [SerializeField] private float popStrength = 1.15f;
    [SerializeField] private float popDuration = 0.15f;

    [Header("Events")]
    public UnityEvent OnSolved;

    /// <summary>A committed rectangle and its current validity.</summary>
    private class ShikakuRect
    {
        public RectInt bounds;
        public bool valid;

        public ShikakuRect(RectInt bounds) => this.bounds = bounds;
    }

    private int _width;
    private int _height;
    private int[,] _clues;            // 0 = no clue
    private Tile[,] _tiles;
    private ShikakuRect[,] _owner;    // which rect owns each cell (null = none)
    private readonly List<ShikakuRect> _rects = new();

    private bool _hasPreview;
    private RectInt _previewBounds;
    private bool _solved;

    private void Start() => Build();

    #region Build / layout

    private void Build()
    {
        if (tilePrefab == null)
        {
            Debug.LogError("GridManager: Tile prefab is not assigned.", this);
            return;
        }

        if (puzzle == null) puzzle = SelectPuzzle();

        _clues = puzzle.BuildClueGrid();
        _width = _clues.GetLength(0);
        _height = _clues.GetLength(1);
        _tiles = new Tile[_width, _height];
        _owner = new ShikakuRect[_width, _height];

        if (autoCellSize)
            cellSize = BoardLayout.FitCellSize(Camera.main, _width, _height, boardScreenFill, maxCellSize);

        Transform parent = boardParent != null ? boardParent : transform;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                var coord = new Vector2Int(x, y);
                Tile tile = Instantiate(tilePrefab, CellToWorld(coord), Quaternion.identity, parent);
                tile.Init(coord, _clues[x, y]);
                BoardLayout.FitTileToCell(tile.transform, cellSize, autoFitTiles, tileFill);
                _tiles[x, y] = tile;
            }
        }

        RefreshColors();
        HideSelectionBox();
    }

    /// <summary>Picks the level chosen on the level-select screen, or a sample.</summary>
    private ShikakuPuzzle SelectPuzzle()
    {
        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;

        if (db != null && db.Count > 0)
        {
            if (db.Get(LevelSession.SelectedLevel) is ShikakuPuzzle chosen)
                return chosen;
        }
        return ShikakuPuzzle.CreateSample();
    }

    /// <summary>World position of a cell's centre (board is centred on this object).</summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        float ox = (_width - 1) * 0.5f;
        float oy = (_height - 1) * 0.5f;
        return transform.position + new Vector3((cell.x - ox) * cellSize, (cell.y - oy) * cellSize, 0f);
    }

    /// <summary>Converts a world point to a cell. Returns false if outside the grid.</summary>
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

    private Vector2Int Clamp(Vector2Int c) => new(
        Mathf.Clamp(c.x, 0, _width - 1),
        Mathf.Clamp(c.y, 0, _height - 1));

    private static RectInt BoundsFrom(Vector2Int a, Vector2Int b)
    {
        int xMin = Mathf.Min(a.x, b.x);
        int yMin = Mathf.Min(a.y, b.y);
        return new RectInt(xMin, yMin, Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);
    }

    #endregion

    #region Selection API (called by PlayerController)

    /// <summary>Preview a rectangle between two (possibly out-of-range) cells.</summary>
    public void ShowPreview(Vector2Int a, Vector2Int b)
    {
        _previewBounds = BoundsFrom(Clamp(a), Clamp(b));
        _hasPreview = true;
        RefreshColors();
        UpdateSelectionBox(_previewBounds);
    }

    public void ClearPreview()
    {
        _hasPreview = false;
        RefreshColors();
        HideSelectionBox();
    }

    /// <summary>
    /// Commit the player's selection. A single-cell selection on an existing
    /// rectangle erases it; otherwise a new rectangle is placed, replacing any
    /// rectangles it overlaps.
    /// </summary>
    public void CommitSelection(Vector2Int a, Vector2Int b)
    {
        _hasPreview = false;
        HideSelectionBox();
        RectInt bounds = BoundsFrom(Clamp(a), Clamp(b));

        // Tap on an owned cell => erase that rectangle.
        if (bounds.width == 1 && bounds.height == 1)
        {
            ShikakuRect tapped = _owner[bounds.xMin, bounds.yMin];
            if (tapped != null)
            {
                RemoveRect(tapped);
                RefreshColors();
                PlaySound(eraseSound);
                CheckSolved();
                return;
            }
        }

        RemoveOverlapping(bounds);

        var rect = new ShikakuRect(bounds);
        _rects.Add(rect);
        SetOwner(rect, rect);
        Validate(rect);

        RefreshColors();
        AnimateRect(bounds);
        PlaySound(rect.valid && validSound != null ? validSound : placeSound);
        CheckSolved();
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfx(clip);
    }

    private void AnimateRect(RectInt bounds)
    {
        if (!animateSelection) return;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
                _tiles[x, y].PlayPop(popStrength, popDuration);
    }

    // Draws the marquee outline around the selected region (world space).
    private void UpdateSelectionBox(RectInt b)
    {
        if (selectionBox == null) return;

        float half = cellSize * 0.5f;
        Vector3 min = CellToWorld(new Vector2Int(b.xMin, b.yMin)) + new Vector3(-half, -half, 0f);
        Vector3 max = CellToWorld(new Vector2Int(b.xMax - 1, b.yMax - 1)) + new Vector3(half, half, 0f);
        const float z = -0.1f; // nudge toward the camera so it draws over tiles

        selectionBox.useWorldSpace = true;
        selectionBox.loop = true;
        selectionBox.positionCount = 4;
        selectionBox.SetPosition(0, new Vector3(min.x, min.y, z));
        selectionBox.SetPosition(1, new Vector3(max.x, min.y, z));
        selectionBox.SetPosition(2, new Vector3(max.x, max.y, z));
        selectionBox.SetPosition(3, new Vector3(min.x, max.y, z));
        selectionBox.enabled = true;
    }

    private void HideSelectionBox()
    {
        if (selectionBox != null) selectionBox.enabled = false;
    }

    #endregion

    #region Rectangle bookkeeping

    private void RemoveOverlapping(RectInt bounds)
    {
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            if (_rects[i].bounds.Overlaps(bounds))
                RemoveRect(_rects[i]);
        }
    }

    private void RemoveRect(ShikakuRect rect)
    {
        SetOwner(rect, null);
        _rects.Remove(rect);
    }

    private void SetOwner(ShikakuRect rect, ShikakuRect value)
    {
        RectInt b = rect.bounds;
        for (int x = b.xMin; x < b.xMax; x++)
            for (int y = b.yMin; y < b.yMax; y++)
                _owner[x, y] = value;
    }

    private void Validate(ShikakuRect rect)
    {
        RectInt b = rect.bounds;
        int clueCount = 0;
        int clueValue = 0;
        for (int x = b.xMin; x < b.xMax; x++)
        {
            for (int y = b.yMin; y < b.yMax; y++)
            {
                if (_clues[x, y] > 0)
                {
                    clueCount++;
                    clueValue = _clues[x, y];
                }
            }
        }

        int area = b.width * b.height;
        rect.valid = clueCount == 1 && area == clueValue;
    }

    #endregion

    #region Win check + rendering

    private void CheckSolved()
    {
        if (_solved) return;

        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (_owner[x, y] == null) return;

        foreach (var rect in _rects)
            if (!rect.valid) return;

        _solved = true;
        OnSolved?.Invoke();
    }

    private void RefreshColors()
    {
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                _tiles[x, y].SetColor(ColorFor(x, y));
            }
        }
    }

    private Color ColorFor(int x, int y)
    {
        if (_hasPreview && _previewBounds.Contains(new Vector2Int(x, y)))
            return previewColor;

        ShikakuRect owner = _owner[x, y];
        if (owner != null)
            return owner.valid ? validColor : invalidColor;

        return _clues[x, y] > 0 ? clueColor : emptyColor;
    }

    #endregion
}
