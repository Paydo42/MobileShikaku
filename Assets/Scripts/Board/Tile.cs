using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// View for a single board cell. Put this on your Tile prefab and wire a
/// background <see cref="SpriteRenderer"/> and (optionally) a TMP label for
/// the clue number. The <see cref="GridManager"/> drives all colour/state.
/// </summary>
[DisallowMultipleComponent]
public class Tile : MonoBehaviour
{
    [Tooltip("Background sprite whose colour reflects the tile's state.")]
    [SerializeField] private SpriteRenderer background;

    [Tooltip("Label that shows the clue number. Hidden on non-clue tiles.")]
    [SerializeField] private TMP_Text label;

    /// <summary>Grid coordinate (x = column, y = row).</summary>
    public Vector2Int Coord { get; private set; }

    /// <summary>Clue value for this tile, or 0 if it has no clue.</summary>
    public int Clue { get; private set; }

    public void Init(Vector2Int coord, int clue)
    {
        Coord = coord;
        Clue = clue;
        name = $"Tile_{coord.x}_{coord.y}";

        if (label != null)
        {
            bool hasClue = clue > 0;
            label.gameObject.SetActive(hasClue);
            if (hasClue)
            {
                label.text = clue.ToString();

                // Centre the digits within the label's rect and never wrap a
                // multi-digit clue onto a second line. The rect's placement
                // (pivot/position) is the prefab's responsibility.
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;

                // Keep the number above the rectangle boxes drawn over the board.
                var labelRenderer = label.GetComponent<Renderer>();
                if (labelRenderer != null) labelRenderer.sortingOrder = 20;
            }
        }
    }

    public void SetColor(Color color)
    {
        if (background != null) background.color = color;
    }

    // Scale the tile once auto-fit has run, so the pop animates around the
    // correct resting size.
    private Vector3 _restScale = Vector3.one;
    private Coroutine _popRoutine;

    private void Start() => _restScale = transform.localScale;

    /// <summary>Play a quick scale "pop" (used when a rectangle is placed).</summary>
    public void PlayPop(float strength, float duration)
    {
        if (!gameObject.activeInHierarchy) return;
        if (_popRoutine != null) StopCoroutine(_popRoutine);
        _popRoutine = StartCoroutine(PopRoutine(strength, duration));
    }

    private IEnumerator PopRoutine(float strength, float duration)
    {
        Vector3 peak = _restScale * strength;
        float half = Mathf.Max(0.01f, duration * 0.5f);

        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(_restScale, peak, t / half);
            yield return null;
        }
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(peak, _restScale, t / half);
            yield return null;
        }

        transform.localScale = _restScale;
        _popRoutine = null;
    }
}
