using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the end-of-level panels in a game scene. Hook
/// <see cref="OnLevelSolved"/> to the board's OnSolved event (and, for modes
/// with a fail state like Preymet, <see cref="OnLevelFailed"/> to OnFailed).
/// It unlocks the next level on a win, hides the in-game HUD, and shows the
/// matching panel with Next / Retry / Back buttons.
/// </summary>
public class LevelCompleteController : MonoBehaviour
{
    [Header("Win")]
    [Tooltip("Panel shown when the level is solved. Hidden on start.")]
    [SerializeField] private GameObject winPanel;
    [Tooltip("Next-level button; auto-disabled when there is no next level.")]
    [SerializeField] private Button nextButton;
    [Tooltip("Types out 'You completed <level> in <n> seconds' when the panel appears.")]
    [SerializeField] private TypewriterText resultText;
    [Tooltip("Container holding the win panel's Next/Back buttons. Revealed once the result text finishes typing.")]
    [SerializeField] private GameObject winButtons;
    [Tooltip("The Shikaku board this panel belongs to, so solve time can be read. Leave empty for modes without a solve timer.")]
    [SerializeField] private GridManager shikakuBoard;

    [Header("Fail (optional)")]
    [Tooltip("Panel shown when the level is failed (e.g. time ran out). Hidden on start.")]
    [SerializeField] private GameObject failPanel;

    [Header("Shared")]
    [Tooltip("In-game HUD to hide when a panel appears (optional).")]
    [SerializeField] private GameObject hudToHide;
    [Tooltip("Tiles/board container to hide when a panel appears (optional).")]
    [SerializeField] private GameObject boardToHide;
    [SerializeField] private string levelSelectSceneName = "LevelSelect";

    private void Awake()
    {
        if (winPanel != null) winPanel.SetActive(false);
        if (failPanel != null) failPanel.SetActive(false);
    }

    /// <summary>Call from the board's OnSolved event.</summary>
    public void OnLevelSolved()
    {
        LevelDatabase db = LevelSession.SelectedDatabase;
        int index = LevelSession.SelectedLevel;

        if (db != null)
            LevelProgress.MarkSolved(db.modeId, index);

        bool hasNext = db != null && index + 1 < db.Count;
        if (nextButton != null) nextButton.interactable = hasNext;

        HideInGameViews();
        if (winPanel != null) winPanel.SetActive(true);

        // Buttons stay hidden until the result sentence has finished typing.
        if (winButtons != null) winButtons.SetActive(false);

        // Played after the panel is active, so the typewriter can animate.
        if (resultText != null)
            resultText.Play(BuildSummary(db, index), ShowWinButtons);
        else
            ShowWinButtons();
    }

    private void ShowWinButtons()
    {
        if (winButtons != null) winButtons.SetActive(true);
    }

    // "You completed <i>Level 4</i> in 37 seconds", localized. The level name is
    // italicised with a rich-text tag so the translation strings stay markup-free.
    private string BuildSummary(LevelDatabase db, int index)
    {
        string levelName = db != null ? db.GetDisplayName(index) : string.Empty;
        int seconds = shikakuBoard != null ? Mathf.Max(0, Mathf.RoundToInt(shikakuBoard.SolveTimeSeconds)) : 0;

        string template = LocalizationManager.Get(
            shikakuBoard != null ? "win_summary" : "win_summary_no_time");

        return shikakuBoard != null
            ? string.Format(template, $"<i>{levelName}</i>", seconds)
            : string.Format(template, $"<i>{levelName}</i>");
    }

    /// <summary>Call from the board's OnFailed event (modes with a fail state).</summary>
    public void OnLevelFailed()
    {
        HideInGameViews();
        if (failPanel != null) failPanel.SetActive(true);
    }

    /// <summary>Hide the in-game HUD and board so only the end panel shows.</summary>
    private void HideInGameViews()
    {
        if (hudToHide != null) hudToHide.SetActive(false);
        if (boardToHide != null) boardToHide.SetActive(false);
    }

    /// <summary>
    /// Hook to the Next button's OnClick. Advancing is the only action that
    /// counts toward the interstitial cadence (Retry / Back don't).
    /// </summary>
    public void NextLevel()
    {
        Action loadNext = () =>
        {
            LevelSession.SelectedLevel++;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        };

        if (AdManager.Instance != null)
            AdManager.Instance.ShowInterstitialThenContinue(loadNext);
        else
            loadNext();
    }

    /// <summary>Hook to the Retry button's OnClick (replays the current level).</summary>
    public void RetryLevel() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    /// <summary>Hook to a Back button's OnClick.</summary>
    public void BackToLevelSelect() => SceneManager.LoadScene(levelSelectSceneName);
}
