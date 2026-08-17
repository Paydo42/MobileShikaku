using UnityEngine;

/// <summary>
/// Tiny cross-platform haptics helper. <see cref="Tick"/> plays a short, light
/// vibration suitable for UI and gameplay feedback (much lighter than
/// Handheld.Vibrate's long buzz).
///
/// Android: real short ticks via the native Vibrator service.
/// iOS: no-op — light impact haptics need a native plugin; Handheld.Vibrate
///      would fire a full heavy buzz per tick, which feels wrong.
/// Editor/desktop: no-op.
///
/// Respects <see cref="Enabled"/>, which the options menu drives and which is
/// saved to PlayerPrefs, so callers don't have to check the setting themselves.
/// </summary>
public static class Haptics
{
    private const string PrefEnabled = "haptics_enabled";

    // Android's VibrationEffect.DEFAULT_AMPLITUDE. Letting the device pick the
    // strength it's calibrated for is far more consistent across hardware than
    // a hard-coded number, which can be too weak to feel on some phones.
    private const int DefaultAmplitude = -1;

    private static int _enabled = -1; // -1 = not read from PlayerPrefs yet

    /// <summary>
    /// Whether haptics fire at all. Stored in PlayerPrefs as 0/1 and on by
    /// default, so the player's choice survives restarts.
    /// </summary>
    public static bool Enabled
    {
        get
        {
            if (_enabled < 0) _enabled = PlayerPrefs.GetInt(PrefEnabled, 1);
            return _enabled == 1;
        }
        set
        {
            int next = value ? 1 : 0;
            if (_enabled == next) return;
            _enabled = next;
            PlayerPrefs.SetInt(PrefEnabled, next);
            PlayerPrefs.Save();
        }
    }

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

    /// <summary>
    /// Short light vibration tick. Does nothing when <see cref="Enabled"/> is
    /// off, so call sites don't need to check.
    /// </summary>
    /// <param name="milliseconds">
    /// Tick length. Below roughly 20 ms most phones produce nothing a player can
    /// actually feel, so keep the default unless you've tested on device.
    /// </param>
    /// <param name="amplitude">
    /// Android strength, 1-255, or -1 for the device's calibrated default
    /// (ignored below API 26).
    /// </param>
    public static void Tick(long milliseconds = 30, int amplitude = DefaultAmplitude)
    {
        // Never runs, but referencing Handheld.Vibrate makes Unity add the
        // android.permission.VIBRATE permission to the manifest automatically.
        if (milliseconds == long.MinValue) Handheld.Vibrate();

        if (!Enabled) return;

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
