using UnityEngine;

/// <summary>
/// Runtime-generated art shared by the boards, so a mode can draw wires and dots
/// without the project having to ship a sprite or material for them. Everything
/// here is built once on first use and reused.
/// </summary>
public static class BoardArt
{
    private const int CircleTextureSize = 128;

    private static readonly string[] ShaderCandidates =
    {
        "Sprites/Default",
        "Universal Render Pipeline/2D/Sprite-Unlit-Default",
        "Legacy Shaders/Particles/Alpha Blended Premultiply",
    };

    private static Material _vertexColorMaterial;
    private static Sprite _circleSprite;

    /// <summary>
    /// An unlit transparent material that takes its colour from vertex colours,
    /// which is what <see cref="LineRenderer"/> writes its start/end colour into.
    /// One instance is shared by every line so they still batch.
    /// </summary>
    public static Material VertexColorMaterial
    {
        get
        {
            if (_vertexColorMaterial != null) return _vertexColorMaterial;

            // Sprites/Default is in this project's always-included shaders, so
            // it survives into a build; the rest are fallbacks in case that
            // changes. Don't use ?? here — Unity's null is not C#'s.
            Shader shader = null;
            foreach (string candidate in ShaderCandidates)
            {
                shader = Shader.Find(candidate);
                if (shader != null) break;
            }

            if (shader == null)
            {
                Debug.LogError("BoardArt: no usable shader found. Assign a wire material on the board instead.");
                return null;
            }

            _vertexColorMaterial = new Material(shader) { name = "BoardArt Vertex Colour" };
            return _vertexColorMaterial;
        }
    }

    /// <summary>
    /// A white filled circle, one world unit across at scale 1, with a soft edge
    /// so it doesn't look jagged. Tint it via the SpriteRenderer's colour.
    /// </summary>
    public static Sprite CircleSprite
    {
        get
        {
            if (_circleSprite != null) return _circleSprite;

            const int size = CircleTextureSize;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BoardArt Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float radius = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    // Fade the last pixel of the radius out, so the rim is smooth.
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            // Pixels-per-unit = size makes the sprite exactly one world unit wide,
            // so callers can scale it straight to the size they want.
            _circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
            _circleSprite.name = "BoardArt Circle";
            return _circleSprite;
        }
    }
}
