using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Amus board's juice, kept out of <see cref="AmusBoard"/> so the rules
/// code stays readable:
///
///   * Erase: a stretch of wire that gets cut off doesn't just vanish. It
///     flashes, shrinks away, and each freed cell lets out a small puff,
///     rippling out from where the wire was cut.
///   * Connect: when a pair gets joined, both its dots pop, the wire briefly
///     swells and brightens, and a little confetti bomb goes off at each end.
///   * Win: a ball of electricity runs through every wire: it leaves the dot
///     the wire was started from, travels inside the cable with a short tail,
///     and disappears into the far dot, which pops (with a rising ding). Once
///     every ball is home all the dots pop together, then the win is reported.
///
/// The board adds this component itself if it's missing; add it in the
/// Inspector to tune the numbers. Everything is built from code, so no prefab,
/// sprite or material is needed.
///
/// Draw order, relative to the board's wire order W: erase ghost W-1 (under
/// the wires, over the tiles); ball halo, tail and erase puffs W+1, ball bloom
/// W+2, ball core W+3, all above the wires but under the dots (W+4) and
/// letters (W+5), so the ball reads as running inside the cable between them;
/// confetti W+6, over everything.
/// </summary>
[DisallowMultipleComponent]
public class AmusFx : MonoBehaviour
{
    [Header("Erase")]
    [SerializeField] private bool playErase = true;
    [Tooltip("Seconds for a removed stretch of wire to shrink away.")]
    [SerializeField] private float eraseDuration = 0.22f;
    [Range(0.1f, 1.5f)]
    [Tooltip("Peak size of the puff on each freed cell, as a fraction of a cell.")]
    [SerializeField] private float erasePuffSize = 0.7f;
    [Tooltip("Delay between neighbouring puffs, so a long cut ripples out from where it was cut.")]
    [SerializeField] private float eraseStagger = 0.03f;
    [Tooltip("Optional extra sound for an erase. The board already plays its backtrack step.")]
    [SerializeField] private AudioClip eraseClip;

