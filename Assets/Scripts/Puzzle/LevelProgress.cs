using UnityEngine;

/// <summary>
/// Persists how far the player has progressed in each game mode, using
/// PlayerPrefs so it survives app restarts. A mode's progress is stored as the
/// number of unlocked levels: levels with index &lt; that count are playable.
/// Level 1 (index 0) is always unlocked.
/// </summary>
public static class LevelProgress
{
    private static string Key(string modeId) => $"shikaku_progress_{modeId}";

    /// <summary>Number of unlocked levels for a mode (always at least 1).</summary>
    public static int GetUnlockedCount(string modeId) =>
        Mathf.Max(1, PlayerPrefs.GetInt(Key(modeId), 1));

    /// <summary>True if the level at <paramref name="index"/> is unlocked.</summary>
    public static bool IsUnlocked(string modeId, int index) =>
        index < GetUnlockedCount(modeId);

    /// <summary>
    /// The level to drop the player straight into for a mode: the furthest one
    /// they've unlocked but not yet finished. Solving level N unlocks N+1, so
    /// finishing 31 and quitting resumes at 32, while leaving 31 unfinished
    /// resumes at 31. Once every level is solved this clamps to the last one.
    /// </summary>
    public static int GetCurrentLevel(string modeId, int levelCount)
    {
        if (levelCount <= 0) return 0;
        return Mathf.Clamp(GetUnlockedCount(modeId) - 1, 0, levelCount - 1);
    }

    /// <summary>Record a level as solved, unlocking the next one.</summary>
    public static void MarkSolved(string modeId, int index)
    {
        int needed = index + 2; // unlock through the next level (index + 1)
        if (needed > GetUnlockedCount(modeId))
        {
            PlayerPrefs.SetInt(Key(modeId), needed);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Wipe progress for a mode (handy for testing).</summary>
    public static void Reset(string modeId)
    {
        PlayerPrefs.DeleteKey(Key(modeId));
        PlayerPrefs.Save();
    }
}
