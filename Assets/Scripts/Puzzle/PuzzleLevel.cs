using UnityEngine;

/// <summary>
/// Base type for a level in any game mode. A <see cref="LevelDatabase"/> holds a
/// list of these, so one menu/level-select/progress system works across modes.
/// Concrete modes (Shikaku, Preymet, ...) derive their own level asset from this.
/// </summary>
public abstract class PuzzleLevel : ScriptableObject
{
    [Tooltip("Shown on the level-select button. Falls back to 'Level N' if empty.")]
    public string levelName = "New Level";
}
