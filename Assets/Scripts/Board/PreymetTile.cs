using TMPro;
using UnityEngine;

/// <summary>
/// View for a single Preymet cell. Put this on your Preymet tile prefab and wire
/// a background <see cref="SpriteRenderer"/> and (optionally) a TMP label used to
/// mark the Start (S) and Goal (G) cells. <see cref="PreymetBoard"/> drives all
/// colour/state.
/// </summary>
[DisallowMultipleComponent]
public class PreymetTile : MonoBehaviour
{
    [SerializeField] private SpriteRenderer background;
    [SerializeField] private TMP_Text label;

    public Vector2Int Coord { get; private set; }

    public void Init(Vector2Int coord, string text)
    {
        Coord = coord;
        name = $"Cell_{coord.x}_{coord.y}";

        if (label != null)
        {
            bool hasText = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(hasText);
            if (hasText) label.text = text;
        }
    }

    public void SetColor(Color color)
    {
        if (background != null) background.color = color;
    }
}
