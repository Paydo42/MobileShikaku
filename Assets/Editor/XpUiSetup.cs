using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click Editor setup for the player-level (XP) UI: builds the widgets in
/// the scenes and wires every serialized field, so nothing needs dragging.
///
///   Shikaku > Setup > Build XP UI
///     Game / Preymet / Amus : an "XpPanel" (<see cref="XpRewardPanel"/>) on the
///                             win panel, assigned to LevelCompleteController's
///                             Xp Panel field.
///     MainMenu              : a "PlayerLevelBadge" (<see cref="PlayerLevelHUD"/>).
///
///   Shikaku > Setup > Add Level Badge To Game HUDs (optional)
///     Game / Preymet / Amus : a compact PlayerLevelHUD on the in-game HUD.
///
/// Safe to run again: anything already built is left alone, so delete an
/// object to have it rebuilt. An XpRewardPanel started by hand is used instead
/// of adding a second one (filled in if it is still an empty shell). Sizes are
/// authored for a 1080-wide portrait canvas and scaled to whatever reference
/// resolution the target canvas uses.
/// </summary>
public static class XpUiSetup
{
    private const string GameScene = "Assets/Scenes/Game.unity";
    private const string PreymetScene = "Assets/Scenes/Preymet.unity";
    private const string AmusScene = "Assets/Scenes/Amus.unity";
    private const string MainMenuScene = "Assets/Scenes/MainMenu.unity";

    private const string XpPanelName = "XpPanel";
    private const string BadgeName = "PlayerLevelBadge";

    private static readonly Color CardColor = new(0f, 0f, 0f, 0.45f);
    private static readonly Color BarBackColor = new(0.12f, 0.14f, 0.2f, 0.95f);
    private static readonly Color BarFillColor = new(1f, 0.78f, 0.24f, 1f);
    private static readonly Color GainColor = new(1f, 0.84f, 0.3f, 1f);
    private static readonly Color SoftColor = new(0.85f, 0.88f, 0.95f, 1f);

    #region Menu

    [MenuItem("Shikaku/Setup/Build XP UI")]
    private static void BuildXpUi()
    {
        RunInScenes("Build XP UI",
            new[] { GameScene, PreymetScene, AmusScene, MainMenuScene },
            scene => scene.path == MainMenuScene ? BuildMenuBadge(scene) : BuildWinXpPanel(scene));
    }

    [MenuItem("Shikaku/Setup/Add Level Badge To Game HUDs (optional)")]
    private static void BuildHudBadges()
    {
        RunInScenes("Add Level Badge To Game HUDs",
            new[] { GameScene, PreymetScene, AmusScene },
            BuildHudBadge);
    }

    #endregion

    #region Scene steps

    // Win panel: XpRewardPanel under the panel the controller turns on, so it is
    // active when LevelCompleteController calls Play() and can animate.
    private static string BuildWinXpPanel(Scene scene)
    {
        LevelCompleteController controller = FindInScene<LevelCompleteController>(scene);
        if (controller == null) return "ERROR: no LevelCompleteController found";

        var so = new SerializedObject(controller);
        SerializedProperty xpPanelProp = so.FindProperty("xpPanel");
        if (xpPanelProp == null) return "ERROR: LevelCompleteController has no 'xpPanel' field";
        if (xpPanelProp.objectReferenceValue != null) return "skipped: Xp Panel is already assigned";

        var winPanel = so.FindProperty("winPanel").objectReferenceValue as GameObject;
        var winButtons = so.FindProperty("winButtons").objectReferenceValue as GameObject;
        if (winPanel == null) return "ERROR: LevelCompleteController's Win Panel is not assigned";

        // Game.unity keeps its content on a "Win Panel" child of the canvas;
        // the other modes put it straight on the canvas.
        Transform parent = winPanel.transform.Find("Win Panel");
        if (parent == null) parent = winPanel.transform;

        float k = DesignScale(parent);
        // A constant-pixel canvas (Preymet) doesn't shrink on narrow phones:
        // 800 * 0.85 = 680 px still fits a 720-px-wide screen.
        CanvasScaler scaler = RootScaler(parent);
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) k *= 0.85f;
        // Game: the empty band between "You Win" and the result sentence.
        // Other modes: just above centre, clear of "You Win" and the bottom buttons.
        var position = new Vector2(0f, (scene.path == GameScene ? -150f : 100f) * k);

