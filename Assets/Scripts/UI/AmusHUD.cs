using TMPro;
using UnityEngine;

/// <summary>
/// Shows Amus progress at the top of the screen: how many colour pairs are
/// connected so far, plus the remaining time when the level has a limit. Put
/// this on a Canvas object in the Amus scene and assign the board and the TMP
/// labels.
/// </summary>
public class AmusHUD : MonoBehaviour
{
    [SerializeField] private AmusBoard board;
    [Tooltip("Shows connected / total pairs.")]
    [SerializeField] private TMP_Text pairsLabel;
    [Tooltip("Shows the countdown (mm:ss). Hidden when the level has no time limit.")]
    [SerializeField] private TMP_Text timerLabel;

    [Header("Low-time warning")]
    [Tooltip("When this many seconds remain, the timer turns red and pulses.")]
    [SerializeField] private float warningSeconds = 10f;
    [SerializeField] private Color warningColor = new(0.90f, 0.25f, 0.20f);
    [Range(0f, 0.5f)]
    [Tooltip("How much the timer grows on each second's tick.")]
    [SerializeField] private float pulseScale = 0.2f;

    private Color _timerNormalColor;
    private Vector3 _timerBaseScale;

    private void Awake()
    {
        if (timerLabel != null)
        {
            _timerNormalColor = timerLabel.color;
            _timerBaseScale = timerLabel.transform.localScale;
        }
    }

    private void Update()
    {
        if (board == null) return;

        if (pairsLabel != null)
            pairsLabel.text = $"{board.ConnectedPairs} / {board.TotalPairs}";

        if (timerLabel != null)
        {
            bool show = board.HasTimeLimit;
            if (timerLabel.gameObject.activeSelf != show)
                timerLabel.gameObject.SetActive(show);

            if (show)
            {
                int seconds = Mathf.CeilToInt(board.TimeRemaining);
                timerLabel.text = $"{seconds / 60:0}:{seconds % 60:00}";

                bool warning = board.TimeRemaining > 0f && board.TimeRemaining <= warningSeconds;
                timerLabel.color = warning ? warningColor : _timerNormalColor;

                if (warning)
                {
                    // Punch the scale as each second ticks over, then settle:
                    // the fractional part is ~1 right after a tick and decays
                    // to 0; raising it to a power sharpens the pulse.
                    float frac = board.TimeRemaining % 1f;
                    float punch = Mathf.Pow(frac, 6f);
                    timerLabel.transform.localScale = _timerBaseScale * (1f + pulseScale * punch);
                }
                else
                {
                    timerLabel.transform.localScale = _timerBaseScale;
                }
            }
        }
    }
}
