using UnityEngine;

/// <summary>
/// Tiny cross-platform haptics helper. <see cref="Tick"/> plays a short, light
/// vibration suitable for UI and gameplay feedback (much lighter than
/// Handheld.Vibrate's long buzz).
///
/// Android: real short ticks via the native Vibrator service. On Android 13+
///          they're tagged as media vibration, so the phone's "Touch feedback"
///          setting doesn't mute them.
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
    private static AndroidJavaObject _attributes; // null below API 33, or if creating them failed
    private static int _sdk = -1;
    private static bool _loggedFailure;

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
            if (_sdk >= 33) _attributes = CreateMediaAttributes();

            // One line in logcat, so a device with no vibration motor (most
            // tablets) is obvious instead of looking like a code bug.
            Debug.Log($"Haptics: Android API {_sdk}, hasVibrator={_vibrator.Call<bool>("hasVibrator")}.");
        }
        catch (System.Exception e)
        {
            _vibrator = null;
            LogFailure("couldn't get the Vibrator service", e);
        }
        return _vibrator != null;
    }

    // Android 13+ files a short vibration with no usage as touch feedback, so a
    // phone with "Touch feedback" (tap/keyboard vibration) switched off silently
    // drops every tick. Tagged as media, ticks follow the "Media vibration"
    // setting instead, which is on by default. The media usage only exists from
    // Android 13, so older versions stay untagged.
    private static AndroidJavaObject CreateMediaAttributes()
    {
        try
        {
            using var attributesClass = new AndroidJavaClass("android.os.VibrationAttributes");
            return attributesClass.CallStatic<AndroidJavaObject>(
                "createForUsage", attributesClass.GetStatic<int>("USAGE_MEDIA"));
        }
        catch (System.Exception e)
        {
            LogFailure("couldn't create media vibration attributes", e);
            return null;
        }
    }

    // Logged once: Tick fires on every drag step, and one line in logcat is
    // enough to see why nothing buzzes.
    private static void LogFailure(string what, System.Exception e)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        Debug.LogWarning($"Haptics: {what}. {e}");
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
                if (_attributes != null) _vibrator.Call("vibrate", effect, _attributes);
                else _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception e)
        {
            // Missing vibrator hardware or permission: no buzz, but say why once.
            LogFailure("vibrate failed", e);
        }
#endif
    }
}
