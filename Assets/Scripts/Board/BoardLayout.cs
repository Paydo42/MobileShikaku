using UnityEngine;

/// <summary>
/// Shared helpers for laying out board tiles, so both game modes size tiles the
/// same way.
/// </summary>
public static class BoardLayout
{
    /// <summary>
    /// Scales a spawned tile so its sprite fits within one cell (times
    /// <paramref name="fill"/>, leaving a gap). Uses the sprite's geometry, so it
    /// works whatever the prefab's sprite or scale is. Assumes the tile's
    /// SpriteRenderer is on the root or a non-scaled child.
    /// </summary>
    /// <summary>
    /// Computes a cell size so a columns x rows board fits inside the camera's
    /// view (times <paramref name="screenFill"/>, leaving a margin), capped at
    /// <paramref name="maxCellSize"/> so small boards don't get giant tiles.
    /// Assumes an orthographic camera centred on the board.
    /// </summary>
    public static float FitCellSize(Camera cam, int columns, int rows,
        float screenFill, float maxCellSize)
    {
        if (cam == null || !cam.orthographic || columns <= 0 || rows <= 0)
            return maxCellSize;

        float availHeight = 2f * cam.orthographicSize * screenFill;
        float availWidth = 2f * cam.orthographicSize * cam.aspect * screenFill;
        return Mathf.Min(maxCellSize, availWidth / columns, availHeight / rows);
    }

    public static void FitTileToCell(Transform tile, float cellSize, bool enabled, float fill)
    {
        if (!enabled || tile == null) return;

        var sr = tile.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        Vector2 spriteSize = sr.sprite.bounds.size; // world units at scale 1
        float maxDim = Mathf.Max(spriteSize.x, spriteSize.y);
        if (maxDim <= 0f) return;

        float scale = cellSize * fill / maxDim;
        tile.localScale = new Vector3(scale, scale, 1f);
    }
}
