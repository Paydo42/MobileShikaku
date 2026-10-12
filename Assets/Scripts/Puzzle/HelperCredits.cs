using UnityEngine;

/// <summary>
/// Uses left for the helper buttons. Shikaku's Hint and Solve All and Amus's
/// Tip, Reveal and Lock each have their own pool of 4; a use costs 1 from that
/// pool only.
/// When a pool is empty the only refill is watching a rewarded ad, which
/// restores that pool to 4. Persisted in PlayerPrefs so it survives app restarts.
/// </summary>
public static class HelperCredits
{
    public const int RefillAmount = 4;

    /// <summary>
    /// Resources text file holding a test amount, written by Shikaku > Testing.
    /// Only development builds read it.
    /// </summary>
    public const string TestUsesResource = "HelperTestUses";

    /// <summary>The helper each pool belongs to.</summary>
    public enum Pool
    {
        Hint,
        SolveAll,
        AmusLock, // Amus: lock the right part of every wire, erase the rest
        AmusTip,    // Amus: show one wire's next tile
        AmusReveal, // Amus: show and lock the colour of a tile the player picks
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

    // On a device, in a development build only: if Shikaku > Testing saved a
    // test amount, every helper gets it each time the app starts. The Editor
    // sets its own counts directly, and release builds never do this.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyTestUses()
    {
        if (Application.isEditor || !Debug.isDebugBuild) return;

        var setting = Resources.Load<TextAsset>(TestUsesResource);
        if (setting == null || !int.TryParse(setting.text.Trim(), out int uses)) return;

        foreach (Pool pool in System.Enum.GetValues(typeof(Pool))) Set(pool, uses);
    }
}
