using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An ordered list of levels for one game mode. The level-select screen builds
/// one button per entry, and the mode's game scene loads the entry the player
/// chose. Works for any mode because it holds <see cref="PuzzleLevel"/> (the
/// shared base), so it can contain Shikaku or Preymet levels.
///
/// Create via Create > Shikaku > Level Database, set the mode id + game scene,
/// then drag that mode's puzzle assets into the Levels list in order.
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

    [Tooltip("This mode's levels, in order. Assets must derive from PuzzleLevel.")]
    public List<PuzzleLevel> levels = new();

    public int Count => levels.Count;

    public PuzzleLevel Get(int index)
    {
        if (levels.Count == 0) return null;
        return levels[Mathf.Clamp(index, 0, levels.Count - 1)];
    }
}
