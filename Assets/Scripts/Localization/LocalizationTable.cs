using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds every piece of UI text, keyed by a short id, with one string per
/// language. Create via Create > Shikaku > Localization Table, place it in a
/// folder named exactly "Resources", and name the asset "LocalizationTable"
/// (that's where <see cref="LocalizationManager"/> loads it from).
///
/// Use the ⋮ menu > "Add Starter Keys" to fill in the common UI strings.
/// </summary>
[CreateAssetMenu(fileName = "LocalizationTable", menuName = "Shikaku/Localization Table")]
public class LocalizationTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Short id referenced by LocalizedText, e.g. 'play', 'options'.")]
        public string key;
        [TextArea] public string english;
        [TextArea] public string turkish;

        public string For(Language lang) => lang switch
        {
            Language.Turkish => turkish,
            _ => english,
        };
    }

    public List<Entry> entries = new();

#if UNITY_EDITOR
    [ContextMenu("Add Starter Keys")]
    private void AddStarterKeys()
    {
        void Add(string key, string en, string tr)
        {
            if (entries.Exists(e => e.key == key)) return;
            entries.Add(new Entry { key = key, english = en, turkish = tr });
        }

        Add("play", "Play", "Oyna");
        Add("options", "Options", "Ayarlar");
        Add("music", "Music", "Müzik");
        Add("sound", "Sound Effects", "Ses Efektleri");
        Add("language", "Language", "Dil");
        Add("back", "Back", "Geri");
        Add("quit", "Quit", "Çıkış");
        Add("next", "Next", "Sonraki");
        Add("retry", "Retry", "Tekrar Dene");
        Add("levels", "Levels", "Bölümler");
        Add("level", "Level", "Bölüm");
        Add("select_level", "Select Level", "Bölüm Seç");
        Add("you_win", "You Win!", "Kazandın!");
        // {0} = level name, {1} = seconds. Keep both placeholders when translating.
        Add("win_summary", "You completed {0} in {1} seconds", "{0} bölümünü {1} saniyede tamamladın");
        Add("win_summary_no_time", "You completed {0}", "{0} bölümünü tamamladın");
        Add("time_up", "Time's Up!", "Süre Doldu!");
        Add("locked", "Locked", "Kilitli");
        Add("main_menu", "Main Menu", "Ana Menü");

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
