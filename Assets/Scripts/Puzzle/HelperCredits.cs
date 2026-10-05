using UnityEngine;

/// <summary>
/// Uses left for the helper buttons. Shikaku's Hint and Solve All and Amus's
/// Lock each have their own pool of 4; a press costs 1 from that pool only.
/// When a pool is empty the only refill is watching a rewarded ad, which
/// restores that pool to 4. Persisted in PlayerPrefs so it survives app restarts.
/// </summary>
public static class HelperCredits
{
    public const int RefillAmount = 4;

    /// <summary>The helper each pool belongs to.</summary>
    public enum Pool
    {
        Hint,
        SolveAll,
        AmusLock, // Amus: lock one wire onto its correct path
    }

    private static string Key(Pool pool) => $"shikaku_helper_{pool}";

    public static int Remaining(Pool pool) => PlayerPrefs.GetInt(Key(pool), RefillAmount);

    /// <summary>Spend one credit from a pool. False if that pool is empty.</summary>
    public static bool TryUse(Pool pool)
    {
        int remaining = Remaining(pool);
        if (remaining <= 0) return false;

        PlayerPrefs.SetInt(Key(pool), remaining - 1);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>Restore one pool to full (rewarded-ad payoff).</summary>
    public static void Refill(Pool pool) => Set(pool, RefillAmount);

    /// <summary>Set a pool to any count. For testing tools (Shikaku > Testing).</summary>
    public static void Set(Pool pool, int uses)
    {
        PlayerPrefs.SetInt(Key(pool), Mathf.Max(0, uses));
        PlayerPrefs.Save();
    }
}
