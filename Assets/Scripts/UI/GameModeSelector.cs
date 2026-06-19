using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Represents one game-mode choice (e.g. Shikaku). Put this on the mode's
/// button, assign that mode's Level Database, and hook <see cref="SelectMode"/>
/// to the button's OnClick. It records the active mode and opens level select.
/// </summary>
public class GameModeSelector : MonoBehaviour
{
    [SerializeField] private LevelDatabase database;
    [SerializeField] private string levelSelectSceneName = "LevelSelect";

    public void SelectMode()
    {
        if (database == null)
        {
            Debug.LogError("GameModeSelector: no Level Database assigned.", this);
            return;
        }

        LevelSession.SelectedDatabase = database;
        SceneManager.LoadScene(levelSelectSceneName);
    }
}
