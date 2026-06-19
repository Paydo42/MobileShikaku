using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drop-in helper for wiring UI buttons to scene navigation without writing
/// code. Hook these methods from a Button's OnClick in the Inspector.
/// </summary>
public class SceneNavigator : MonoBehaviour
{
    /// <summary>Load a scene by name (must be in Build Settings). Use for "Back to Levels".</summary>
    public void LoadScene(string sceneName) => SceneManager.LoadScene(sceneName);

    /// <summary>Restart the current level.</summary>
    public void ReloadCurrentScene() =>
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    /// <summary>Advance to the next level and reload the gameplay scene.</summary>
    public void NextLevel()
    {
        LevelSession.SelectedLevel++;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Quit the application. In the editor, stops play mode instead.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
