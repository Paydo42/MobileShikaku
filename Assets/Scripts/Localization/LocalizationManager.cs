using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central, always-available localization service. Loads the LocalizationTable
/// from a Resources folder on first use, remembers the chosen language in
/// PlayerPrefs, and notifies listeners when the language changes.
///
/// It's static, so any scene can call <see cref="Get"/> without a manager object
/// existing in that scene.
/// </summary>
public static class LocalizationManager
{
    private const string ResourcePath = "LocalizationTable"; // Assets/Resources/LocalizationTable.asset
    private const string PrefLanguage = "loc_language";

    /// <summary>Raised after the language changes, so UI can refresh.</summary>
    public static event Action LanguageChanged;

    private static Dictionary<string, LocalizationTable.Entry> _lookup;
    private static Language _current;
    private static bool _loaded;

    public static Language Current
    {
        get { EnsureLoaded(); return _current; }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var table = Resources.Load<LocalizationTable>(ResourcePath);
        _lookup = new Dictionary<string, LocalizationTable.Entry>();
        if (table != null)
        {
            foreach (var e in table.entries)
                if (!string.IsNullOrEmpty(e.key)) _lookup[e.key] = e;
        }
        else
        {
            Debug.LogWarning($"LocalizationManager: no LocalizationTable at Resources/{ResourcePath}. " +
                "Create one (Create > Shikaku > Localization Table) inside an 'Assets/Resources' folder.");
        }

        // Saved choice wins; otherwise guess from the device language.
        if (PlayerPrefs.HasKey(PrefLanguage))
            _current = (Language)PlayerPrefs.GetInt(PrefLanguage);
        else
            _current = Application.systemLanguage == SystemLanguage.Turkish
                ? Language.Turkish
                : Language.English;
    }

    public static void SetLanguage(Language language)
    {
        EnsureLoaded();
        if (_current == language) return;

        _current = language;
        PlayerPrefs.SetInt(PrefLanguage, (int)language);
        PlayerPrefs.Save();
        LanguageChanged?.Invoke();
    }

    /// <summary>Translated text for a key, in the current language.</summary>
    public static string Get(string key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (_lookup.TryGetValue(key, out var entry))
        {
            string value = entry.For(_current);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        return key; // missing keys show as their id, so gaps are obvious
    }
}
