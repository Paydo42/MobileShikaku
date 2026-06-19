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
            if (hasClue) label.text = clue.ToString();
        }
    }

    public void SetColor(Color color)
    {
        if (background != null) background.color = color;
    }
}
