using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central audio manager for a 2D mobile game.
/// Persists across scenes, exposes simple Play APIs, manages a pool of
/// AudioSources for overlapping SFX, crossfades music, and saves the
/// player's volume/mute preferences to PlayerPrefs.
///
/// Usage:
///   SoundManager.Instance.PlaySfx(myClip);
///   SoundManager.Instance.PlayMusic(myTrack);
///   SoundManager.Instance.SfxVolume = 0.5f;
/// </summary>
[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Music")]
    [Tooltip("Seconds to crossfade between music tracks.")]
    [SerializeField] private float musicCrossfadeDuration = 1f;

    [Header("SFX Pool")]
    [Tooltip("Number of AudioSources available for overlapping sound effects.")]
    [SerializeField] private int sfxPoolSize = 12;

    [Header("Mobile")]
    [Tooltip("Mute everything when the app loses focus / is backgrounded.")]
    [SerializeField] private bool muteOnFocusLoss = true;

    // --- PlayerPrefs keys ---
    private const string PrefMasterVolume = "snd_master_volume";
    private const string PrefMusicVolume = "snd_music_volume";
    private const string PrefSfxVolume = "snd_sfx_volume";
    private const string PrefMuted = "snd_muted";

    // --- Backing state ---
    private float _masterVolume = 1f;
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;
    private bool _muted;

    // Two music sources so we can crossfade between tracks.
    private AudioSource _musicA;
    private AudioSource _musicB;
    private AudioSource _activeMusic;       // currently audible source
    private Coroutine _musicFadeRoutine;

    private readonly List<AudioSource> _sfxPool = new List<AudioSource>();
    private int _sfxCursor;

    #region Public volume / mute API

    /// <summary>Overall volume multiplier [0..1].</summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = Mathf.Clamp01(value); ApplyVolumes(); Save(); }
    }

    /// <summary>Music volume [0..1].</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set { _musicVolume = Mathf.Clamp01(value); ApplyVolumes(); Save(); }
    }

    /// <summary>Sound-effect volume [0..1].</summary>
    public float SfxVolume
    {
        get => _sfxVolume;
        set { _sfxVolume = Mathf.Clamp01(value); ApplyVolumes(); Save(); }
    }

    /// <summary>Mute all audio.</summary>
    public bool Muted
    {
        get => _muted;
        set { _muted = value; ApplyVolumes(); Save(); }
    }

    public void ToggleMute() => Muted = !_muted;

    #endregion

    #region Unity lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);          // DontDestroyOnLoad requires a root object
        DontDestroyOnLoad(gameObject);

        BuildAudioSources();
        Load();
        ApplyVolumes();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (muteOnFocusLoss)
            AudioListener.pause = !hasFocus;
    }

    private void OnApplicationPause(bool paused)
    {
        if (muteOnFocusLoss)
            AudioListener.pause = paused;
    }

    #endregion

    #region SFX

    /// <summary>Play a one-shot sound effect using the pool.</summary>
    /// <param name="clip">Clip to play.</param>
    /// <param name="volumeScale">Per-call volume multiplier [0..1].</param>
    /// <param name="pitch">Playback pitch (1 = normal). Useful for variation.</param>
    public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        AudioSource source = GetNextSfxSource();
        source.pitch = pitch;
        source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    /// <summary>
    /// Play a sound effect with a randomized pitch range for organic variation
    /// (great for repeated SFX like taps, footsteps, coin pickups).
    /// </summary>
    public void PlaySfxRandomPitch(AudioClip clip, float volumeScale = 1f,
        float minPitch = 0.95f, float maxPitch = 1.05f)
    {
        PlaySfx(clip, volumeScale, Random.Range(minPitch, maxPitch));
    }

    private AudioSource GetNextSfxSource()
    {
        // Round-robin; prefer an idle source if one is free.
        for (int i = 0; i < _sfxPool.Count; i++)
        {
            int idx = (_sfxCursor + i) % _sfxPool.Count;
            if (!_sfxPool[idx].isPlaying)
            {
                _sfxCursor = (idx + 1) % _sfxPool.Count;
                return _sfxPool[idx];
            }
        }

        // All busy: steal the next one in rotation (oldest in practice).
        AudioSource source = _sfxPool[_sfxCursor];
        _sfxCursor = (_sfxCursor + 1) % _sfxPool.Count;
        return source;
    }

    #endregion

    #region Music

    /// <summary>
    /// Play a music track, crossfading from whatever is currently playing.
    /// Does nothing if the same clip is already the active track.
    /// </summary>
    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        if (clip == null) return;
        if (_activeMusic != null && _activeMusic.clip == clip && _activeMusic.isPlaying)
            return;

        if (_musicFadeRoutine != null) StopCoroutine(_musicFadeRoutine);
        _musicFadeRoutine = StartCoroutine(CrossfadeMusic(clip, loop));
    }

    /// <summary>Stop music, optionally fading out over the crossfade duration.</summary>
    public void StopMusic(bool fade = true)
    {
        if (_musicFadeRoutine != null) StopCoroutine(_musicFadeRoutine);

        if (fade && _activeMusic != null)
            _musicFadeRoutine = StartCoroutine(FadeOutAndStop(_activeMusic));
        else
        {
            _musicA.Stop();
            _musicB.Stop();
        }
    }

    public void PauseMusic() { if (_activeMusic != null) _activeMusic.Pause(); }
    public void ResumeMusic() { if (_activeMusic != null) _activeMusic.UnPause(); }

    private IEnumerator CrossfadeMusic(AudioClip clip, bool loop)
    {
        AudioSource from = _activeMusic;
        AudioSource to = (_activeMusic == _musicA) ? _musicB : _musicA;

        to.clip = clip;
        to.loop = loop;
        to.volume = 0f;
        to.Play();
        _activeMusic = to;

        float target = TargetMusicVolume();
        float duration = Mathf.Max(0.01f, musicCrossfadeDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = t / duration;
            to.volume = Mathf.Lerp(0f, target, k);
            if (from != null) from.volume = Mathf.Lerp(target, 0f, k);
            yield return null;
        }

        to.volume = target;
        if (from != null) { from.Stop(); from.volume = 0f; }
        _musicFadeRoutine = null;
    }

    private IEnumerator FadeOutAndStop(AudioSource source)
    {
        float start = source.volume;
        float duration = Mathf.Max(0.01f, musicCrossfadeDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }

        source.Stop();
        source.volume = 0f;
        _musicFadeRoutine = null;
    }

    #endregion

    #region Setup / persistence

    private void BuildAudioSources()
    {
        _musicA = CreateChildSource("Music A", loop: true);
        _musicB = CreateChildSource("Music B", loop: true);
        _activeMusic = _musicA;

        for (int i = 0; i < Mathf.Max(1, sfxPoolSize); i++)
            _sfxPool.Add(CreateChildSource($"SFX {i}", loop: false));
    }

    private AudioSource CreateChildSource(string label, bool loop)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;           // 2D game: pure stereo, no 3D attenuation
        return source;
    }

    /// <summary>Recompute and apply effective volumes to every source.</summary>
    private void ApplyVolumes()
    {
        float music = TargetMusicVolume();
        if (_activeMusic != null && _musicFadeRoutine == null)
            _activeMusic.volume = music;

        float sfx = _muted ? 0f : _masterVolume * _sfxVolume;
        foreach (var s in _sfxPool)
            s.volume = sfx;
    }

    private float TargetMusicVolume() => _muted ? 0f : _masterVolume * _musicVolume;

    private void Load()
    {
        _masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, 1f);
        _musicVolume = PlayerPrefs.GetFloat(PrefMusicVolume, 1f);
        _sfxVolume = PlayerPrefs.GetFloat(PrefSfxVolume, 1f);
        _muted = PlayerPrefs.GetInt(PrefMuted, 0) == 1;
    }

    private void Save()
    {
        PlayerPrefs.SetFloat(PrefMasterVolume, _masterVolume);
        PlayerPrefs.SetFloat(PrefMusicVolume, _musicVolume);
        PlayerPrefs.SetFloat(PrefSfxVolume, _sfxVolume);
        PlayerPrefs.SetInt(PrefMuted, _muted ? 1 : 0);
        PlayerPrefs.Save();
    }

    #endregion
}