        // An XpRewardPanel started by hand (any name) is used rather than
        // adding a second one: an empty shell gets filled in, anything with
        // content is left exactly as it is.
        string result;
        XpRewardPanel panel = winPanel.GetComponentInChildren<XpRewardPanel>(true);
        if (panel != null && !IsEmptyShell(panel))
        {
            result = $"kept your '{GetPath(panel.transform)}' as is, assigned to Xp Panel";
        }
        else if (panel != null)
        {
            FillXpRewardPanel(panel, position, k);
            result = $"filled in your empty '{GetPath(panel.transform)}' (scale {k:0.##}), assigned to Xp Panel";
        }
        else
        {
            RectTransform root = NewRect(XpPanelName, parent);
            panel = root.gameObject.AddComponent<XpRewardPanel>();
            FillXpRewardPanel(panel, position, k);

            if (winButtons != null && winButtons.transform.parent == parent)
                root.SetSiblingIndex(winButtons.transform.GetSiblingIndex());
            else
                root.SetAsLastSibling(); // draw over the backdrop

            result = $"built '{GetPath(root)}' (scale {k:0.##}), assigned to Xp Panel";
        }

        // The controller hides WinButtons right after Play(), which would stop
        // the fill coroutine of anything inside it.
        if (winButtons != null && panel.transform.IsChildOf(winButtons.transform))
            result += $"  WARNING: it is inside '{winButtons.name}' - move it out, or the bar won't animate";

