using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Advertisements;

/// <summary>
/// Central ad manager built on Unity Ads (already installed via com.unity.ads).
/// Persists across scenes like SoundManager. Preloads one interstitial and one
/// rewarded ad, and shows an interstitial every N completed levels.
///
/// Note on the skip button: how many seconds must pass before a player can
/// skip an interstitial is controlled by the ad network (set per placement in
/// the Unity Dashboard / LevelPlay), not by this script — client code can only
/// request that an ad shows and react once it closes.
///
/// Usage:
///   AdManager.Instance.ShowInterstitialThenContinue(() => LoadNextScene());
///   AdManager.Instance.ShowRewardedAd(granted => { if (granted) GiveReward(); });
/// </summary>
[DisallowMultipleComponent]
public class AdManager : MonoBehaviour, IUnityAdsInitializationListener, IUnityAdsLoadListener, IUnityAdsShowListener
{
    public static AdManager Instance { get; private set; }

    [Header("Unity Ads IDs")]
    [Tooltip("From the Unity Dashboard (Unity Gaming Services > Ads > your project).")]
    [SerializeField] private string androidGameId = "800104933";
    [SerializeField] private string iosGameId = "800104933";
    [Tooltip("Use Unity Ads test ads. Leave ON until you have real, approved ad units.")]
    [SerializeField] private bool testMode = true;

    [Header("Placements")]
    [SerializeField] private string androidInterstitialId = "Interstitial_Android";
    [SerializeField] private string iosInterstitialId = "Interstitial_iOS";
    [SerializeField] private string androidRewardedId = "Rewarded_Android";
    [SerializeField] private string iosRewardedId = "Rewarded_iOS";

    [Header("Interstitial cadence")]
    [Tooltip("Show an interstitial after this many completed levels (win or fail).")]
    [SerializeField] private int levelsPerInterstitial = 3;

    private const string PrefLevelsSinceAd = "ads_levels_since_interstitial";

    private string InterstitialId =>
#if UNITY_IOS
        iosInterstitialId;
#else
        androidInterstitialId;
#endif

    private string RewardedId =>
#if UNITY_IOS
        iosRewardedId;
#else
        androidRewardedId;
#endif

    private string GameId =>
#if UNITY_IOS
        iosGameId;
#else
        androidGameId;
#endif

    private bool _initialized;
    private bool _interstitialLoaded;
    private bool _rewardedLoaded;

    private Action _pendingContinue;
    private Action<bool> _pendingRewardResult;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        if (string.IsNullOrEmpty(GameId))
        {
            Debug.LogWarning("AdManager: no Game ID set for this platform. " +
                "Ads are disabled until one is entered from the Unity Dashboard.", this);
            return;
        }

        Advertisement.Initialize(GameId, testMode, this);
    }

    #region Public API

    /// <summary>
    /// Show an interstitial if one is due (every levelsPerInterstitial calls),
    /// then invoke <paramref name="onContinue"/> — whether or not an ad played.
    /// Call this at level-transition points (Next / Retry / Back), not
    /// mid-celebration, so it never interrupts win/fail feedback.
    /// </summary>
    public void ShowInterstitialThenContinue(Action onContinue)
    {
        int count = PlayerPrefs.GetInt(PrefLevelsSinceAd, 0) + 1;

        bool due = count >= Mathf.Max(1, levelsPerInterstitial);
        if (due)
        {
            PlayerPrefs.SetInt(PrefLevelsSinceAd, 0);
            PlayerPrefs.Save();
        }
        else
        {
            PlayerPrefs.SetInt(PrefLevelsSinceAd, count);
            PlayerPrefs.Save();
        }

        if (due && _initialized && _interstitialLoaded)
        {
            _pendingContinue = onContinue;
            Advertisement.Show(InterstitialId, this);
        }
        else
        {
            onContinue?.Invoke();
        }
    }

    /// <summary>
    /// Show a rewarded ad. <paramref name="onResult"/> is called with true only
    /// if the player watched it to completion; false if skipped, failed, or
    /// unavailable. Not hooked to any reward yet — wire it up whenever a
    /// reward (hint, extra time, etc.) is added.
    /// </summary>
    public void ShowRewardedAd(Action<bool> onResult)
    {
        if (!_initialized || !_rewardedLoaded)
        {
            onResult?.Invoke(false);
            return;
        }

        _pendingRewardResult = onResult;
        Advertisement.Show(RewardedId, this);
    }

    /// <summary>True once a rewarded ad is loaded and ready to show.</summary>
    public bool IsRewardedReady => _initialized && _rewardedLoaded;

    #endregion

    #region IUnityAdsInitializationListener

    public void OnInitializationComplete()
    {
        _initialized = true;
        LoadInterstitial();
        LoadRewarded();
    }

    public void OnInitializationFailed(UnityAdsInitializationError error, string message) =>
        Debug.LogWarning($"AdManager: initialization failed ({error}): {message}", this);

    #endregion

    #region Loading

    private void LoadInterstitial()
    {
        _interstitialLoaded = false;
        Advertisement.Load(InterstitialId, this);
    }

    private void LoadRewarded()
    {
        _rewardedLoaded = false;
        Advertisement.Load(RewardedId, this);
    }

    public void OnUnityAdsAdLoaded(string placementId)
    {
        if (placementId == InterstitialId) _interstitialLoaded = true;
        else if (placementId == RewardedId) _rewardedLoaded = true;
    }

    public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
    {
        Debug.LogWarning($"AdManager: failed to load '{placementId}' ({error}): {message}", this);
        StartCoroutine(RetryLoad(placementId));
    }

    private static readonly WaitForSeconds RetryDelay = new(10f);

    private IEnumerator RetryLoad(string placementId)
    {
        yield return RetryDelay;
        if (placementId == InterstitialId) LoadInterstitial();
        else if (placementId == RewardedId) LoadRewarded();
    }

    #endregion

    #region IUnityAdsShowListener

    public void OnUnityAdsShowStart(string placementId) { }
    public void OnUnityAdsShowClick(string placementId) { }

    public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState state)
    {
        if (placementId == InterstitialId)
        {
            LoadInterstitial(); // preload the next one
            Action continue_ = _pendingContinue;
            _pendingContinue = null;
            continue_?.Invoke();
        }
        else if (placementId == RewardedId)
        {
            LoadRewarded();
            bool completed = state == UnityAdsShowCompletionState.COMPLETED;
            Action<bool> result = _pendingRewardResult;
            _pendingRewardResult = null;
            result?.Invoke(completed);
        }
    }

    public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
    {
        Debug.LogWarning($"AdManager: show failed for '{placementId}' ({error}): {message}", this);

        if (placementId == InterstitialId)
        {
            LoadInterstitial();
            Action continue_ = _pendingContinue;
            _pendingContinue = null;
            continue_?.Invoke(); // fail open: never block the player on a broken ad
        }
        else if (placementId == RewardedId)
        {
            LoadRewarded();
            Action<bool> result = _pendingRewardResult;
            _pendingRewardResult = null;
            result?.Invoke(false);
        }
    }

    #endregion
}
