using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shikaku > Testing: sets the uses left on every helper (Amus Lock, Shikaku
/// Hint and Solve All) while testing in the Editor. Works in and out of Play
/// mode; the buttons update straight away. Only touches this computer's
/// saved data, so phone builds and players are unaffected.
/// </summary>
public static class HelperCreditsTools
{
    private const int Plenty = 99;

    [MenuItem("Shikaku/Testing/Helper Uses: Set All to 99")]
    private static void SetPlenty() => SetAll(Plenty);

    [MenuItem("Shikaku/Testing/Helper Uses: Set All to 0 (test the ad refill)")]
    private static void SetEmpty() => SetAll(0);

    [MenuItem("Shikaku/Testing/Helper Uses: Reset All to Normal (4)")]
    private static void SetNormal() => SetAll(HelperCredits.RefillAmount);

    private static void SetAll(int uses)
    {
        foreach (HelperCredits.Pool pool in Enum.GetValues(typeof(HelperCredits.Pool)))
            HelperCredits.Set(pool, uses);
        Debug.Log($"[Testing] Every helper now has {uses} uses left (Lock, Hint, Solve All).");
    }
}
