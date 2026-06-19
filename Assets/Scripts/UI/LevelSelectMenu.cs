using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the level-select screen for the active game mode: one button per
/// level. Locked levels (ones the player hasn't reached yet) are shown but not
/// clickable. Clicking an unlocked level records the choice and loads the game.
///
/// The active mode is normally set by the game-mode screen
/// (<see cref="GameModeSelector"/>); the serialized fallback below lets you test
/// this scene on its own.
/// </summary>
public class LevelSelectMenu : MonoBehaviour
{
    [Tooltip("Used only if no mode was chosen yet (i.e. when testing this scene directly).")]
    [SerializeField] private LevelDatabase fallbackDatabase;
    [Tooltip("A UI Button prefab. If it has a TMP label child, it gets the level name.")]
    [SerializeField] private Button buttonPrefab;
    [Tooltip("Parent for spawned buttons (give it a Layout Group).")]
    [SerializeField] private Transform buttonContainer;
    [Tooltip("Name of the gameplay scene to load. Must be in Build Settings.")]
    [SerializeField] private string gameSceneName = "Game";

    private void Start()
    {
        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;

        if (db == null || buttonPrefab == null || buttonContainer == null)
        {
            Debug.LogError("LevelSelectMenu: assign a Database, Button Prefab, and Button Container.", this);
            return;
        }

        // Persist the resolved mode so the game scene knows which mode is active.
        LevelSession.SelectedDatabase = db;

        for (int i = 0; i < db.Count; i++)
        {
            int index = i; // capture for the closure
            ShikakuPuzzle level = db.levels[i];
            bool unlocked = LevelProgress.IsUnlocked(db.modeId, index);

            Button button = Instantiate(buttonPrefab, buttonContainer);
            button.interactable = unlocked;

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                string name = level != null && !string.IsNullOrEmpty(level.levelName)
                    ? level.levelName
                    : $"Level {index + 1}";
                label.text = unlocked ? name : $"{name}  (locked)";
            }

            if (unlocked)
                button.onClick.AddListener(() => Play(index));
        }
    }

    private void Play(int index)
    {
        LevelSession.SelectedLevel = index;
        SceneManager.LoadScene(gameSceneName);
    }

    [ContextMenu("Reset Progress (testing)")]
    private void ResetProgress()
    {
        LevelDatabase db = LevelSession.SelectedDatabase != null
            ? LevelSession.SelectedDatabase
            : fallbackDatabase;
        if (db != null) LevelProgress.Reset(db.modeId);
    }
}
