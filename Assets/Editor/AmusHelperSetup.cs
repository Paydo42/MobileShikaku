using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click Editor setup for the Amus helper buttons (Reset, then Tip, Reveal
/// and Lock with their uses-left badges, plus the "tap a tile" hint Reveal
/// shows): builds them on the Amus HUD, just above the level badge, and wires
/// <see cref="AmusHelperUI"/> to the board, buttons and hint.
///
///   Shikaku > Setup > Build Amus Helper Buttons
///
/// Safe to run again. On a scene whose row an older version built, it brings
/// the row up to date and leaves the rest alone: the old Undo button becomes
/// Tip (name, label and badge), and the Reveal button and its hint are added,
/// with the four buttons spaced evenly. Reuses the UI building blocks from
/// <see cref="XpUiSetup"/>.
/// </summary>
public static class AmusHelperSetup
{
    private const string AmusScene = "Assets/Scenes/Amus.unity";
    private const float ButtonWidth = 180f;
    private const float ButtonHeight = 120f;
    private static readonly float[] Slots = { -300f, -100f, 100f, 300f }; // Reset, Tip, Reveal, Lock

    private static readonly Color ButtonTextColor = new(0.13f, 0.16f, 0.24f, 1f);
    private static readonly Color BadgeColor = new(0.9f, 0.3f, 0.25f, 1f);

    [MenuItem("Shikaku/Setup/Build Amus Helper Buttons")]
    private static void Build()
    {
        XpUiSetup.RunInScenes("Build Amus Helper Buttons", new[] { AmusScene }, BuildRow);
    }

    private static string BuildRow(Scene scene)
    {
        AmusHelperUI existing = XpUiSetup.FindInScene<AmusHelperUI>(scene);
        if (existing != null) return UpgradeRow(existing);

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

        Button reset = NewButton(row, "ResetButton", "Reset", Slots[0]);
        Button tip = NewButton(row, "TipButton", "Tip", Slots[1]);
        Button reveal = NewButton(row, "RevealButton", "Reveal", Slots[2]);
        Button lockButton = NewButton(row, "LockButton", "Lock", Slots[3]);

        XpUiSetup.Assign(ui,
            ("board", board), ("tipButton", tip), ("revealButton", reveal), ("resetButton", reset),
            ("lockButton", lockButton), ("tipCreditsLabel", NewBadge(tip.transform)),
            ("revealCreditsLabel", NewBadge(reveal.transform)), ("lockCreditsLabel", NewBadge(lockButton.transform)),
            ("revealHint", NewHint(row)));
        return $"built '{XpUiSetup.GetPath(row)}' (scale {k:0.##})";
    }

    // Brings a row built by an older version of this tool up to date. The Tip
    // field took over the old Undo button's reference, so that button only
    // needs a new name, label and badge.
    private static string UpgradeRow(AmusHelperUI ui)
    {
        var so = new SerializedObject(ui);
        var tip = so.FindProperty("tipButton").objectReferenceValue as Button;
        var reset = so.FindProperty("resetButton").objectReferenceValue as Button;
        var lockButton = so.FindProperty("lockButton").objectReferenceValue as Button;
        bool tipHasBadge = so.FindProperty("tipCreditsLabel").objectReferenceValue != null;
        bool hasReveal = so.FindProperty("revealButton").objectReferenceValue != null;
        bool hasHint = so.FindProperty("revealHint").objectReferenceValue != null;
        var done = new List<string>();

        if (!tipHasBadge)
        {
            if (tip == null) return "ERROR: the helper row has no Tip (or old Undo) button assigned";
            tip.gameObject.name = "TipButton";
            TMP_Text label = tip.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Tip";
            XpUiSetup.Assign(ui, ("tipCreditsLabel", NewBadge(tip.transform)));
            done.Add("turned the Undo button into Tip");
        }

        if (!hasReveal)
        {
            Button reveal = NewButton(ui.transform, "RevealButton", "Reveal", Slots[2]);
            XpUiSetup.Assign(ui, ("revealButton", reveal), ("revealCreditsLabel", NewBadge(reveal.transform)));

            // Four buttons now, spaced evenly: Reset, Tip, Reveal, Lock.
            Fit(reset, Slots[0]);
            Fit(tip, Slots[1]);
            Fit(lockButton, Slots[3]);
            reveal.transform.SetSiblingIndex(lockButton != null ? lockButton.transform.GetSiblingIndex() : 0);
            done.Add("added the Reveal button");
        }

        if (!hasHint)
        {
            XpUiSetup.Assign(ui, ("revealHint", NewHint(ui.transform)));
            done.Add("added the 'tap a tile' hint");
        }

        return done.Count == 0 ? "skipped: the helper row is already up to date" : string.Join(", ", done);
    }

    private static Button NewButton(Transform parent, string objectName, string label, float x)
    {
        RectTransform rect = XpUiSetup.NewRect(objectName, parent);
        XpUiSetup.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f),
            new Vector2(ButtonWidth, ButtonHeight));

        Image background = XpUiSetup.NewImage(rect.gameObject, Color.white, filled: false);
        background.raycastTarget = true; // it's the button's hit area
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        TMP_Text text = XpUiSetup.NewText("Label", rect, label, 46f, TextAlignmentOptions.Center, ButtonTextColor, bold: true);
        Stretch(text.rectTransform);
        return button;
    }

    // Moves and resizes an existing button into a slot of the four-button row.
    private static void Fit(Button button, float x)
    {
        if (button == null) return;
        var rect = (RectTransform)button.transform;
        rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        rect.sizeDelta = new Vector2(ButtonWidth, rect.sizeDelta.y);
    }

    // Round red badge on a button's corner showing uses left.
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

    // "Tap a tile" line just above the buttons, shown only while Reveal waits.
    private static GameObject NewHint(Transform row)
    {
        TMP_Text hint = XpUiSetup.NewText("RevealHint", row, "Tap a tile to reveal its colour", 40f,
            TextAlignmentOptions.Center, Color.white, bold: true);
        XpUiSetup.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 16f),
            new Vector2(780f, 56f));
        hint.gameObject.SetActive(false);
        return hint.gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