    [Header("Connect")]
    [SerializeField] private bool playConnect = true;
    [Tooltip("Seconds for the connect pop.")]
    [SerializeField] private float connectDuration = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("How much the two dots grow at the peak of the pop (0.3 = 30% bigger).")]
    [SerializeField] private float connectDotPop = 0.3f;
    [Range(0f, 1f)]
    [Tooltip("How much the wire swells at the peak of the pop.")]
    [SerializeField] private float connectWireSwell = 0.25f;
    [Tooltip("Optional sound for a connection. The board already plays its step sound.")]
    [SerializeField] private AudioClip connectClip;

    [Header("Connect: confetti burst")]
    [SerializeField] private bool playConfetti = true;
    [Range(0, 40)]
    [Tooltip("Balls thrown out at each end when a pair connects.")]
    [SerializeField] private int confettiCount = 14;
    [Tooltip("Top launch speed, in cells per second. Each ball gets 45-100% of it.")]
    [SerializeField] private float confettiSpeed = 6f;
    [Tooltip("How hard the balls brake after the blast. Higher = a snappier pop that stays close.")]
    [SerializeField] private float confettiDrag = 6f;
    [Tooltip("Downward pull, in cells per second squared, so the balls arc like falling confetti.")]
    [SerializeField] private float confettiGravity = 4f;
    [Tooltip("Longest a ball lives, in seconds.")]
    [SerializeField] private float confettiLifetime = 0.55f;
    [Range(0.02f, 0.5f)]
    [Tooltip("Ball diameter, as a fraction of a cell.")]
    [SerializeField] private float confettiSize = 0.12f;
    [Tooltip("Optional extra colours mixed in with the wire's own shades, for a rainbow burst.")]
    [SerializeField] private Color[] confettiExtraColors = new Color[0];

    [Header("Win: a ball of electricity through each wire")]
    [SerializeField] private bool playWin = true;
    [Tooltip("How fast the ball travels, in cells per second.")]
    [SerializeField] private float ballSpeed = 7f;
    [Tooltip("Most seconds a ball may take along the longest wire; long wires speed it up to fit.")]
    [SerializeField] private float maxTravelTime = 1.3f;
    [Tooltip("Colour of the ball's hot centre. Its halo and tail take the wire's colour.")]
    [SerializeField] private Color ballColor = Color.white;
    [Range(0.05f, 1f)]
    [Tooltip("Diameter of the ball's solid centre, as a fraction of a cell. Around the wire's width reads as inside the cable.")]
    [SerializeField] private float ballSize = 0.24f;
    [Range(0.2f, 2f)]
    [Tooltip("Diameter of the coloured halo around the ball, as a fraction of a cell.")]
    [SerializeField] private float haloSize = 0.9f;
    [Range(0, 12)]
    [Tooltip("Afterimages trailing the ball. 0 = no tail.")]
    [SerializeField] private int tailLength = 6;
    [Tooltip("Gap between afterimages, as a fraction of a cell.")]
    [SerializeField] private float tailSpacing = 0.08f;
    [Tooltip("Seconds the board is held after the last ball arrives, before the win panel.")]
    [SerializeField] private float holdAfter = 0.45f;
    [Tooltip("Optional: played as each ball arrives. Empty = the path step sound, pitched up ball by ball.")]
    [SerializeField] private AudioClip arriveClip;
    [Tooltip("Optional: played when the last ball arrives.")]
    [SerializeField] private AudioClip completeClip;

    private const float FinalPopDuration = 0.35f;
    private const float ConfettiFlashDuration = 0.15f;

    private Transform _parent;
    private Material _material;
    private float _cell;
    private float _wireWidth;
    private int _roundness;
    private string _sortingLayer;
    private int _wireOrder;

    private readonly List<AmusWire> _linePool = new();
    private readonly List<SpriteRenderer> _spritePool = new();
    private readonly Dictionary<Transform, Vector3> _restScale = new();

    /// <summary>Whether the board should wait for <see cref="PlayWin"/> before reporting a win.</summary>
    public bool PlaysWin => playWin;

    /// <summary>Takes the board's look, so the effects match its wires. Call once the board is built.</summary>
    public void Init(Transform parent, Material material, float cellSize, float wireWidth, int roundness,
        string sortingLayer, int wireSortingOrder)
    {
        _parent = parent;
        _material = material;
        _cell = cellSize;
        _wireWidth = wireWidth;
        _roundness = roundness;
        _sortingLayer = sortingLayer;
        _wireOrder = wireSortingOrder;
    }

    #region Erase

    /// <summary>
    /// Plays the erase for a stretch of wire that is being removed.
    /// <paramref name="points"/> run from the cell the wire is cut back to
    /// (which stays on the wire) out to the old tail. They're copied, so the
    /// caller can reuse its list.
    /// </summary>
    public void PlayErase(List<Vector3> points, Color color)
    {
        if (!playErase || _parent == null || points == null || points.Count < 2 || !isActiveAndEnabled) return;

        StartCoroutine(EraseRoutine(new List<Vector3>(points), color));

        if (eraseClip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfx(eraseClip, 1f, Random.Range(0.95f, 1.05f));
    }

    private IEnumerator EraseRoutine(List<Vector3> points, Color color)
    {
        // Under the live wires, so the head the player is still dragging stays crisp.
        AmusWire ghost = RentLine(color, _wireWidth, _roundness, _wireOrder - 1);
        ghost.SetPoints(points);

        // One puff per freed cell; points[0] is where the wire was cut back to.
        int puffCount = points.Count - 1;
        var puffs = new SpriteRenderer[puffCount];
        Color puffColor = Color.Lerp(color, Color.white, 0.3f);
        for (int i = 0; i < puffCount; i++)
        {
            puffs[i] = RentSprite(BoardArt.GlowSprite, _wireOrder + 1);
            puffs[i].transform.position = points[i + 1];
            puffs[i].transform.localScale = Vector3.zero;
        }

        float total = eraseDuration + eraseStagger * (puffCount - 1);
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            // The cut-off piece flashes toward white, pops a little, then
            // shrinks to nothing as it fades.
            float k = Mathf.Clamp01(t / eraseDuration);
            Color ghostColor = Color.Lerp(Color.Lerp(color, Color.white, 0.5f), color, k);
            ghostColor.a = 1f - k * k;
            ghost.SetColor(ghostColor);
            ghost.SetWidth(_wireWidth * (1f - k) * (1f + 0.3f * (1f - k)));

            for (int i = 0; i < puffCount; i++)
            {
                float pk = (t - eraseStagger * i) / eraseDuration;
                if (pk <= 0f || pk >= 1f)
                {
                    puffs[i].color = Color.clear;
                    continue;
                }
                puffs[i].transform.localScale = Vector3.one * (_cell * erasePuffSize * (0.3f + 0.7f * EaseOut(pk)));
                puffs[i].color = WithAlpha(puffColor, 0.7f * (1f - pk));
            }

            yield return null;
        }

        Release(ghost);
        foreach (SpriteRenderer puff in puffs) Release(puff);
    }

    #endregion

    #region Connect

    /// <summary>
    /// Pops a pair that was just joined: both dots and the wire itself, plus a
    /// confetti burst at each end of the wire (<paramref name="start"/> and
    /// <paramref name="end"/>). <paramref name="wire"/> is the board's own view
    /// of that wire; it's put back to <paramref name="color"/> and the normal
    /// width afterwards. The dots may be null when the board draws none.
    /// </summary>
    public void PlayConnect(AmusWire wire, Color color, Transform dotA, Transform dotB, Vector3 start, Vector3 end)
    {
        if (!playConnect || _parent == null || !isActiveAndEnabled) return;

        Punch(dotA, connectDotPop, connectDuration);
        Punch(dotB, connectDotPop, connectDuration);
        if (wire != null) StartCoroutine(SwellRoutine(wire, color));
        if (playConfetti && confettiCount > 0)
        {
            StartCoroutine(ConfettiRoutine(start, color));
            StartCoroutine(ConfettiRoutine(end, color));
        }

        Haptics.Tick(35);
        if (connectClip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfx(connectClip);
    }

    private IEnumerator SwellRoutine(AmusWire wire, Color color)
    {
        for (float t = 0f; t < connectDuration; t += Time.deltaTime)
        {
            if (wire == null) yield break;
            float bump = Mathf.Sin(t / connectDuration * Mathf.PI); // 0 -> 1 -> 0
            wire.SetWidth(_wireWidth * (1f + connectWireSwell * bump));
            wire.SetColor(Color.Lerp(color, Color.white, 0.35f * bump));
            yield return null;
        }

        if (wire == null) yield break;
        wire.SetWidth(_wireWidth);
        wire.SetColor(color);
    }

    // A small bomb going off: a quick flash, then balls flung out in every
    // direction that brake hard, drop a little, and shrink away.
    private IEnumerator ConfettiRoutine(Vector3 origin, Color color)
    {
        int count = confettiCount;
        var balls = new SpriteRenderer[count];
        var position = new Vector3[count];
        var velocity = new Vector3[count];
        var life = new float[count];
        var size = new float[count];
        var tint = new Color[count];

        SpriteRenderer flash = RentSprite(BoardArt.GlowSprite, _wireOrder + 6);
        Color flashColor = Color.Lerp(color, Color.white, 0.5f);

        for (int i = 0; i < count; i++)
        {
            // Evenly spread angles with a little jitter read as a blast, not a clump.
            float angle = (i + Random.Range(-0.4f, 0.4f)) / count * Mathf.PI * 2f;
            float speed = confettiSpeed * Random.Range(0.45f, 1f) * _cell;
            velocity[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
            position[i] = origin;
            life[i] = confettiLifetime * Random.Range(0.7f, 1f);
            size[i] = confettiSize * Random.Range(0.6f, 1.3f);
            tint[i] = ConfettiColor(color);
            balls[i] = RentSprite(BoardArt.CircleSprite, _wireOrder + 6);
        }

        float gravity = confettiGravity * _cell;
        for (float t = 0f; t < confettiLifetime; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            float damping = Mathf.Exp(-confettiDrag * dt);

            float fk = t / ConfettiFlashDuration;
            if (fk < 1f) Place(flash, origin, Mathf.Lerp(0.4f, 1.4f, EaseOut(fk)), WithAlpha(flashColor, 0.8f * (1f - fk)));
            else flash.color = Color.clear;

            for (int i = 0; i < count; i++)
            {
                if (t >= life[i])
                {
                    balls[i].color = Color.clear;
                    continue;
                }

                velocity[i] *= damping;
                velocity[i].y -= gravity * dt;
                position[i] += velocity[i] * dt;

                // Full strength for most of its life, then shrink and fade out.
                float k = t / life[i];
                float fade = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                Place(balls[i], position[i], size[i] * (1f - 0.5f * k), WithAlpha(tint[i], fade));
            }

            yield return null;
        }

        Release(flash);
        foreach (SpriteRenderer ball in balls) Release(ball);
    }

    // Mostly shades of the wire's own colour, so the burst belongs to it; the
    // optional extra colours turn it into a rainbow. No white: it would vanish
    // against the light tiles.
    private Color ConfettiColor(Color pair)
    {
        if (confettiExtraColors != null && confettiExtraColors.Length > 0 && Random.value < 0.4f)
            return confettiExtraColors[Random.Range(0, confettiExtraColors.Length)];

        float shade = Random.Range(-0.25f, 0.35f);
        return shade < 0f
            ? Color.Lerp(pair, Color.black, -shade)
            : Color.Lerp(pair, Color.white, shade);
    }

    #endregion

    #region Win

    // One ball of electricity: a coloured halo, a white bloom, a solid core
    // and a fading tail of afterimages.
    private struct Ball
    {
        public SpriteRenderer halo, bloom, core;
        public SpriteRenderer[] tail;
    }

    /// <summary>
    /// Sends a ball of electricity through every wire, then pops all the dots.
    /// Yield on it, then report the win. Each of <paramref name="paths"/> is
    /// one wire's cell centres, from the endpoint it was started at to the far
    /// one; <paramref name="startDots"/> and <paramref name="endDots"/> may hold
    /// nulls when the board draws no dots.
    /// </summary>
    public IEnumerator PlayWin(List<Vector3>[] paths, Color[] colors, Transform[] startDots, Transform[] endDots)
    {
        if (_parent == null) yield break;

        int n = paths.Length;
        var balls = new Ball[n];
        var lengths = new float[n];
        var arrived = new bool[n];
        float longest = 0f;

        for (int i = 0; i < n; i++)
        {
            // Consecutive cells are always one cell apart, so length is just steps.
            lengths[i] = Mathf.Max(0, paths[i].Count - 1) * _cell;
            longest = Mathf.Max(longest, lengths[i]);
            balls[i] = RentBall();
            Punch(startDots[i], 0.2f, 0.2f);
        }

        float speed = Mathf.Max(ballSpeed * _cell, longest / Mathf.Max(0.1f, maxTravelTime));
        float travelled = 0f;
        int arrivedCount = 0;

        Haptics.Tick(25);

        // Every ball sets off at once, so the ones on shorter wires land
        // first and the arrivals cascade.
        while (arrivedCount < n)
        {
            travelled += speed * Time.deltaTime;

            for (int i = 0; i < n; i++)
            {
                if (arrived[i]) continue;

                float reach = Mathf.Min(travelled, lengths[i]);
                PlaceBall(balls[i], paths[i], reach, colors[i]);

                if (reach < lengths[i]) continue;

                // Into the far dot it goes.
                arrived[i] = true;
                arrivedCount++;
                ReleaseBall(balls[i]);
                Punch(endDots[i], 0.35f, 0.28f);
                Haptics.Tick(20);
                PlayArrive(arrivedCount, n);
            }

            yield return null;
        }

        // Every wire is live: all the dots pop together, then hold.
        Haptics.Tick(60);
        if (completeClip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySfx(completeClip);
        for (int i = 0; i < n; i++)
        {
            Punch(startDots[i], 0.3f, FinalPopDuration);
            Punch(endDots[i], 0.3f, FinalPopDuration);
        }

        yield return new WaitForSeconds(FinalPopDuration + holdAfter);
    }

    private Ball RentBall()
    {
        var ball = new Ball
        {
            halo = RentSprite(BoardArt.GlowSprite, _wireOrder + 1),
            bloom = RentSprite(BoardArt.GlowSprite, _wireOrder + 2),
            core = RentSprite(BoardArt.CircleSprite, _wireOrder + 3),
            tail = new SpriteRenderer[tailLength],
        };
        for (int t = 0; t < tailLength; t++) ball.tail[t] = RentSprite(BoardArt.GlowSprite, _wireOrder + 1);
        return ball;
    }

    private void ReleaseBall(Ball ball)
    {
        Release(ball.halo);
        Release(ball.bloom);
        Release(ball.core);
        foreach (SpriteRenderer afterimage in ball.tail) Release(afterimage);
    }

    // Puts a ball 'reach' world units along its wire. It flickers a little in
    // size so it reads as live current rather than a bead.
    private void PlaceBall(Ball ball, List<Vector3> path, float reach, Color color)
    {
        Vector3 head = PointAt(path, reach);
        float flicker = Random.Range(0.9f, 1.1f);
        Color light = Color.Lerp(color, Color.white, 0.35f);

        Place(ball.halo, head, haloSize * flicker, WithAlpha(light, 0.85f));
        Place(ball.bloom, head, ballSize * 2.2f * flicker, ballColor);
        Place(ball.core, head, ballSize, ballColor);

        for (int t = 0; t < ball.tail.Length; t++)
        {
            float back = reach - (t + 1) * tailSpacing * _cell;
            if (back < 0f)
            {
                ball.tail[t].color = Color.clear;
                continue;
            }
            float fade = 1f - (t + 1f) / (ball.tail.Length + 1f); // nearest afterimage strongest
            Place(ball.tail[t], PointAt(path, back), haloSize * 0.6f * fade + ballSize * 0.5f, WithAlpha(light, 0.6f * fade));
        }
    }

    private void Place(SpriteRenderer sprite, Vector3 position, float sizeInCells, Color color)
    {
        sprite.transform.position = position;
        sprite.transform.localScale = Vector3.one * (_cell * sizeInCells);
        sprite.color = color;
    }

    // The point 'distance' world units along a wire's cell path.
    private Vector3 PointAt(List<Vector3> path, float distance)
    {
        if (path.Count < 2) return path[0];
        float steps = Mathf.Clamp(distance / _cell, 0f, path.Count - 1);
        int s = Mathf.Min(Mathf.FloorToInt(steps), path.Count - 2);
        return Vector3.Lerp(path[s], path[s + 1], steps - s);
    }

    // Each arrival rings a little higher than the last, so the cascade climbs.
    private void PlayArrive(int arrivedCount, int total)
    {
        if (SoundManager.Instance == null) return;

        float pitch = 1f + 0.6f * (arrivedCount - 1) / Mathf.Max(1, total - 1);
        if (arriveClip != null) SoundManager.Instance.PlaySfx(arriveClip, 1f, pitch);
        else SoundManager.Instance.PlayPathStep(pitch + 0.2f);
    }

    #endregion

    #region Helpers

    // Scales a dot up and back down once. Overlapping punches are fine: each
    // ends by restoring the dot's resting size.
    private void Punch(Transform target, float amount, float duration)
    {
        if (target == null || !isActiveAndEnabled) return;
        if (!_restScale.ContainsKey(target)) _restScale[target] = target.localScale;
        StartCoroutine(PunchRoutine(target, _restScale[target], amount, duration));
    }

    private static IEnumerator PunchRoutine(Transform target, Vector3 rest, float amount, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (target == null) yield break;
            target.localScale = rest * (1f + amount * Mathf.Sin(t / duration * Mathf.PI));
            yield return null;
        }
        if (target != null) target.localScale = rest;
    }

    private AmusWire RentLine(Color color, float width, int roundness, int sortingOrder)
    {
        AmusWire line = null;
        foreach (AmusWire pooled in _linePool)
        {
            if (pooled.gameObject.activeSelf) continue;
            line = pooled;
            break;
        }
        if (line == null)
        {
            line = AmusWire.Create("Fx Line", _parent);
            _linePool.Add(line);
        }

        line.gameObject.SetActive(true);
        line.Configure(_material, color, width, roundness, _sortingLayer, sortingOrder);
        return line;
    }

    private SpriteRenderer RentSprite(Sprite shape, int sortingOrder)
    {
        SpriteRenderer sprite = null;
        foreach (SpriteRenderer pooled in _spritePool)
        {
            if (pooled.gameObject.activeSelf) continue;
            sprite = pooled;
            break;
        }
        if (sprite == null)
        {
            var go = new GameObject("Fx Sprite");
            go.transform.SetParent(_parent, false);
            sprite = go.AddComponent<SpriteRenderer>();
            if (!string.IsNullOrEmpty(_sortingLayer)) sprite.sortingLayerName = _sortingLayer;
            _spritePool.Add(sprite);
        }

        sprite.sprite = shape;
        sprite.sortingOrder = sortingOrder;
        sprite.color = Color.clear;
        sprite.gameObject.SetActive(true);
        return sprite;
    }

    private static void Release(Component pooled) => pooled.gameObject.SetActive(false);

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    #endregion
}
