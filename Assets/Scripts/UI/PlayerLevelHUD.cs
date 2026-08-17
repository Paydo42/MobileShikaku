using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static read-out of the player's global level: the level number, its status
/// title, and an XP bar toward the next level. Drop it on the main menu and on
/// each game scene's HUD — it reads <see cref="PlayerLevel"/> directly, so it
/// needs no wiring beyond its own labels.
///
/// For the animated version shown when a level is completed, use
/// <see cref="XpRewardPanel"/> instead.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLevelHUD : MonoBehaviour
{
    [Header("Labels (all optional)")]
    [Tooltip("Level number, e.g. '12'. Combined with Level Format.")]
    [SerializeField] private TMP_Text levelLabel;
    [Tooltip("Status title, e.g. 'Novice'.")]
    [SerializeField] private TMP_Text titleLabel;
    [Tooltip("Progress within the level, e.g. '45 / 64'.")]
    [SerializeField] private TMP_Text xpLabel;
    [Tooltip("XP still needed, e.g. '19 to next level'.")]
    [SerializeField] private TMP_Text toNextLabel;

    [Header("Bar (assign whichever you use)")]
    [Tooltip("Image with Image Type = Filled; its Fill Amount is driven 0..1.")]
    [SerializeField] private Image xpFill;
    [Tooltip("Slider alternative; its value is driven 0..1.")]
    [SerializeField] private Slider xpBar;

    [Header("Formats")]
    [Tooltip("{0} = level number.")]
    [SerializeField] private string levelFormat = "Lv {0}";
    [Tooltip("{0} = XP into level, {1} = XP the level costs.")]
    [SerializeField] private string xpFormat = "{0} / {1}";
    [Tooltip("{0} = XP remaining to the next level.")]
    [SerializeField] private string toNextFormat = "{0} XP to next level";
    [Tooltip("Shown in place of the numbers once the level cap is reached.")]
    [SerializeField] private string maxLevelText = "MAX";

    private void OnEnable()
    {
        PlayerLevel.LeveledUp += OnLeveledUp;
        Refresh();
    }

    private void OnDisable() => PlayerLevel.LeveledUp -= OnLeveledUp;

    private void OnLeveledUp(int newLevel) => Refresh();

    /// <summary>Re-read the player's level and repaint. Safe to call any time.</summary>
    public void Refresh()
    {
        PlayerLevel.SplitTotalXp(PlayerLevel.TotalXp, out int level, out int into, out int forLevel);
        Apply(level, into, forLevel);
    }

    // Shared by Refresh and, in the reward panel, by the fill animation.
    private void Apply(int level, int xpIntoLevel, int xpForLevel)
    {
        bool atCap = xpForLevel <= 0;

        if (levelLabel != null) levelLabel.text = string.Format(levelFormat, level);
        if (titleLabel != null) titleLabel.text = PlayerLevel.GetTitle(level);

        float fill = atCap ? 1f : Mathf.Clamp01(xpIntoLevel / (float)xpForLevel);
        if (xpFill != null) xpFill.fillAmount = fill;
        if (xpBar != null) xpBar.SetValueWithoutNotify(fill);

        if (xpLabel != null)
            xpLabel.text = atCap ? maxLevelText : string.Format(xpFormat, xpIntoLevel, xpForLevel);

        if (toNextLabel != null)
            toNextLabel.text = atCap
                ? maxLevelText
                : string.Format(toNextFormat, Mathf.Max(0, xpForLevel - xpIntoLevel));
    }
}
