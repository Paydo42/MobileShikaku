using TMPro;
using UnityEngine;

/// <summary>
/// Put this on any TMP text to make it localized. Set the Key to a
/// LocalizationTable id (e.g. "play"); the text fills in for the current
/// language and updates automatically whenever the language is switched.
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("A key from the LocalizationTable, e.g. 'play', 'options'.")]
    [SerializeField] private string key;
    [SerializeField] private TMP_Text label;

    private void Reset() => label = GetComponent<TMP_Text>();

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        LocalizationManager.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => LocalizationManager.LanguageChanged -= Refresh;

    /// <summary>Change the key at runtime (e.g. dynamic labels).</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Refresh();
    }

    private void Refresh()
    {
        if (label != null) label.text = LocalizationManager.Get(key);
    }
}
