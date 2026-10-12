using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shikaku > Testing: sets the uses left on every helper (Amus Lock, Shikaku
/// Hint and Solve All) for testing.
///   * In the Editor it applies straight away, in or out of Play mode.
///   * Phone builds: it's saved into the game too, and a Development Build
///     (Build Settings > Development Build) gives every helper that amount each
///     time the app starts. Release builds ignore it, so players never get it.
/// "Reset All to Normal" turns both off.
/// </summary>
public static class HelperCreditsTools
{
    private const int Plenty = 99;
    private const string TestFile = "Assets/Resources/" + HelperCredits.TestUsesResource + ".txt";

    [MenuItem("Shikaku/Testing/Helper Uses: Set All to 99")]
    private static void SetPlenty() => SetAll(Plenty, forBuilds: true);

    [MenuItem("Shikaku/Testing/Helper Uses: Set All to 0 (test the ad refill)")]
    private static void SetEmpty() => SetAll(0, forBuilds: true);

    [MenuItem("Shikaku/Testing/Helper Uses: Reset All to Normal (4)")]
    private static void SetNormal() => SetAll(HelperCredits.RefillAmount, forBuilds: false);

    private static void SetAll(int uses, bool forBuilds)
    {
        foreach (HelperCredits.Pool pool in Enum.GetValues(typeof(HelperCredits.Pool)))
            HelperCredits.Set(pool, uses);

        if (forBuilds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TestFile));
            File.WriteAllText(TestFile, uses.ToString());
            AssetDatabase.ImportAsset(TestFile);
            Debug.Log($"[Testing] Every helper now has {uses} uses (Lock, Hint, Solve All). " +
                      "Development Builds will also start with this many each time the app opens.");
        }
        else
        {
            if (File.Exists(TestFile)) AssetDatabase.DeleteAsset(TestFile);
            Debug.Log($"[Testing] Every helper is back to the normal {uses} uses, in the Editor and in builds.");
        }
    }
}
