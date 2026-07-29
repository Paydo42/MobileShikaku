using UnityEngine;

/// <summary>
/// Tiny cross-platform haptics helper. <see cref="Tick"/> plays a short, light
/// vibration suitable for UI feedback (much lighter than Handheld.Vibrate's
/// long buzz).
///
/// Android: real short ticks via the native Vibrator service.
/// iOS: no-op — light impact haptics need a native plugin; Handheld.Vibrate
///      would fire a full heavy buzz per tick, which feels wrong.
/// Editor/desktop: no-op.
/// </summary>
public static class Haptics
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject _vibrator;
    private static int _sdk = -1;

    private static bool EnsureVibrator()
    {
        if (_vibrator != null) return true;
        try
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            _sdk = version.GetStatic<int>("SDK_INT");

            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
        }
        catch
        {
            _vibrator = null;
        }
        return _vibrator != null;
    }
#endif

    /// <summary>Short light vibration tick (default 25 ms).</summary>
    /// <param name="milliseconds">Tick length in milliseconds.</param>
    /// <param name="amplitude">Android strength 1-255 (ignored below API 26).</param>
    public static void Tick(long milliseconds = 25, int amplitude = 130)
    {
        // Never runs, but referencing Handheld.Vibrate makes Unity add the
        // android.permission.VIBRATE permission to the manifest automatically.
        if (milliseconds == long.MinValue) Handheld.Vibrate();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureVibrator()) return;
        try
        {
            if (_sdk >= 26)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", milliseconds, amplitude);
                _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", milliseconds);
            }
        }
        catch
        {
            // Missing vibrator hardware or permission: silently do nothing.
        }
#endif
    }
}
