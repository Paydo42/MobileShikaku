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

    private void Reset() => label = FindLabel();

    private void Awake()
    {
        if (label == null) label = FindLabel();
        if (label == null)
            Debug.LogWarning($"LocalizedText on '{name}': no TMP text on this object or its children.", this);
    }

    // Toggles and buttons keep their text on a child ("Label"), so when this
    // component sits on the control's root there is nothing to find on the root
    // itself. Inactive children count too, since options panels start hidden.
    private TMP_Text FindLabel()
    {
        var own = GetComponent<TMP_Text>();
        return own != null ? own : GetComponentInChildren<TMP_Text>(true);
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
