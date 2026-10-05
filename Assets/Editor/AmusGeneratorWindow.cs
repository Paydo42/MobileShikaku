using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shikaku > Amus > Level Generator: makes Puzzle Baron-style levels (every
/// tile filled, wires winding around each other, exactly one answer) with
/// <see cref="AmusLevelGenerator"/>, previews them, and saves one into the
/// level you pick. Nothing changes until you press Save, and Edit > Undo
/// reverts a save.
/// </summary>
public class AmusGeneratorWindow : EditorWindow
{
    private static Color[] Palette => AmusBoard.DefaultPalette; // same colours as the game
    private static readonly Color EmptyColor = new(0.86f, 0.86f, 0.86f);
    private static readonly Color WallColor = new(0.20f, 0.20f, 0.24f);

    [SerializeField] private AmusPuzzle level;
    [SerializeField] private int width = 9;
    [SerializeField] private int height = 9;
    [SerializeField] private int pairs = 7;
    [SerializeField] private bool keepWalls = true;
    [SerializeField] private int seconds = 15;
    [SerializeField] private bool showAnswer = true;

    private bool[] _walls; // the picked level's walls, null if it has none
    private int _wallsWidth;
    private int _wallsHeight;

    private AmusLevelGenerator.Level _result;
    private Task<AmusLevelGenerator.Level> _task;
    private CancellationTokenSource _stop;
    private AmusLevelGenerator.Progress _progress;
    private double _startTime;
    private int _searchSeconds;
    private string _message;
    private Vector2 _scroll;

    [MenuItem("Shikaku/Amus/Level Generator")]
    private static void Open()
    {
        var window = GetWindow<AmusGeneratorWindow>("Amus Generator");
        window.minSize = new Vector2(360f, 480f);
        if (Selection.activeObject is AmusPuzzle selected) window.Pick(selected);
    }

    private void OnEnable()
    {
        if (level != null) ReadWalls();
    }

    private void OnDisable() => _stop?.Cancel();

    private void OnSelectionChange()
    {
        if (_task != null || !(Selection.activeObject is AmusPuzzle selected) || selected == level) return;
        Pick(selected);
        Repaint();
    }

    // Takes a level's size, pair count and walls as the starting settings.
    private void Pick(AmusPuzzle picked)
    {
        level = picked;
        _result = null;
        _message = null;
        _walls = null;
        if (level == null) return;

        int found = ReadWalls();
        width = _wallsWidth;
        height = _wallsHeight;
        pairs = Mathf.Max(2, found);
    }

    // Caches the picked level's walls and size; returns its pair count.
    private int ReadWalls()
    {
        List<AmusPuzzle.Pair> found = level.Parse(out bool[,] blocked, out _wallsWidth, out _wallsHeight);
        _walls = new bool[_wallsWidth * _wallsHeight];
        bool any = false;
        for (int x = 0; x < _wallsWidth; x++)
        {
            for (int y = 0; y < _wallsHeight; y++)
            {
                _walls[y * _wallsWidth + x] = blocked[x, y];
                any |= blocked[x, y];
            }
        }
        if (!any) _walls = null;
        return found.Count;
    }

    // The picked level's walls, if it has any and the size still matches.
    private bool[] UsableWalls() =>
        _walls != null && width == _wallsWidth && height == _wallsHeight ? _walls : null;

    private void Update()
    {
        if (_task == null) return;
        Repaint();
        if (!_task.IsCompleted) return;

        if (_task.IsFaulted)
        {
            Debug.LogException(_task.Exception);
            _message = "The generator hit an error; see the Console.";
        }
        else
        {
            _result = _task.Result;
            if (_result == null)
                _message = "No level with exactly one answer was found in time. Try again, give it more " +
                           "seconds, or allow a few more pairs.";
        }
        _task = null;
        _stop.Dispose();
        _stop = null;
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox(
            "Makes Puzzle Baron-style levels: wires fill every tile, wind around each other, and there is " +
            "exactly one answer. Fewer pairs means longer wires and a harder level. Nothing is saved until " +
            "you press Save.", MessageType.None);

        bool busy = _task != null;
        using (new EditorGUI.DisabledScope(busy))
        {
            EditorGUI.BeginChangeCheck();
            var picked = (AmusPuzzle)EditorGUILayout.ObjectField(
                new GUIContent("Level to replace", "Its size, pair count and walls are the starting settings."),
                level, typeof(AmusPuzzle), false);
            if (EditorGUI.EndChangeCheck()) Pick(picked);

            width = EditorGUILayout.IntSlider("Width", width, 3, 20);
            height = EditorGUILayout.IntSlider("Height", height, 3, 20);
            int maxPairs = Mathf.Clamp(width * height / 3, 2, AmusLevelGenerator.MaxPairs);
            pairs = EditorGUILayout.IntSlider(new GUIContent("Pairs",
                    "Fewer pairs = longer, more winding wires = harder. If the board can't take this few, " +
                    "it lands as close as it can."),
                pairs, 2, maxPairs);

            using (new EditorGUI.DisabledScope(UsableWalls() == null))
                keepWalls = EditorGUILayout.Toggle(new GUIContent("Keep the level's walls",
                    "Only when the level has walls and the size is unchanged."), keepWalls);

            seconds = EditorGUILayout.IntSlider(new GUIContent("Search time (s)",
                "Longer finds more candidates to pick the most winding from. Big boards need more."), seconds, 3, 120);
        }

        EditorGUILayout.Space();
        if (busy)
        {
            DrawProgress();
            if (GUILayout.Button("Stop (keep the best so far)")) _stop.Cancel();
        }
        else if (GUILayout.Button("Generate", GUILayout.Height(30f)))
        {
            StartGenerating();
        }

        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Warning);
        if (_result != null && !busy) DrawResult();
        EditorGUILayout.EndScrollView();
    }

    private void StartGenerating()
    {
        int w = width, h = height, p = pairs;
        bool[] walls = keepWalls ? UsableWalls() : null;
        int seed = Environment.TickCount;
        AmusLevelGenerator.Progress progress = _progress = new AmusLevelGenerator.Progress();
        _stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        CancellationToken token = _stop.Token;

        _result = null;
        _message = null;
        _startTime = EditorApplication.timeSinceStartup;
        _searchSeconds = seconds;
        _task = Task.Run(() => AmusLevelGenerator.Generate(w, h, walls, p, seed, progress, token));
    }

    private void DrawProgress()
    {
        float elapsed = (float)(EditorApplication.timeSinceStartup - _startTime);
        Rect bar = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.ProgressBar(bar, Mathf.Clamp01(elapsed / _searchSeconds), $"Searching... {elapsed:0} / {_searchSeconds} s");

        AmusLevelGenerator.Progress p = _progress;
        EditorGUILayout.LabelField(
            $"{p.Layouts} layouts tried, {p.Checked} checked, {p.Unique} with one answer",
            EditorStyles.miniLabel);
        if (p.Unique > 0)
            EditorGUILayout.LabelField($"Best so far: {p.BestPairs} pairs, winding {p.BestWinding}", EditorStyles.miniLabel);
    }

    private void DrawResult()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            $"{_result.Width}x{_result.Height}, {_result.Wires.Length} pairs, exactly one answer, winding {_result.Winding}",
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Winding = extra steps the wires take beyond straight routes (higher = trickier).",
            EditorStyles.miniLabel);
        if (_result.Wires.Length > Palette.Length)
            EditorGUILayout.HelpBox($"More than {Palette.Length} pairs: some colours repeat unless the board's palette " +
                                    "gets more colours.", MessageType.Info);

        showAnswer = EditorGUILayout.ToggleLeft("Show the answer", showAnswer);
        DrawPreview(_result);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(level == null))
            {
                string label = level != null ? $"Save into '{level.name}'" : "Save (pick a level first)";
                if (GUILayout.Button(label, GUILayout.Height(26f))) Save();
            }
            if (GUILayout.Button("Copy level text", GUILayout.Height(26f)))
            {
                EditorGUIUtility.systemCopyBuffer = _result.Grid;
                ShowNotification(new GUIContent("Copied"));
            }
        }
    }

    private void DrawPreview(AmusLevelGenerator.Level shown)
    {
        float size = Mathf.Floor(Mathf.Clamp((position.width - 30f) / shown.Width, 10f, 32f));
        Rect area = GUILayoutUtility.GetRect(shown.Width * size, shown.Height * size,
            GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

        int cells = shown.Width * shown.Height;
        var wireOf = new int[cells];
        for (int i = 0; i < cells; i++) wireOf[i] = -1;
        for (int p = 0; p < shown.Wires.Length; p++)
            foreach (int cell in shown.Wires[p]) wireOf[cell] = p;

        var letter = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(size * 0.5f)),
        };
        letter.normal.textColor = Color.white;

        for (int y = 0; y < shown.Height; y++)
        {
            for (int x = 0; x < shown.Width; x++)
            {
                int cell = y * shown.Width + x;
                int p = wireOf[cell];
                var rect = new Rect(area.x + x * size, area.y + (shown.Height - 1 - y) * size, size - 1f, size - 1f);

                Color fill = shown.Blocked[cell] ? WallColor
                    : p >= 0 && showAnswer ? Color.Lerp(PairColor(p), Color.white, 0.45f)
                    : EmptyColor;
                EditorGUI.DrawRect(rect, fill);

                bool isDot = p >= 0 && (shown.Wires[p][0] == cell || shown.Wires[p][^1] == cell);
                if (!isDot) continue;
                float inset = size * 0.12f;
                EditorGUI.DrawRect(new Rect(rect.x + inset, rect.y + inset, rect.width - 2f * inset, rect.height - 2f * inset),
                    PairColor(p));
                GUI.Label(rect, shown.Names[p], letter);
            }
        }
    }

    private static Color PairColor(int p) => Palette[p % Palette.Length];

    // Writes the level text, turns on Must Fill Board, and bakes the answer
    // for the Lock helper, checked by reading it back like the game does.
    private void Save()
    {
        if (!EditorUtility.DisplayDialog("Replace level?",
                $"Replace the grid of '{level.name}' with this generated level?\n\nEdit > Undo reverts it.",
                "Replace", "Cancel"))
            return;

        Undo.RecordObject(level, "Generate Amus Level");
        string oldGrid = level.grid;
        string oldSolution = level.solution;
        bool oldFill = level.mustFillBoard;

        level.grid = _result.Grid;
        level.mustFillBoard = true;

        List<AmusPuzzle.Pair> parsed = level.Parse(out bool[,] blocked, out int w, out int h);
        bool ok = parsed.Count == _result.Wires.Length && w == _result.Width && h == _result.Height;
        if (ok)
        {
            var wires = new List<Vector2Int>[parsed.Count];
            for (int p = 0; p < parsed.Count && ok; p++)
            {
                ok = parsed[p].symbol == _result.Names[p];
                wires[p] = new List<Vector2Int>(_result.Wires[p].Length);
                foreach (int cell in _result.Wires[p]) wires[p].Add(new Vector2Int(cell % w, cell / w));
            }
            if (ok)
            {
                level.solution = AmusPuzzle.EncodeSolution(parsed, wires);
                ok = level.TryDecodeSolution(parsed, blocked, w, h, true, out _);
            }
        }

        if (!ok)
        {
            level.grid = oldGrid;
            level.solution = oldSolution;
            level.mustFillBoard = oldFill;
            Debug.LogError($"[Amus Generator] The generated level didn't read back correctly; '{level.name}' was left unchanged.", level);
            return;
        }

        EditorUtility.SetDirty(level);
        AssetDatabase.SaveAssets();
        ReadWalls();
        ShowNotification(new GUIContent($"Saved into {level.name}"));
    }
}
