using UnityEngine;

/// <summary>
/// One-time runtime setup with no natural home in a scene. Runs before the first
/// scene loads, so it applies however the game was started — including pressing
/// Play directly on a puzzle scene.
/// </summary>
public static class AppBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // Android and iOS cap the frame rate at 30 unless asked otherwise, which
        // makes a dragged selection visibly trail the finger. Mobile ignores the
        // quality settings' vSyncCount, so this is the only knob that matters.
        Application.targetFrameRate = 60;
    }
}
