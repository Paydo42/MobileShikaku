using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The four Amus helper buttons:
///   Reset  — clears every wire the player drew except locked parts, unlimited
///   Tip    — pulses one unfinished wire and marks the tile it should go to
///            next (pool of 4)
///   Reveal — the player taps any tile; it shows the colour it needs and is
///            locked to it (pool of 4). While it waits for the tap the button
///            pulses and the hint shows; pressing it again cancels for free.
///   Lock   — locks the right part of every wire the player drew (whole
///            wires, or the right stretch of unfinished ones) and erases the
///            rest (pool of 4)
/// When a pool runs out, pressing its button offers a rewarded ad which
/// refills it to 4. Unusable buttons fade out.
///
/// Put this on the Amus scene's HUD and assign the board, buttons, and labels.
/// Button clicks are wired in code — no OnClick setup needed.
/// </summary>
public class AmusHelperUI : MonoBehaviour
{
    [SerializeField] private AmusBoard board;

    [Header("Buttons")]
    [FormerlySerializedAs("undoButton")] // Tip took the old Undo button's place
    [SerializeField] private Button tipButton;
    [SerializeField] private Button revealButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button lockButton;

    [Header("Remaining-use labels (optional)")]
    [SerializeField] private TMP_Text tipCreditsLabel;
    [SerializeField] private TMP_Text revealCreditsLabel;
    [SerializeField] private TMP_Text lockCreditsLabel;

    [Header("Reveal")]
    [Tooltip("Shown while Reveal waits for the player to tap a tile, e.g. a 'Tap a tile' label. Optional.")]
    [SerializeField] private GameObject revealHint;
    [Range(0f, 0.3f)]
    [Tooltip("How much the Reveal button pulses while it waits for the tap.")]
    [SerializeField] private float revealPulse = 0.08f;

    [Header("Disabled look")]
    [Range(0.1f, 1f)]
    [Tooltip("Alpha of a button that can't be used right now.")]
    [SerializeField] private float disabledAlpha = 0.4f;
    [Tooltip("Seconds for a button to fade between usable and unusable.")]
    [SerializeField] private float fadeDuration = 0.2f;

    private CanvasGroup _tipGroup;
    private CanvasGroup _revealGroup;
    private CanvasGroup _resetGroup;
    private CanvasGroup _lockGroup;
    private Vector3 _revealRestScale = Vector3.one;

    private void Awake()
    {
        if (tipButton != null) tipButton.onClick.AddListener(OnTip);
        if (revealButton != null) revealButton.onClick.AddListener(OnReveal);
        if (resetButton != null) resetButton.onClick.AddListener(OnReset);
        if (lockButton != null) lockButton.onClick.AddListener(OnLock);

        _tipGroup = EnsureGroup(tipButton);
        _revealGroup = EnsureGroup(revealButton);
        _resetGroup = EnsureGroup(resetButton);
        _lockGroup = EnsureGroup(lockButton);
        if (revealButton != null) _revealRestScale = revealButton.transform.localScale;
        if (revealHint != null) revealHint.SetActive(false);
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

        bool canReset = boardActive && board.CanReset;
        Apply(resetButton, _resetGroup, canReset, canReset);

        // Each stays off while it has nothing to work on (or the board is still
        // working out its solution).
        ApplyHelper(tipButton, _tipGroup, tipCreditsLabel, HelperCredits.Pool.AmusTip,
            boardActive && board.TipReady);
        ApplyHelper(revealButton, _revealGroup, revealCreditsLabel, HelperCredits.Pool.AmusReveal,
            boardActive && board.RevealReady);
        ApplyHelper(lockButton, _lockGroup, lockCreditsLabel, HelperCredits.Pool.AmusLock,
            boardActive && board.LockReady);

        // While Reveal waits for a tap: the button pulses and the hint shows.
        bool picking = boardActive && board.PickingReveal;
        if (revealHint != null && revealHint.activeSelf != picking) revealHint.SetActive(picking);
        if (revealButton != null)
        {
            float pulse = picking ? 1f + revealPulse * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            revealButton.transform.localScale = _revealRestScale * pulse;
        }
    }

    // An empty pool fades its button but keeps it pressable, since pressing
    // it is what offers the rewarded ad that refills it.
    private void ApplyHelper(Button button, CanvasGroup group, TMP_Text label, HelperCredits.Pool pool, bool ready)
    {
        int left = HelperCredits.Remaining(pool);
        Apply(button, group, ready, ready && left > 0);
        if (label != null) label.text = left.ToString();
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

    private void OnTip() => UseHelper(HelperCredits.Pool.AmusTip, () => board.ShowTip());

    // First press: wait for the player to tap a tile (the use is spent when a
    // tile is revealed). Pressing again while waiting cancels, for free.
    private void OnReveal()
    {
        if (board == null) return;

        if (board.PickingReveal)
        {
            board.CancelRevealPick();
            return;
        }

        UseHelper(HelperCredits.Pool.AmusReveal, () =>
        {
            board.StartRevealPick(() => HelperCredits.TryUse(HelperCredits.Pool.AmusReveal));
            return false; // nothing to spend yet
        });
    }

    private void OnReset()
    {
        if (board == null) return;
        board.CancelRevealPick();
        board.ResetBoard();
    }

    private void OnLock() => UseHelper(HelperCredits.Pool.AmusLock, () => board.LockCorrectWires());

    // With uses left, runs the helper and spends one only if it did something;
    // with none, offers the ad instead. Any other helper stops a Reveal pick.
    private void UseHelper(HelperCredits.Pool pool, Func<bool> use)
    {
        if (board == null) return;
        board.CancelRevealPick();

        if (HelperCredits.Remaining(pool) <= 0)
        {
            OfferRefillAd(pool);
            return;
        }

        if (use()) HelperCredits.TryUse(pool);
    }

    private void OfferRefillAd(HelperCredits.Pool pool)
    {
        if (AdManager.Instance == null)
        {
            Debug.LogWarning("AmusHelperUI: no AdManager in the scene — cannot refill credits.");
            return;
        }

        AdManager.Instance.ShowRewardedAd(completed =>
        {
            if (completed) HelperCredits.Refill(pool);
        });
    }
}
