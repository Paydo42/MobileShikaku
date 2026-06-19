using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the "level complete" panel in the game scene. Hook
/// <see cref="OnLevelSolved"/> to the GridManager's OnSolved event. It unlocks
/// the next level, then shows the panel with Next / Back-to-levels buttons.
/// </summary>
public class LevelCompleteController : MonoBehaviour
{
    [Tooltip("Panel shown when the level is solved. Hidden on start.")]
    [SerializeField] private GameObject winPanel;
    [Tooltip("Next-level button; auto-disabled when there is no next level.")]
    [SerializeField] private Button nextButton;
    [SerializeField] private string levelSelectSceneName = "LevelSelect";

    private void Awake()
    {
        if (winPanel != null) winPanel.SetActive(false);
    }

    /// <summary>Call from GridManager.OnSolved.</summary>
    public void OnLevelSolved()
    {
        LevelDatabase db = LevelSession.SelectedDatabase;
        int index = LevelSession.SelectedLevel;

        if (db != null)
            LevelProgress.MarkSolved(db.modeId, index);

        bool hasNext = db != null && index + 1 < db.Count;
        if (nextButton != null) nextButton.interactable = hasNext;

        if (winPanel != null) winPanel.SetActive(true);
    }

    /// <summary>Hook to the Next button's OnClick.</summary>
    public void NextLevel()
    {
        LevelSession.SelectedLevel++;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Hook to the Back button's OnClick.</summary>
    public void BackToLevelSelect() => SceneManager.LoadScene(levelSelectSceneName);
}