        xpPanelProp.objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();
        return result;
    }

    // Main menu: the player's level card, top-centre, below the status-bar area.
    private static string BuildMenuBadge(Scene scene)
    {
        Transform canvas = FindPath(scene, "Canvas");
        if (canvas == null) return "ERROR: no root 'Canvas' found";
        if (canvas.GetComponentInChildren<PlayerLevelHUD>(true) != null)
            return "skipped: this canvas already has a PlayerLevelHUD";

        float k = DesignScale(canvas);
        RectTransform root = NewRect(BadgeName, canvas);
        Place(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f * k), new Vector2(640f, 190f));
        root.localScale = Vector3.one * k;
        NewImage(root.gameObject, CardColor, filled: false);
        var hud = root.gameObject.AddComponent<PlayerLevelHUD>();

        TMP_Text level = NewText("LevelTxt", root, "Lv 1", 56f, TextAlignmentOptions.Left, Color.white, bold: true);
        Band(level.rectTransform, top: 14f, height: 70f, xMin: 0f, xMax: 0.5f, inset: 24f);
        TMP_Text title = NewText("TitleTxt", root, "Beginner", 40f, TextAlignmentOptions.Right, SoftColor);
        Band(title.rectTransform, top: 14f, height: 70f, xMin: 0.5f, xMax: 1f, inset: 24f);
        Image fill = NewBar(root, top: 96f, height: 32f, inset: 24f);
        TMP_Text xp = NewText("XpTxt", root, "0 / 33", 28f, TextAlignmentOptions.Center, SoftColor);
        Band(xp.rectTransform, top: 136f, height: 44f, xMin: 0f, xMax: 1f, inset: 24f);

        // Behind the options overlay, so opening Options covers it.
        Transform options = canvas.Find("OptionsPanel");
        if (options != null) root.SetSiblingIndex(options.GetSiblingIndex());

        Assign(hud, ("levelLabel", level), ("titleLabel", title), ("xpLabel", xp), ("xpFill", fill));
        return $"built '{GetPath(root)}'";
    }

    // In-game HUD: a compact read-out. Game has a free top-right corner; the
    // other modes' top rows are taken, so theirs sits bottom-centre.
    private static string BuildHudBadge(Scene scene)
    {
        bool isGame = scene.path == GameScene;
        string hudPath = isGame ? "Reward/WhichLevelAreU" : scene.path == PreymetScene ? "GameHud" : "AmusHud";

        Transform hudCanvas = FindPath(scene, hudPath);
        if (hudCanvas == null) return $"ERROR: '{hudPath}' not found";
        if (hudCanvas.GetComponentInChildren<PlayerLevelHUD>(true) != null)
            return $"skipped: '{hudPath}' already has a PlayerLevelHUD";

        float k = DesignScale(hudCanvas);
        RectTransform root = NewRect(BadgeName, hudCanvas);
        if (isGame)
            Place(root, Vector2.one, Vector2.one, new Vector2(-29f, -51f) * k, new Vector2(320f, 140f));
        else
            Place(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f) * k, new Vector2(320f, 140f));
        root.localScale = Vector3.one * k;
        NewImage(root.gameObject, CardColor, filled: false);
        var hud = root.gameObject.AddComponent<PlayerLevelHUD>();

        TMP_Text level = NewText("LevelTxt", root, "Lv 1", 44f, TextAlignmentOptions.Center, Color.white, bold: true);
        Band(level.rectTransform, top: 8f, height: 56f, xMin: 0f, xMax: 1f, inset: 16f);
        Image fill = NewBar(root, top: 70f, height: 24f, inset: 16f);
        TMP_Text title = NewText("TitleTxt", root, "Beginner", 26f, TextAlignmentOptions.Center, SoftColor);
        Band(title.rectTransform, top: 100f, height: 34f, xMin: 0f, xMax: 1f, inset: 16f);

        Assign(hud, ("levelLabel", level), ("titleLabel", title), ("xpFill", fill));
        return $"built '{GetPath(root)}'" + (isGame ? string.Empty : " (check in the Game view that it clears the board)");
    }

    #endregion

    #region Widgets

    // A panel with no children and no labels or bar assigned: nothing of the
    // user's to lose by building into it.
    private static bool IsEmptyShell(XpRewardPanel panel)
    {
        if (!(panel.transform is RectTransform) || panel.transform.childCount > 0) return false;

        var so = new SerializedObject(panel);
        foreach (string field in new[] { "gainedLabel", "levelLabel", "titleLabel", "xpLabel", "toNextLabel", "xpFill", "xpBar" })
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null && prop.objectReferenceValue != null) return false;
        }
        return true;
    }

    // 800x300 card on the panel's own object: gain, level + title, bar, then
    // numbers + XP-to-next.
    private static void FillXpRewardPanel(XpRewardPanel panel, Vector2 position, float scale)
    {
        var root = (RectTransform)panel.transform;
        root.gameObject.SetActive(true); // Play() only animates on an active object
        Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(800f, 300f));
        root.localScale = Vector3.one * scale;
        if (!root.TryGetComponent(out Image _)) NewImage(root.gameObject, CardColor, filled: false);

        TMP_Text gained = NewText("GainedTxt", root, "+20 XP", 60f, TextAlignmentOptions.Center, GainColor, bold: true);
        Band(gained.rectTransform, top: 14f, height: 80f, xMin: 0f, xMax: 1f, inset: 24f);
        TMP_Text level = NewText("LevelTxt", root, "Lv 1", 48f, TextAlignmentOptions.Left, Color.white, bold: true);
        Band(level.rectTransform, top: 100f, height: 60f, xMin: 0f, xMax: 0.5f, inset: 24f);
        TMP_Text title = NewText("TitleTxt", root, "Beginner", 40f, TextAlignmentOptions.Right, SoftColor);
        Band(title.rectTransform, top: 100f, height: 60f, xMin: 0.5f, xMax: 1f, inset: 24f);
        Image fill = NewBar(root, top: 172f, height: 40f, inset: 24f);
        TMP_Text xp = NewText("XpTxt", root, "0 / 33", 32f, TextAlignmentOptions.Left, Color.white);
        Band(xp.rectTransform, top: 224f, height: 50f, xMin: 0f, xMax: 0.5f, inset: 24f);
        TMP_Text toNext = NewText("ToNextTxt", root, "33 XP to next level", 30f, TextAlignmentOptions.Right, SoftColor);
        Band(toNext.rectTransform, top: 224f, height: 50f, xMin: 0.5f, xMax: 1f, inset: 24f);

        Assign(panel,
            ("gainedLabel", gained), ("levelLabel", level), ("titleLabel", title),
            ("xpLabel", xp), ("toNextLabel", toNext), ("xpFill", fill));
    }

    // Dark track with a Filled image inside; returns the fill.
    private static Image NewBar(Transform parent, float top, float height, float inset)
    {
        RectTransform back = NewRect("XpBarBg", parent);
        Band(back, top, height, 0f, 1f, inset);
        NewImage(back.gameObject, BarBackColor, filled: false);

        RectTransform fillRect = NewRect("XpBarFill", back);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
        return NewImage(fillRect.gameObject, BarFillColor, filled: true);
    }

    internal static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    internal static Image NewImage(GameObject go, Color color, bool filled)
    {
        var image = go.AddComponent<Image>();
        // A sprite is required: without one, Unity ignores Image Type and Fill Amount.
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.color = color;
        image.raycastTarget = false;
        if (filled)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 0.6f; // preview only; the scripts drive it at runtime
        }
        else
        {
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    // Plain TMP label with no LocalizedText, which would overwrite the XP text.
    internal static TMP_Text NewText(string name, Transform parent, string text, float size,
        TextAlignmentOptions alignment, Color color, bool bold = false)
    {
        RectTransform rect = NewRect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = alignment;
        label.color = color;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    #endregion

    #region Helpers

    internal static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    // A horizontal band measured down from the parent's top edge, spanning
    // xMin..xMax of its width. Outer edges get the full inset, the seam between
    // two half-width bands gets half each.
    private static void Band(RectTransform rect, float top, float height, float xMin, float xMax, float inset)
    {
        rect.anchorMin = new Vector2(xMin, 1f);
        rect.anchorMax = new Vector2(xMax, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        float left = xMin <= 0f ? inset : inset * 0.5f;
        float right = xMax >= 1f ? inset : inset * 0.5f;
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// How many of the canvas's units one unit of a 1080x1920 portrait design
    /// is, so the same widget reads the same size under any scaler (Amus's win
    /// canvas, for one, uses a 1920x1080 reference).
    /// </summary>
    internal static float DesignScale(Transform t)
    {
        CanvasScaler scaler = RootScaler(t);
        if (scaler == null) return 1f;
        switch (scaler.uiScaleMode)
        {
            case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                float m = scaler.matchWidthOrHeight;
                return Mathf.Pow(scaler.referenceResolution.x / 1080f, 1f - m)
                     * Mathf.Pow(scaler.referenceResolution.y / 1920f, m);
            case CanvasScaler.ScaleMode.ConstantPixelSize:
                return scaler.scaleFactor > 0f ? 1f / scaler.scaleFactor : 1f;
            default:
                return 1f;
        }
    }

    // The scaler that actually applies: only the outermost canvas's counts.
    internal static CanvasScaler RootScaler(Transform t)
    {
        CanvasScaler scaler = null;
        for (Transform p = t; p != null; p = p.parent)
            if (p.TryGetComponent(out CanvasScaler s)) scaler = s;
        return scaler;
    }

    internal static void Assign(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach ((string field, Object value) in refs)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[XpUiSetup] {target.GetType().Name} has no field '{field}'.", target);
                continue;
            }
            prop.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    internal static T FindInScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .FirstOrDefault();

    // "Root/Child/Grandchild" lookup that, unlike GameObject.Find, sees inactive objects.
    internal static Transform FindPath(Scene scene, string path)
    {
        int slash = path.IndexOf('/');
        string rootName = slash < 0 ? path : path.Substring(0, slash);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != rootName) continue;
            if (slash < 0) return root.transform;
            Transform found = root.transform.Find(path.Substring(slash + 1));
            if (found != null) return found;
        }
        return null;
    }

    internal static string GetPath(Transform t) =>
        t.parent == null ? t.name : $"{GetPath(t.parent)}/{t.name}";

    // Opens each scene, applies the step, saves it if the step changed
    // anything, then restores whatever was open before.
    internal static void RunInScenes(string title, string[] scenePaths, System.Func<Scene, string> step)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(title, "Exit Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        var report = new List<string>();
        try
        {
            foreach (string path in scenePaths)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    string result = step(scene);
                    report.Add($"{sceneName}: {result}");

                    if (!result.StartsWith("skipped") && !result.StartsWith("ERROR"))
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene))
                            report[report.Count - 1] += "  (ERROR: save failed)";
                    }
                }
                catch (System.Exception e)
                {
                    report.Add($"{sceneName}: ERROR: {e.Message}");
                    Debug.LogException(e);
                }
            }
        }
        finally
        {
            SceneSetup[] restore = previous.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
            if (restore.Length > 0)
            {
                if (!restore.Any(s => s.isActive)) restore[0].isActive = true;
                EditorSceneManager.RestoreSceneManagerSetup(restore);
            }
        }

        string summary = string.Join("\n", report);
        if (report.Any(line => line.Contains("ERROR")))
            Debug.LogError($"[XpUiSetup] {title}\n{summary}");
        else
            Debug.Log($"[XpUiSetup] {title}\n{summary}");
        EditorUtility.DisplayDialog(title, summary, "OK");
    }

    #endregion
}
