using TMPro;
using UnityEngine;

/// <summary>
/// Shows which level is being played, for the in-game HUD. Works in every mode's
/// game scene without any mode-specific code, because it reads the choice from
/// <see cref="LevelSession"/> and asks that mode's <see cref="LevelDatabase"/>
/// for the name — the same call the level-select buttons and the win panel use,
/// so all three always agree on what a level is called.
///
/// Put it on a TMP text in the HUD. If that text sits under the object wired to
/// LevelCompleteController's "Hud To Hide", it disappears with the rest of the
/// HUD when the level ends.
/// </summary>
[DisallowMultipleComponent]
public class LevelLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [Tooltip("Used only if no mode was chosen yet (i.e. when testing this scene directly).")]
    [SerializeField] private LevelDatabase fallbackDatabase;
    [Tooltip("Prefix with the mode name, e.g. 'Amus - Level 3'.")]
    [SerializeField] private bool showModeName;
    [Tooltip("Append how many levels the mode has, e.g. 'Level 3 / 12'.")]
    [SerializeField] private bool showTotal;
    [Tooltip("Placed between the mode name and the level name.")]
    [SerializeField] private string modeSeparator = " - ";

    private void Reset() => label = GetComponent<TMP_Text>();

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
    }

    // Re-read on a language switch, in case the player changes language from an
    // in-game options panel while the level is open.
    private void OnEnable()
    {
        LocalizationManager.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => LocalizationManager.LanguageChanged -= Refresh;

    private void Refresh()
    {
        if (label == null) return;

        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;

        if (db == null || db.Count == 0)
        {
            label.text = string.Empty;
            return;
        }

        int index = Mathf.Clamp(LevelSession.SelectedLevel, 0, db.Count - 1);
        string text = db.GetDisplayName(index);

        if (showTotal) text = $"{text} / {db.Count}";
        if (showModeName) text = $"{db.displayName}{modeSeparator}{text}";

        label.text = text;
    }
}
