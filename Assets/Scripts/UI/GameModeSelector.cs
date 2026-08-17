using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Represents one game-mode choice (e.g. Shikaku). Put this on the mode's
/// button, assign that mode's Level Database, and hook <see cref="SelectMode"/>
/// to the button's OnClick: the player drops straight into where they left off,
/// with no level-select step in between.
///
/// Hook <see cref="OpenLevelSelect"/> to a second button if you also want a way
/// to browse and replay earlier levels of the same mode.
/// </summary>
public class GameModeSelector : MonoBehaviour
{
    [SerializeField] private LevelDatabase database;
    [SerializeField] private string levelSelectSceneName = "LevelSelect";

    /// <summary>
    /// Hook to the mode button's OnClick. Records the mode and loads the level
    /// the player is up to — the furthest one they've unlocked but not solved.
    /// </summary>
    public void SelectMode()
    {
        if (!ChooseMode()) return;

        LevelSession.SelectedLevel =
            LevelProgress.GetCurrentLevel(database.modeId, database.Count);
        SceneManager.LoadScene(database.gameSceneName);
    }

    /// <summary>
    /// Hook to a "Levels" button. Records the mode and opens level select, so
    /// the player can replay anything they've already unlocked.
    /// </summary>
    public void OpenLevelSelect()
    {
        if (!ChooseMode()) return;
        SceneManager.LoadScene(levelSelectSceneName);
    }

    // Shared validation + mode bookkeeping for both entry points.
    private bool ChooseMode()
    {
        if (database == null)
        {
            Debug.LogError("GameModeSelector: no Level Database assigned.", this);
            return false;
        }

        if (database.Count == 0)
        {
            Debug.LogError($"GameModeSelector: database '{database.name}' has no levels.", this);
            return false;
        }

        LevelSession.SelectedDatabase = database;
        return true;
    }
}
