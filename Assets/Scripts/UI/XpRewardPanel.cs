using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The XP payout shown on the win panel: "+45 XP", then the bar slides from
/// where the player was to where the gain puts them, ticking the level number
/// over if it crosses a threshold (more than once, if the gain is big enough).
///
/// Put it on the win panel, assign the labels and bar, and assign it to
/// <see cref="LevelCompleteController"/>'s "Xp Panel" field — that calls
/// <see cref="Play"/> with the before/after values.
///
/// The animation sweeps a *displayed* XP total and re-derives level and
/// progress from it each frame, so multi-level gains fall out for free rather
/// than needing special cases.
/// </summary>
[DisallowMultipleComponent]
public class XpRewardPanel : MonoBehaviour
{
    [Header("Labels (all optional)")]
    [Tooltip("The gain, e.g. '+45 XP'.")]
    [SerializeField] private TMP_Text gainedLabel;
    [Tooltip("Level number, e.g. 'Lv 12'.")]
    [SerializeField] private TMP_Text levelLabel;
    [Tooltip("Status title, e.g. 'Novice'.")]
    [SerializeField] private TMP_Text titleLabel;
    [Tooltip("Progress within the level, e.g. '45 / 64'.")]
    [SerializeField] private TMP_Text xpLabel;
    [Tooltip("XP still needed, e.g. '19 XP to next level'.")]
    [SerializeField] private TMP_Text toNextLabel;

    [Header("Bar (assign whichever you use)")]
    [Tooltip("Image with Image Type = Filled; its Fill Amount is driven 0..1.")]
    [SerializeField] private Image xpFill;
    [Tooltip("Slider alternative; its value is driven 0..1.")]
    [SerializeField] private Slider xpBar;

    [Header("Timing")]
    [Tooltip("Pause before the bar starts moving, so the panel can settle first.")]
    [SerializeField] private float startDelay = 0.35f;
    [Tooltip("How long the whole slide takes, however much XP was gained.")]
    [SerializeField] private float fillDuration = 1.1f;

    [Header("Formats")]
    [Tooltip("{0} = XP gained.")]
    [SerializeField] private string gainedFormat = "+{0} XP";
    [Tooltip("Shown when the level was already cleared before, so it paid no XP.")]
    [SerializeField] private string replayText = "Already completed";
    [Tooltip("{0} = level number.")]
    [SerializeField] private string levelFormat = "Lv {0}";
    [Tooltip("{0} = XP into level, {1} = XP the level costs.")]
    [SerializeField] private string xpFormat = "{0} / {1}";
    [Tooltip("{0} = XP remaining to the next level.")]
    [SerializeField] private string toNextFormat = "{0} XP to next level";
    [Tooltip("Shown in place of the numbers once the level cap is reached.")]
    [SerializeField] private string maxLevelText = "MAX";

    [Header("Events")]
    [Tooltip("Fired each time the bar rolls over into a new level. Hook a sound, a badge, confetti.")]
    public UnityEvent OnLevelUp;

    private Coroutine _routine;

    /// <summary>
    /// Animate from <paramref name="xpBefore"/> (lifetime XP before this win) up
    /// by <paramref name="xpGained"/>. A gain of 0 — a replay — just shows the
    /// current standing without sliding.
    /// </summary>
    public void Play(int xpBefore, int xpGained)
    {
        if (_routine != null) StopCoroutine(_routine);

        if (!gameObject.activeInHierarchy)
        {
            // Can't run a coroutine on an inactive object; show the end state.
            ApplyForTotal(xpBefore + Mathf.Max(0, xpGained));
            return;
        }

        _routine = StartCoroutine(PlayRoutine(xpBefore, Mathf.Max(0, xpGained)));
    }

    private IEnumerator PlayRoutine(int xpBefore, int xpGained)
    {
        if (gainedLabel != null)
            gainedLabel.text = xpGained > 0 ? string.Format(gainedFormat, xpGained) : replayText;

        // Start from where the player stood before this win.
        ApplyForTotal(xpBefore);

        if (xpGained <= 0)
        {
            _routine = null;
            yield break;
        }

        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        PlayerLevel.SplitTotalXp(xpBefore, out int shownLevel, out _, out _);

        int target = xpBefore + xpGained;
        float elapsed = 0f;
        while (elapsed < fillDuration)
        {
            elapsed += Time.deltaTime;
            float t = fillDuration > 0f ? Mathf.Clamp01(elapsed / fillDuration) : 1f;

            int displayed = Mathf.RoundToInt(Mathf.Lerp(xpBefore, target, t));
            int level = ApplyForTotal(displayed);

            // Crossing a threshold mid-slide: announce every level gained.
            while (shownLevel < level)
            {
                shownLevel++;
                Haptics.Tick();
                OnLevelUp?.Invoke();
            }

            yield return null;
        }

        ApplyForTotal(target);
        _routine = null;
    }

    // Paints the whole panel for a lifetime-XP value; returns the level it maps to.
    private int ApplyForTotal(int totalXp)
    {
        PlayerLevel.SplitTotalXp(totalXp, out int level, out int into, out int forLevel);
        bool atCap = forLevel <= 0;

        if (levelLabel != null) levelLabel.text = string.Format(levelFormat, level);
        if (titleLabel != null) titleLabel.text = PlayerLevel.GetTitle(level);

        float fill = atCap ? 1f : Mathf.Clamp01(into / (float)forLevel);
        if (xpFill != null) xpFill.fillAmount = fill;
        if (xpBar != null) xpBar.SetValueWithoutNotify(fill);

        if (xpLabel != null)
            xpLabel.text = atCap ? maxLevelText : string.Format(xpFormat, into, forLevel);

        if (toNextLabel != null)
            toNextLabel.text = atCap
                ? maxLevelText
                : string.Format(toNextFormat, Mathf.Max(0, forLevel - into));

        return level;
    }
}
