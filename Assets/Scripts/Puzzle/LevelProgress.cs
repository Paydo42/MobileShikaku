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
