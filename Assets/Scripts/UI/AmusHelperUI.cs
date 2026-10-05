using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The three Amus helper buttons:
///   Undo  — steps back one drag or reset, unlimited
///   Reset — clears every wire the player drew (undoable), unlimited
///   Lock  — lays one pair's wire on its correct path and locks it (pool of 4)
/// When the Lock pool runs out, pressing Lock offers a rewarded ad which
/// refills it to 4. Unusable buttons fade out.
///
/// Put this on the Amus scene's HUD and assign the board, buttons, and label.
/// Button clicks are wired in code — no OnClick setup needed.
/// </summary>
public class AmusHelperUI : MonoBehaviour
{
    [SerializeField] private AmusBoard board;

    [Header("Buttons")]
    [SerializeField] private Button undoButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button lockButton;

    [Header("Remaining-use label (optional)")]
    [SerializeField] private TMP_Text lockCreditsLabel;

    [Header("Disabled look")]
    [Range(0.1f, 1f)]
    [Tooltip("Alpha of a button that can't be used right now.")]
    [SerializeField] private float disabledAlpha = 0.4f;
    [Tooltip("Seconds for a button to fade between usable and unusable.")]
    [SerializeField] private float fadeDuration = 0.2f;

    private CanvasGroup _undoGroup;
    private CanvasGroup _resetGroup;
    private CanvasGroup _lockGroup;

    private void Awake()
    {
        if (undoButton != null) undoButton.onClick.AddListener(OnUndo);
        if (resetButton != null) resetButton.onClick.AddListener(OnReset);
        if (lockButton != null) lockButton.onClick.AddListener(OnLock);

        _undoGroup = EnsureGroup(undoButton);
        _resetGroup = EnsureGroup(resetButton);
        _lockGroup = EnsureGroup(lockButton);
    }

    // A CanvasGroup fades the whole button — background, icon and label together.
    private static CanvasGroup EnsureGroup(Button button)
    {
        if (button == null) return null;
        return button.TryGetComponent(out CanvasGroup group)
            ? group
            : button.gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        bool boardActive = board != null && !board.IsFinished;

        bool canUndo = boardActive && board.CanUndo;
        Apply(undoButton, _undoGroup, canUndo, canUndo);

        bool canReset = boardActive && board.CanReset;
        Apply(resetButton, _resetGroup, canReset, canReset);

        // An empty pool fades the button but keeps it pressable, since pressing
        // it is what offers the rewarded ad that refills it. It stays off while
        // the board is still working out its solution.
        bool lockReady = boardActive && board.LockReady;
        bool credits = HelperCredits.Remaining(HelperCredits.Pool.AmusLock) > 0;
        Apply(lockButton, _lockGroup, lockReady, lockReady && credits);

        if (lockCreditsLabel != null)
            lockCreditsLabel.text = HelperCredits.Remaining(HelperCredits.Pool.AmusLock).ToString();
    }

    // 'clickable' controls whether the press registers; 'full' controls the
    // look. They differ for an out-of-credits helper: dimmed but still pressable.
    private void Apply(Button button, CanvasGroup group, bool clickable, bool full)
    {
        if (button != null) button.interactable = clickable;
        if (group == null) return;

        float target = full ? 1f : disabledAlpha;
        group.alpha = fadeDuration > 0f
            ? Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeDuration)
            : target;
    }

    private void OnUndo()
    {
        if (board != null) board.Undo();
    }

    private void OnReset()
    {
        if (board != null) board.ResetBoard();
    }

    private void OnLock()
    {
        if (board == null) return;

        if (HelperCredits.Remaining(HelperCredits.Pool.AmusLock) <= 0)
        {
            OfferRefillAd();
            return;
        }

        // Spend a credit only if a wire actually got locked.
        if (board.LockOnePath()) HelperCredits.TryUse(HelperCredits.Pool.AmusLock);
    }

    private void OfferRefillAd()
    {
        if (AdManager.Instance == null)
        {
            Debug.LogWarning("AmusHelperUI: no AdManager in the scene — cannot refill credits.");
            return;
        }

        AdManager.Instance.ShowRewardedAd(completed =>
        {
            if (completed) HelperCredits.Refill(HelperCredits.Pool.AmusLock);
        });
    }
}
