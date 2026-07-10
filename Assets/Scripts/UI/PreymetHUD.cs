using TMPro;
using UnityEngine;

/// <summary>
/// Shows the Preymet target and countdown at the top of the screen: how many
/// tiles the path must use (and how many are used so far), plus the remaining
/// time when the level has a limit. Put this on a Canvas object in the Preymet
/// scene and assign the board and the TMP labels.
/// </summary>
public class PreymetHUD : MonoBehaviour
{
    [SerializeField] private PreymetBoard board;
    [Tooltip("Shows used / target tiles.")]
    [SerializeField] private TMP_Text tilesLabel;
    [Tooltip("Shows the countdown (mm:ss). Hidden when the level has no time limit.")]
    [SerializeField] private TMP_Text timerLabel;

    private void Update()
    {
        if (board == null) return;

        if (tilesLabel != null)
            tilesLabel.text = $"{board.PathLength} / {board.TargetTiles}";

        if (timerLabel != null)
        {
            bool show = board.HasTimeLimit;
            if (timerLabel.gameObject.activeSelf != show)
                timerLabel.gameObject.SetActive(show);

            if (show)
            {
                int seconds = Mathf.CeilToInt(board.TimeRemaining);
                timerLabel.text = $"{seconds / 60:0}:{seconds % 60:00}";
            }
        }
    }
}
