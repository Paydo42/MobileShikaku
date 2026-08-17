using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws one colour's wire as a thick rounded line through the centres of the
/// cells it covers. <see cref="AmusBoard"/> creates one of these per pair and
/// feeds it a fresh point list whenever the wire changes.
///
/// The board can spawn these from scratch, so nothing needs to exist in the
/// project; assign a prefab with a styled LineRenderer on the board if you want
/// a custom look (dashes, a gradient, a glow material).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class AmusWire : MonoBehaviour
{
    private LineRenderer _line;
    private Vector3[] _buffer = new Vector3[0];

    /// <summary>Builds a wire object from scratch, for boards with no wire prefab.</summary>
    public static AmusWire Create(string objectName, Transform parent)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        return go.AddComponent<AmusWire>();
    }

    private LineRenderer Line
    {
        get
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            return _line;
        }
    }

    /// <summary>
    /// Applies this wire's look. <paramref name="material"/> may be null, in
    /// which case a shared generated one is used. <paramref name="roundness"/>
    /// is how many extra vertices go into each corner and cap: 0 is a hard
    /// mitred line, ~8 reads as fully rounded.
    /// </summary>
    public void Configure(Material material, Color color, float width, int roundness,
        string sortingLayer, int sortingOrder)
    {
        LineRenderer line = Line;

        // sharedMaterial, not material: colour comes from the line's vertex
        // colours, so every wire can share one material and still batch.
        line.sharedMaterial = material != null ? material : BoardArt.VertexColorMaterial;
        line.startColor = color;
        line.endColor = color;
        line.widthMultiplier = width;
        line.numCornerVertices = roundness;
        line.numCapVertices = roundness;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        if (!string.IsNullOrEmpty(sortingLayer)) line.sortingLayerName = sortingLayer;
        line.sortingOrder = sortingOrder;

        line.positionCount = 0;
    }

    /// <summary>
    /// Redraws the wire along <paramref name="points"/>. Fewer than two points
    /// means there's nothing to draw yet (the endpoint dot covers that case), so
    /// the line is hidden.
    /// </summary>
    public void SetPoints(List<Vector3> points)
    {
        LineRenderer line = Line;

        if (points == null || points.Count < 2)
        {
            line.positionCount = 0;
            return;
        }

        // Exact size: SetPositions reads the whole array, so a longer buffer
        // would trail stale points.
        if (_buffer.Length != points.Count) _buffer = new Vector3[points.Count];
        points.CopyTo(_buffer);

        line.positionCount = points.Count;
        line.SetPositions(_buffer);
    }
}
