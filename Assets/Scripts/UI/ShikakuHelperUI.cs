using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The three Shikaku helper buttons:
///   Hint      — places one correct rectangle (own pool of 4)
///   Solve All — completes the level (own separate pool of 4)
///   Undo      — removes the last placed box, unlimited
/// When a pool runs out, pressing that button offers a rewarded ad which
/// refills that pool only. Unusable buttons fade out.
///
/// Put this on the game scene's HUD and assign the board, buttons, and labels.
/// Button clicks are wired in code — no OnClick setup needed.
/// </summary>
public class ShikakuHelperUI : MonoBehaviour
{
    [SerializeField] private GridManager board;

    [Header("Buttons")]
    [SerializeField] private Button hintButton;
    [SerializeField] private Button solveAllButton;
    [SerializeField] private Button undoButton;

    [Header("Remaining-use labels (optional)")]
    [SerializeField] private TMP_Text hintCreditsLabel;
    [SerializeField] private TMP_Text solveAllCreditsLabel;

    [Header("Disabled look")]
    [Range(0.1f, 1f)]
    [Tooltip("Alpha of a button that can't be used right now.")]
    [SerializeField] private float disabledAlpha = 0.4f;
    [Tooltip("Seconds for a button to fade between usable and unusable.")]
    [SerializeField] private float fadeDuration = 0.2f;

    private CanvasGroup _hintGroup;
    private CanvasGroup _solveAllGroup;
    private CanvasGroup _undoGroup;

    private void Awake()
    {
        if (hintButton != null) hintButton.onClick.AddListener(OnHint);
        if (solveAllButton != null) solveAllButton.onClick.AddListener(OnSolveAll);
        if (undoButton != null) undoButton.onClick.AddListener(OnUndo);

        _hintGroup = EnsureGroup(hintButton);
        _solveAllGroup = EnsureGroup(solveAllButton);
        _undoGroup = EnsureGroup(undoButton);
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
        bool boardActive = board != null && !board.IsSolved;
        bool hintCredits = HelperCredits.Remaining(HelperCredits.Pool.Hint) > 0;
        bool solveAllCredits = HelperCredits.Remaining(HelperCredits.Pool.SolveAll) > 0;

        // An empty pool fades the button but keeps it pressable, since pressing
        // it is what offers the rewarded ad that refills that pool.
        Apply(hintButton, _hintGroup, boardActive, boardActive && hintCredits);
        Apply(solveAllButton, _solveAllGroup, boardActive, boardActive && solveAllCredits);

        bool canUndo = boardActive && board.CanUndo;
        Apply(undoButton, _undoGroup, canUndo, canUndo);

        if (hintCreditsLabel != null)
            hintCreditsLabel.text = HelperCredits.Remaining(HelperCredits.Pool.Hint).ToString();
        if (solveAllCreditsLabel != null)
            solveAllCreditsLabel.text = HelperCredits.Remaining(HelperCredits.Pool.SolveAll).ToString();
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

    private void OnHint()
    {
        if (board == null) return;

        if (HelperCredits.Remaining(HelperCredits.Pool.Hint) <= 0)
        {
            OfferRefillAd(HelperCredits.Pool.Hint);
            return;
        }

        // Spend a credit only if the hint actually placed something.
        if (board.UseHint()) HelperCredits.TryUse(HelperCredits.Pool.Hint);
    }

    private void OnSolveAll()
    {
        if (board == null) return;

        if (HelperCredits.Remaining(HelperCredits.Pool.SolveAll) <= 0)
        {
            OfferRefillAd(HelperCredits.Pool.SolveAll);
            return;
        }

        if (board.SolveAll()) HelperCredits.TryUse(HelperCredits.Pool.SolveAll);
    }

    private void OnUndo()
    {
        if (board != null) board.UndoLastMove();
    }

    private void OfferRefillAd(HelperCredits.Pool pool)
    {
        if (AdManager.Instance == null)
        {
            Debug.LogWarning("ShikakuHelperUI: no AdManager in the scene — cannot refill credits.");
            return;
        }

        AdManager.Instance.ShowRewardedAd(completed =>
        {
            if (completed) HelperCredits.Refill(pool);
        });
    }
}
