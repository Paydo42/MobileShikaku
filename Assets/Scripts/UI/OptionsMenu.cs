using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Options panel: music + SFX volume sliders (driven through SoundManager) and
/// language buttons (driven through LocalizationManager). Put this on an object
/// in the MainMenu scene, assign the panel, sliders, and language buttons, then
/// hook Open() to an "Options" button and Close() to the panel's Back button.
/// </summary>
public class OptionsMenu : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panel;

    [Header("Audio (sliders should range 0..1)")]
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    [Header("Language")]
    [SerializeField] private Button englishButton;
    [SerializeField] private Button turkishButton;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);

        if (musicSlider != null) musicSlider.onValueChanged.AddListener(OnMusicChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        if (englishButton != null) englishButton.onClick.AddListener(() => SetLanguage(Language.English));
        if (turkishButton != null) turkishButton.onClick.AddListener(() => SetLanguage(Language.Turkish));
    }

    /// <summary>Hook to the Options button's OnClick.</summary>
    public void Open()
    {
        SyncUI();
        if (panel != null) panel.SetActive(true);
    }

    /// <summary>Hook to the panel's Back/Close button's OnClick.</summary>
    public void Close()
    {
        if (panel != null) panel.SetActive(false);
    }

    // Reflect the saved settings on the controls without firing their events.
    private void SyncUI()
    {
        if (SoundManager.Instance != null)
        {
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(SoundManager.Instance.MusicVolume);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(SoundManager.Instance.SfxVolume);
        }
        UpdateLanguageButtons();
    }

    private void OnMusicChanged(float value)
    {
        if (SoundManager.Instance != null) SoundManager.Instance.MusicVolume = value;
    }

    private void OnSfxChanged(float value)
    {
        if (SoundManager.Instance != null) SoundManager.Instance.SfxVolume = value;
    }

    private void SetLanguage(Language language)
    {
        LocalizationManager.SetLanguage(language);
        UpdateLanguageButtons();
    }

    // The active language's button is disabled, so the selection is visible.
    private void UpdateLanguageButtons()
    {
        if (englishButton != null) englishButton.interactable = LocalizationManager.Current != Language.English;
        if (turkishButton != null) turkishButton.interactable = LocalizationManager.Current != Language.Turkish;
    }
}
