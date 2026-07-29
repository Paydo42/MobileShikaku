using System.Collections;
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
    [SerializeField] private Color validColor = new(0.60f, 0.85f, 0.60f);
    [SerializeField] private Color invalidColor = new(0.90f, 0.55f, 0.55f);
    [Range(0f, 1f)]
    [Tooltip("Opacity of the fill of a committed rectangle's box.")]
    [SerializeField] private float rectFillAlpha = 0.55f;
    [Tooltip("Outline thickness of the boxes that stay on the board.")]
    [SerializeField] private float rectOutlineWidth = 0.05f;
    [Range(0f, 0.5f)]
    [Tooltip("Corner rounding of the boxes, as a fraction of a cell (0 = sharp).")]
    [SerializeField] private float cornerRadius = 0.18f;
    [Range(0.7f, 1f)]
    [Tooltip("Fraction of the selected area a box fills; the rest becomes a visible gap between neighbouring boxes.")]
    [SerializeField] private float boxFill = 0.92f;
    [Tooltip("A single tap on a placed box erases it so it can be redrawn. Off = only long-press erases.")]
    [SerializeField] private bool tapToErase = true;

    [Header("3D Look")]
    [Tooltip("Raised look for the boxes: shaded face over a darker inner edge.")]
    [SerializeField] private bool boxDepthEffect = true;
    [Range(0f, 0.3f)]
    [Tooltip("Height of the darker inner edge at the bottom of a box, as a fraction of a cell.")]
    [SerializeField] private float shadowInset = 0.12f;
    [Range(0f, 1f)]
    [Tooltip("How much darker the inner edge is than the box's own colour.")]
    [SerializeField] private float shadowDarkness = 0.45f;
    [Range(0f, 0.5f)]
    [Tooltip("How much darker a box's bottom edge is than its top (the fake lighting).")]
    [SerializeField] private float shadeStrength = 0.25f;
    [Tooltip("Give each clue's solved rectangle its own unique colour (instead of one green).")]
    [SerializeField] private bool uniqueRectColors = true;
    [Range(0f, 1f)]
    [Tooltip("Saturation of the unique colours (lower = more pastel).")]
    [SerializeField] private float rectColorSaturation = 0.45f;
    [Range(0f, 1f)]
    [Tooltip("Brightness of the unique colours.")]
    [SerializeField] private float rectColorBrightness = 0.95f;

    [Header("Feedback")]
    [Tooltip("Scale-pop the tiles of a rectangle when it's placed. Sounds are assigned on the SoundManager.")]
    [SerializeField] private bool animateSelection = true;
    [SerializeField] private float popStrength = 1.15f;
    [SerializeField] private float popDuration = 0.15f;
    [Tooltip("Animate the selection outline with a springy overshoot while dragging.")]
    [SerializeField] private bool bouncySelectionBox = true;
    [Tooltip("Spring strength: higher = snappier follow.")]
    [SerializeField] private float boxSpring = 250f;
    [Tooltip("Spring damping: lower = more wobble/bounce.")]
    [SerializeField] private float boxDamping = 12f;
    [Tooltip("Fill the selection rectangle with a colour while dragging.")]
    [SerializeField] private bool showSelectionFill = true;
    [Tooltip("Fill while the selection is not yet correct (transparent).")]
    [SerializeField] private Color fillSelectingColor = new(1f, 1f, 1f, 0.15f);
    [Tooltip("Fill when the selection is a correct rectangle (solid).")]
    [SerializeField] private Color fillCorrectColor = new(0.60f, 0.85f, 0.60f, 0.85f);

    [Header("Win Sequence")]
    [Tooltip("Pause after the last box lands before the celebration starts.")]
    [SerializeField] private float winStartDelay = 0.25f;
    [Tooltip("Delay between each box's bounce.")]
    [SerializeField] private float winStagger = 0.08f;
    [Tooltip("Peak scale of each box's bounce.")]
    [SerializeField] private float winBounceScale = 1.12f;
    [Tooltip("Duration of one box's bounce.")]
    [SerializeField] private float winBounceDuration = 0.25f;
    [Tooltip("Extra pause after the last bounce before OnSolved fires (win canvas).")]
    [SerializeField] private float winEndDelay = 0.35f;
    [Tooltip("Light phone vibration tick on each box's bounce (Android).")]
    [SerializeField] private bool winHaptics = true;

    [Header("Events")]
    public UnityEvent OnSolved;

    /// <summary>A committed rectangle and its current validity.</summary>
    private class ShikakuRect
    {
        public RectInt bounds;
        public bool valid;
        public Vector2Int clueCell;   // the clue this rectangle solves (when valid)
        public GameObject visual;     // the persistent box (fill + outline)

        public ShikakuRect(RectInt bounds) => this.bounds = bounds;
    }

    private int _width;
    private int _height;
    private int[,] _clues;            // 0 = no clue
    private Tile[,] _tiles;
    private ShikakuRect[,] _owner;    // which rect owns each cell (null = none)
    private readonly List<ShikakuRect> _rects = new();

    private RectInt _previewBounds;
    private bool _solved;
    private float _levelStartTime;
    private float _solveTimeSeconds;

    /// <summary>Seconds it took to solve the level (valid once solved).</summary>
    public float SolveTimeSeconds => _solveTimeSeconds;

    // Spring state for the bouncy selection outline.
    private Vector2 _boxMin, _boxMax, _boxMinVel, _boxMaxVel;
    private Vector2 _boxTargetMin, _boxTargetMax;

    private bool _previewValid;       // current drag selects a correct rectangle
    private Vector2Int _previewClue;  // clue the current drag would solve
    private SpriteRenderer _fill;     // runtime-generated selection fill quad

    // One distinct colour per clue cell, generated at build time.
    private readonly Dictionary<Vector2Int, Color> _clueColors = new();

    private void Start() => Build();

    private void Update()
    {
        if (selectionBox != null && selectionBox.enabled && bouncySelectionBox)
        {
            Spring(ref _boxMin, ref _boxMinVel, _boxTargetMin, Time.deltaTime);
            Spring(ref _boxMax, ref _boxMaxVel, _boxTargetMax, Time.deltaTime);
            ApplySelectionBox();
        }
    }

    #region Build / layout

    private void Build()
    {
        if (tilePrefab == null)
        {
            Debug.LogError("GridManager: Tile prefab is not assigned.", this);
            return;
        }

        _levelStartTime = Time.time;
        if (puzzle == null) puzzle = SelectPuzzle();

        _clues = puzzle.BuildClueGrid();
        _width = _clues.GetLength(0);
        _height = _clues.GetLength(1);
        _tiles = new Tile[_width, _height];
        _owner = new ShikakuRect[_width, _height];

        // Give every clue its own colour, stepping the hue by the golden ratio
        // so consecutive clues are always clearly different.
        _clueColors.Clear();
        int clueIndex = 0;
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_clues[x, y] > 0)
                {
                    float hue = (clueIndex * 0.618034f) % 1f;
                    _clueColors[new Vector2Int(x, y)] =
                        Color.HSVToRGB(hue, rectColorSaturation, rectColorBrightness);
                    clueIndex++;
                }
            }
        }

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
        if (_solved) return; // board is locked during/after the win sequence
        _previewBounds = BoundsFrom(Clamp(a), Clamp(b));
        _previewValid = IsValidBounds(_previewBounds, out _previewClue);
        RefreshColors();
        UpdateSelectionBox(_previewBounds);
    }

    public void ClearPreview()
    {
        HideSelectionBox();
    }

    /// <summary>
    /// Commit the player's selection. A single-cell selection on an existing
    /// rectangle erases it; otherwise a new rectangle is placed, replacing any
    /// rectangles it overlaps.
    /// </summary>
    public void CommitSelection(Vector2Int a, Vector2Int b)
    {
        if (_solved) return; // board is locked during/after the win sequence
        HideSelectionBox();
        RectInt bounds = BoundsFrom(Clamp(a), Clamp(b));

        // Tap on an owned cell: erase the box so it can be redrawn (if enabled).
        if (bounds.width == 1 && bounds.height == 1)
        {
            ShikakuRect tapped = _owner[bounds.xMin, bounds.yMin];
            if (tapped != null)
            {
                if (tapToErase)
                {
                    RemoveRect(tapped);
                    if (SoundManager.Instance != null) SoundManager.Instance.PlayRectErase();
                }
                return;
            }
        }

        RemoveOverlapping(bounds);

        var rect = new ShikakuRect(bounds);
        _rects.Add(rect);
        SetOwner(rect, rect);
        Validate(rect);
        CreateRectVisual(rect);

        AnimateRect(bounds);
        if (SoundManager.Instance != null)
        {
            if (rect.valid) SoundManager.Instance.PlayRectValid();
            else SoundManager.Instance.PlayRectPlace();
        }
        CheckSolved();
    }

    /// <summary>
    /// Deliberately erase the rectangle owning this cell (called on long-press).
    /// Returns true if one was removed.
    /// </summary>
    public bool EraseAt(Vector2Int cell)
    {
        if (_solved || !InBounds(cell)) return false;

        ShikakuRect owner = _owner[cell.x, cell.y];
        if (owner == null) return false;

        RemoveRect(owner);
        RefreshColors();
        if (SoundManager.Instance != null) SoundManager.Instance.PlayRectErase();
        return true;
    }

    private void AnimateRect(RectInt bounds)
    {
        if (!animateSelection) return;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
                _tiles[x, y].PlayPop(popStrength, popDuration);
    }

    // Sets the marquee outline's target; the spring in Update chases it.
    private void UpdateSelectionBox(RectInt b)
    {
        if (selectionBox == null) return;

        // Inset from the cell bounds so neighbouring boxes keep a visible gap.
        float half = cellSize * 0.5f - cellSize * (1f - boxFill) * 0.5f;
        Vector3 min = CellToWorld(new Vector2Int(b.xMin, b.yMin)) + new Vector3(-half, -half, 0f);
        Vector3 max = CellToWorld(new Vector2Int(b.xMax - 1, b.yMax - 1)) + new Vector3(half, half, 0f);

        _boxTargetMin = new Vector2(min.x, min.y);
        _boxTargetMax = new Vector2(max.x, max.y);

        if (!selectionBox.enabled)
        {
            // First frame of a drag: start at the pressed cell, no fly-in.
            _boxMin = _boxTargetMin;
            _boxMax = _boxTargetMax;
            _boxMinVel = _boxMaxVel = Vector2.zero;

            selectionBox.useWorldSpace = true;
            selectionBox.loop = true;
            selectionBox.enabled = true;
        }

        if (!bouncySelectionBox)
        {
            _boxMin = _boxTargetMin;
            _boxMax = _boxTargetMax;
        }

        if (showSelectionFill)
        {
            SpriteRenderer fill = GetSelectionFill();
            fill.enabled = true;

            Color correct = fillCorrectColor;
            if (uniqueRectColors && _previewValid && _clueColors.TryGetValue(_previewClue, out Color c))
                correct = new Color(c.r, c.g, c.b, fillCorrectColor.a);

            fill.color = _previewValid ? correct : fillSelectingColor;
        }

        ApplySelectionBox();
    }

    private void ApplySelectionBox()
    {
        // z nudges toward the camera so the marquee draws over tiles.
        SetRoundedRectPath(selectionBox, _boxMin, _boxMax, cellSize * cornerRadius, -0.1f);

        if (_fill != null && _fill.enabled)
        {
            Vector2 center = (_boxMin + _boxMax) * 0.5f;
            _fill.transform.position = new Vector3(center.x, center.y, -0.05f);
            _fill.size = _boxMax - _boxMin; // 9-slice size, not scale
        }
    }

    // Code-generated box sprites (no art assets): a rounded-corner square, flat
    // or with a vertical light-to-dark gradient (reads as a lit surface when
    // tinted). Built with a 9-slice border so corners keep their shape whatever
    // rectangle they're stretched to — renderers must use Sliced draw mode.
    private Sprite _flatSprite;
    private Sprite _gradientSprite;

    private Sprite FlatBoxSprite()
    {
        if (_flatSprite == null) _flatSprite = MakeBoxSprite(gradient: false);
        return _flatSprite;
    }

    private Sprite GradientBoxSprite()
    {
        if (_gradientSprite == null) _gradientSprite = MakeBoxSprite(gradient: true);
        return _gradientSprite;
    }

    private Sprite BoxSprite() => boxDepthEffect ? GradientBoxSprite() : FlatBoxSprite();

    private Sprite MakeBoxSprite(bool gradient)
    {
        const int size = 64; // texture spans one cell in world units
        int radiusPx = Mathf.RoundToInt(Mathf.Clamp01(cornerRadius) * size);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < size; y++)
        {
            float t = y / (size - 1f); // 0 = bottom, 1 = top
            float v = gradient ? Mathf.Lerp(1f - shadeStrength, 1f, t) : 1f;
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, new Color(v, v, v, CornerMask(x, y, size, radiusPx)));
        }
        tex.Apply();

        float ppu = size / Mathf.Max(0.01f, cellSize);
        var border = new Vector4(radiusPx, radiusPx, radiusPx, radiusPx);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            ppu, 0, SpriteMeshType.FullRect, border);
    }

    // 1 inside the rounded rectangle, 0 outside, with a ~1px soft edge.
    private static float CornerMask(int x, int y, int size, int radius)
    {
        if (radius <= 0) return 1f;
        float cx = Mathf.Clamp(x, radius, size - 1 - radius);
        float cy = Mathf.Clamp(y, radius, size - 1 - radius);
        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        return Mathf.Clamp01(radius - d + 0.5f);
    }

    // Lays a LineRenderer along a rounded-rectangle path (arcs at the corners).
    // worldSpace false = positions are local, so the line scales with its parent.
    private static void SetRoundedRectPath(LineRenderer line, Vector2 min, Vector2 max,
        float radius, float z, bool worldSpace = true)
    {
        float r = Mathf.Min(radius, Mathf.Min(max.x - min.x, max.y - min.y) * 0.5f);
        const int seg = 4; // arc segments per corner

        Vector2[] centers =
        {
            new(min.x + r, min.y + r), // bottom-left,  arc 180..270
            new(max.x - r, min.y + r), // bottom-right, arc 270..360
            new(max.x - r, max.y - r), // top-right,    arc   0..90
            new(min.x + r, max.y - r), // top-left,     arc  90..180
        };

        line.useWorldSpace = worldSpace;
        line.loop = true;
        line.positionCount = (seg + 1) * 4;

        int i = 0;
        for (int c = 0; c < 4; c++)
        {
            float startAngle = 180f + 90f * c;
            for (int s = 0; s <= seg; s++)
            {
                float ang = (startAngle + 90f * s / seg) * Mathf.Deg2Rad;
                Vector2 p = centers[c] + r * new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                line.SetPosition(i++, new Vector3(p.x, p.y, z));
            }
        }
    }

    private SpriteRenderer GetSelectionFill()
    {
        if (_fill == null)
        {
            var go = new GameObject("SelectionFill");
            go.transform.SetParent(transform, false);
            _fill = go.AddComponent<SpriteRenderer>();
            _fill.sprite = BoxSprite();
            _fill.drawMode = SpriteDrawMode.Sliced; // 9-slice keeps corners round
            _fill.sortingOrder = 5; // above committed boxes, below the drag outline
            _fill.enabled = false;
        }
        return _fill;
    }

    // Builds the box (translucent fill + solid outline) that stays on a
    // committed rectangle until it is replaced or erased.
    private void CreateRectVisual(ShikakuRect rect)
    {
        Color baseColor = rect.valid
            ? (uniqueRectColors && _clueColors.TryGetValue(rect.clueCell, out Color unique)
                ? unique
                : validColor)
            : invalidColor;

        // Inset from the cell bounds so neighbouring boxes keep a visible gap.
        float half = cellSize * 0.5f - cellSize * (1f - boxFill) * 0.5f;
        Vector3 min = CellToWorld(new Vector2Int(rect.bounds.xMin, rect.bounds.yMin))
            + new Vector3(-half, -half, 0f);
        Vector3 max = CellToWorld(new Vector2Int(rect.bounds.xMax - 1, rect.bounds.yMax - 1))
            + new Vector3(half, half, 0f);
        Vector3 center = (min + max) * 0.5f;

        var go = new GameObject("RectBox");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(center.x, center.y, 0f);

        float w = max.x - min.x;
        float h = max.y - min.y;

        var fill = go.AddComponent<SpriteRenderer>();
        // Sprite must be assigned before draw mode/size: assigning a sprite
        // resets the renderer's size, which would undo the 9-slice sizing.
        fill.sprite = FlatBoxSprite();
        fill.drawMode = SpriteDrawMode.Sliced; // 9-slice keeps the corners round
        fill.size = new Vector2(w, h);

        if (boxDepthEffect)
        {
            // Thick-tile look: the root renderer is the box's darker 'inside'
            // (same hue, darkened), and the lighter face sits slightly higher,
            // exposing an inner lip along the bottom — all within the box.
            float k = 1f - shadowDarkness;
            fill.sortingOrder = 3; // under the face
            fill.color = new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, rectFillAlpha);

            float inset = Mathf.Min(cellSize * shadowInset, h * 0.4f);
            var faceGo = new GameObject("Face");
            faceGo.transform.SetParent(go.transform, false);
            faceGo.transform.localPosition = new Vector3(0f, inset * 0.5f, 0f);

            var face = faceGo.AddComponent<SpriteRenderer>();
            face.sprite = GradientBoxSprite();
            face.drawMode = SpriteDrawMode.Sliced;
            face.size = new Vector2(w, h - inset);
            face.sortingOrder = 4; // above tiles, below the drag preview
            face.color = new Color(baseColor.r, baseColor.g, baseColor.b, rectFillAlpha);
        }
        else
        {
            fill.sortingOrder = 4; // above tiles, below the drag preview
            fill.color = new Color(baseColor.r, baseColor.g, baseColor.b, rectFillAlpha);
        }

        // Outline in local space so it scales with the box (win-sequence bounce).
        var outlineGo = new GameObject("Outline");
        outlineGo.transform.SetParent(go.transform, false);
        var line = outlineGo.AddComponent<LineRenderer>();
        SetRoundedRectPath(line, min - center, max - center, cellSize * cornerRadius,
            -0.05f, worldSpace: false);
        line.startWidth = line.endWidth = rectOutlineWidth;
        if (selectionBox != null) line.sharedMaterial = selectionBox.sharedMaterial;
        line.sortingOrder = 6;
        line.startColor = line.endColor = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

        rect.visual = go;
    }

    // Under-damped spring: overshoots the target a little, then settles.
    private void Spring(ref Vector2 value, ref Vector2 velocity, Vector2 target, float dt)
    {
        velocity += (target - value) * (boxSpring * dt);
        velocity *= Mathf.Max(0f, 1f - boxDamping * dt);
        value += velocity * dt;
    }

    private void HideSelectionBox()
    {
        if (selectionBox != null) selectionBox.enabled = false;
        if (_fill != null) _fill.enabled = false;
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
        if (rect.visual != null) Destroy(rect.visual);
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

    private void Validate(ShikakuRect rect) =>
        rect.valid = IsValidBounds(rect.bounds, out rect.clueCell);

    /// <summary>
    /// True if the region holds exactly one clue and its area matches it.
    /// <paramref name="clueCell"/> is that clue's cell (only meaningful when true).
    /// </summary>
    private bool IsValidBounds(RectInt b, out Vector2Int clueCell)
    {
        clueCell = default;
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
                    clueCell = new Vector2Int(x, y);
                }
            }
        }

        return clueCount == 1 && b.width * b.height == clueValue;
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
        _solveTimeSeconds = Time.time - _levelStartTime;
        StartCoroutine(WinSequence());
    }

    // Celebration before the win canvas: each box bounces in place, sweeping
    // across the board in reading order, then OnSolved fires.
    private IEnumerator WinSequence()
    {
        yield return new WaitForSeconds(winStartDelay);

        var ordered = new List<ShikakuRect>(_rects);
        ordered.Sort((a, b) =>
        {
            int byRow = b.bounds.center.y.CompareTo(a.bounds.center.y); // top first
            return byRow != 0 ? byRow : a.bounds.center.x.CompareTo(b.bounds.center.x);
        });

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].visual != null)
                StartCoroutine(BounceBox(ordered[i].visual.transform));

            if (winHaptics) Haptics.Tick();

            if (SoundManager.Instance != null)
            {
                float pitch = ordered.Count > 1
                    ? 1f + 0.5f * i / (ordered.Count - 1)
                    : 1f;
                SoundManager.Instance.PlayRectValid(pitch);
            }

            yield return new WaitForSeconds(winStagger);
        }

        yield return new WaitForSeconds(winBounceDuration + winEndDelay);
        OnSolved?.Invoke();
    }

    private IEnumerator BounceBox(Transform box)
    {
        Vector3 rest = box.localScale;
        Vector3 peak = rest * winBounceScale;
        float half = Mathf.Max(0.01f, winBounceDuration * 0.5f);

        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            box.localScale = Vector3.Lerp(rest, peak, t / half);
            yield return null;
        }
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            box.localScale = Vector3.Lerp(peak, rest, t / half);
            yield return null;
        }
        box.localScale = rest;
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

    // Tiles keep their base look; committed rectangles are shown by their own
    // persistent box visuals instead of tile tinting.
    private Color ColorFor(int x, int y)
    {
        return _clues[x, y] > 0 ? clueColor : emptyColor;
    }

    #endregion
}
