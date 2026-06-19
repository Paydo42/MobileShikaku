using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An ordered list of Shikaku levels. The level-select screen builds one button
/// per entry, and the game scene loads the entry the player chose.
///
/// Create via Create > Shikaku > Level Database, then drag your puzzle assets
/// into the Levels list in the order you want them to appear.
/// </summary>
[CreateAssetMenu(fileName = "LevelDatabase", menuName = "Shikaku/Level Database")]
public class LevelDatabase : ScriptableObject
{
    [Tooltip("Unique id for this game mode; progress is saved per mode id.")]
    public string modeId = "shikaku";

    [Tooltip("Shown on the game-mode button.")]
    public string displayName = "Shikaku";

    public List<ShikakuPuzzle> levels = new();

    public int Count => levels.Count;

    public ShikakuPuzzle Get(int index)
    {
        if (levels.Count == 0) return null;
        return levels[Mathf.Clamp(index, 0, levels.Count - 1)];
    }
}
