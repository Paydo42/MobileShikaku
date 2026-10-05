using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click Editor setup for the Amus helper buttons (Undo, Reset, Lock with
/// its uses-left badge): builds them on the Amus HUD, just above the level
/// badge, and wires <see cref="AmusHelperUI"/> to the board and buttons.
///
///   Shikaku > Setup > Build Amus Helper Buttons
///
/// Safe to run again: it does nothing if the scene already has an
/// AmusHelperUI. Reuses the UI building blocks from <see cref="XpUiSetup"/>.
/// </summary>
public static class AmusHelperSetup
{
    private const string AmusScene = "Assets/Scenes/Amus.unity";

    private static readonly Color ButtonTextColor = new(0.13f, 0.16f, 0.24f, 1f);
    private static readonly Color BadgeColor = new(0.9f, 0.3f, 0.25f, 1f);

    [MenuItem("Shikaku/Setup/Build Amus Helper Buttons")]
    private static void Build()
    {
        XpUiSetup.RunInScenes("Build Amus Helper Buttons", new[] { AmusScene }, BuildRow);
    }

    private static string BuildRow(Scene scene)
    {
        if (XpUiSetup.FindInScene<AmusHelperUI>(scene) != null) return "skipped: the scene already has an AmusHelperUI";

        AmusBoard board = XpUiSetup.FindInScene<AmusBoard>(scene);
        if (board == null) return "ERROR: no AmusBoard found";
        Transform hud = XpUiSetup.FindPath(scene, "AmusHud");
        if (hud == null) return "ERROR: no root 'AmusHud' canvas found";

        // Bottom-centre, just above the level badge (y 60-200 from the bottom).
        // Under the HUD, so the win panel hides it along with the rest.
        float k = XpUiSetup.DesignScale(hud);
        RectTransform row = XpUiSetup.NewRect("HelperButtons", hud);
        XpUiSetup.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 230f) * k, new Vector2(780f, 130f));
        row.localScale = Vector3.one * k;
        var ui = row.gameObject.AddComponent<AmusHelperUI>();

        Button undo = NewButton(row, "UndoButton", "Undo", -270f);
        Button reset = NewButton(row, "ResetButton", "Reset", 0f);
        Button lockButton = NewButton(row, "LockButton", "Lock", 270f);
        TMP_Text credits = NewBadge(lockButton.transform);

        XpUiSetup.Assign(ui,
            ("board", board), ("undoButton", undo), ("resetButton", reset),
            ("lockButton", lockButton), ("lockCreditsLabel", credits));
        return $"built '{XpUiSetup.GetPath(row)}' (scale {k:0.##})";
    }

    private static Button NewButton(Transform parent, string objectName, string label, float x)
    {
        RectTransform rect = XpUiSetup.NewRect(objectName, parent);
        XpUiSetup.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(230f, 120f));

        Image background = XpUiSetup.NewImage(rect.gameObject, Color.white, filled: false);
        background.raycastTarget = true; // it's the button's hit area
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        TMP_Text text = XpUiSetup.NewText("Label", rect, label, 46f, TextAlignmentOptions.Center, ButtonTextColor, bold: true);
        Stretch(text.rectTransform);
        return button;
    }

    // Round red badge on the Lock button's corner showing uses left.
    private static TMP_Text NewBadge(Transform button)
    {
        RectTransform badge = XpUiSetup.NewRect("UsesLeft", button);
        XpUiSetup.Place(badge, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-12f, -12f), new Vector2(60f, 60f));

        Image dot = XpUiSetup.NewImage(badge.gameObject, BadgeColor, filled: false);
        dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        dot.type = Image.Type.Simple;

        TMP_Text count = XpUiSetup.NewText("Count", badge, "4", 32f, TextAlignmentOptions.Center, Color.white, bold: true);
        Stretch(count.rectTransform);
        return count;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
