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

    /// <summary>Hook to the Next button's OnClick.</summary>
    public void NextLevel()
    {
        LevelSession.SelectedLevel++;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Hook to the Retry button's OnClick (replays the current level).</summary>
    public void RetryLevel() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    /// <summary>Hook to a Back button's OnClick.</summary>
    public void BackToLevelSelect() => SceneManager.LoadScene(levelSelectSceneName);
}
