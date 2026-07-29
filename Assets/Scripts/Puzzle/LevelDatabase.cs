using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
#endif

/// <summary>
/// An ordered list of levels for one game mode. The level-select screen builds
/// one button per entry, and the mode's game scene loads the entry the player
/// chose. Works for any mode because it holds <see cref="PuzzleLevel"/> (the
/// shared base), so it can contain Shikaku or Preymet levels.
///
/// Create via Create > Shikaku > Level Database, set the mode id + game scene,
/// then either drag level assets into the Levels list, or drop them in a folder
/// and use "Refresh Levels From Folder" (Inspector ⋮ menu) to fill it in order.
/// </summary>
[CreateAssetMenu(fileName = "LevelDatabase", menuName = "Shikaku/Level Database")]
public class LevelDatabase : ScriptableObject
{
    [Tooltip("Unique id for this game mode; progress is saved per mode id.")]
    public string modeId = "shikaku";

    [Tooltip("Shown on the game-mode button.")]
    public string displayName = "Shikaku";

    [Tooltip("Scene loaded when a level of this mode is selected. Must be in Build Settings.")]
    public string gameSceneName = "Game";

    [Tooltip("Folder scanned by 'Refresh Levels From Folder'. Empty = the folder this asset is in.")]
    public string levelsFolder = "";

    [Tooltip("This mode's levels, in order. Assets must derive from PuzzleLevel.")]
    public List<PuzzleLevel> levels = new();

    public int Count => levels.Count;

    public PuzzleLevel Get(int index)
    {
        if (levels.Count == 0) return null;
        return levels[Mathf.Clamp(index, 0, levels.Count - 1)];
    }

    /// <summary>
    /// The name shown for a level, localized. A custom levelName is treated as
    /// a localization key (falls back to itself if it isn't one); an empty name
    /// becomes "<Level word> N", so one 'level' translation covers every
    /// numbered level. Shared by the level-select buttons and the win panel so
    /// both always agree on a level's display name.
    /// </summary>
    public string GetDisplayName(int index)
    {
        PuzzleLevel level = Get(index);
        return level != null && !string.IsNullOrEmpty(level.levelName)
            ? LocalizationManager.Get(level.levelName)
            : $"{LocalizationManager.Get("level")} {index + 1}";
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only: replaces the Levels list with every PuzzleLevel asset found
    /// in the folder (numerically sorted, so Level2 comes before Level10).
    /// Keep each mode's levels in their own folder so modes don't mix.
    /// </summary>
    [ContextMenu("Refresh Levels From Folder")]
    private void RefreshFromFolder()
    {
        string folder = levelsFolder;
        if (string.IsNullOrEmpty(folder))
            folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(this))?.Replace('\\', '/');

        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError($"LevelDatabase '{name}': folder '{folder}' not found.", this);
            return;
        }

        var found = AssetDatabase.FindAssets("t:PuzzleLevel", new[] { folder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<PuzzleLevel>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null)
            .ToList();

        found.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));

        levels = found;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        Debug.Log($"LevelDatabase '{name}': loaded {levels.Count} level(s) from '{folder}'.", this);
    }
#endif
}
