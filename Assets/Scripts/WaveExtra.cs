using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for the briefed "wave extras" — interactive arena entities (a trap, a neutral hazard, a
/// player-friendly node) spawned through the ORDINARY wave pipeline: each is a MarauderSO whose prefab
/// carries one of these scripts, painted in the Wave Forge "Extras" row and built by Tools > Build Era-1
/// Extras. (Coexists with <see cref="SentryExtra"/>, the bonus beam/line-of-sight sentry set — this base
/// instead provides code-baked SDF body art + an element halo + particle bursts for richer pods/founts.)
///
/// THE GOTCHA (load-bearing): EmbersEdge.SpawnEnemy adds every spawned object to SpawnManager.alives, and
/// a wave only ends once alives is empty — removal happens only on Destroy. So every extra MUST end in
/// Destroy: subclasses self-destruct when used (set <c>consumed</c> + Destroy), and a <see cref="maxLifetime"/>
/// watchdog guarantees cleanup if it is never triggered. (We CANNOT despawn on "wave over" — that state
/// can't arrive while we still sit in alives.)
///
/// All art is generated in code and the prefabs are thin shells, mirroring the Nova / Firestorm pattern.
/// </summary>
public class WaveExtra : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Keep the extra on the rim where the wave spawned it (true), or drop it interiorDepth units " +
             "inward into the arena (false — the usual case for interior traps / nodes).")]
    public bool wallMounted = false;
    [Tooltip("Interior extras: how far inward (world units) from the spawn rim point to settle.")]
    public float interiorDepth = 5f;

    [Header("Lifetime")]
    [Tooltip("Hard cap: the extra self-destructs after this long even if never triggered, so it can never " +
             "block the wave from completing (it sits in SpawnManager.alives until destroyed).")]
    public float maxLifetime = 26f;
    [Tooltip("Seconds for the spawn pop-in / despawn shrink-out tweens.")]
    public float popTime = 0.35f;

    protected bool consumed;          // set by subclasses that destroy themselves on use (no shrink-out)
    protected Vector3 baseScale = Vector3.one;
    protected float popK = 0f;        // 0..1 spawn-scale; subclasses multiply their idle scale by this

    // ---------------------------------------------------------------- lifecycle ----------------------
    protected virtual void Start()
    {
        baseScale = transform.localScale;
        PlaceInWorld();
        StartCoroutine(PopIn());
        StartCoroutine(Lifetime());
    }

    // Painted into a mine pocket at an exact cell — keep that spot; never reposition (nothing self-
    // scatters). Wall-mounted entities only re-aim toward the room (pocket) interior.
    void PlaceInWorld()
    {
        if (wallMounted) transform.up = InwardDir(transform.position);
    }

    /// <summary>Unit vector from `from` toward the centre of the space it sits in — the active mine
    /// pocket if we're in one, else the arena bbox centre. "Inward" for a wall-mounted sentry.</summary>
    protected Vector2 InwardDir(Vector2 from)
    {
        Vector2 d = RoomCenter() - from;
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up;
    }

    // Centre of the enclosing space: the active mine pocket (entities are painted into pockets), else the arena.
    protected static Vector2 RoomCenter()
    {
        var mdm = MineDungeonManager.i;
        if (mdm != null && mdm.activePocket != null) return mdm.activePocket.WorldCenter();
        return MapManager.MapBounds().center;
    }

    /// <summary>Drop `depth` units inward from the spawn (rim) position, clamped to stay inside the map.</summary>
    protected Vector2 InteriorPointInward(float depth)
    {
        Vector2 start = transform.position;
        Vector2 dir = InwardDir(start);
        Vector2[] bnd = MapManager.GetBuildableBoundary(1.5f);
        if (bnd == null) return start + dir * depth;
        for (float d = depth; d >= 1f; d -= 1f)
        {
            Vector2 p = start + dir * d;
            if (MapManager.PointInPoly(p, bnd)) return p;
        }
        Bounds b = MapManager.MapBounds();
        return MapManager.PointInPoly(b.center, bnd) ? (Vector2)b.center : start;
    }

    IEnumerator PopIn()
    {
        if (popTime <= 0f) { popK = 1f; transform.localScale = baseScale; yield break; }
        for (float t = 0f; t < 1f; t += Time.deltaTime / popTime)
        {
            popK = EaseOutBack(Mathf.Clamp01(t));          // overshoot for a lively spawn
            transform.localScale = baseScale * popK;
            yield return null;
        }
        popK = 1f;
        transform.localScale = baseScale;
    }

    // Hard-cap watchdog — the only reliable cleanup for a pipeline extra that is never triggered.
    IEnumerator Lifetime()
    {
        if (maxLifetime <= 0f) yield break;
        yield return new WaitForSeconds(maxLifetime);
        if (!consumed) Despawn();
    }

    /// <summary>Graceful shrink-out then destroy. Subclasses that "spend" themselves should set
    /// <c>consumed = true</c> and destroy directly instead.</summary>
    public virtual void Despawn()
    {
        if (consumed) return;
        consumed = true;
        StartCoroutine(ShrinkOut());
    }

    IEnumerator ShrinkOut()
    {
        Vector3 from = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, popTime))
        {
            transform.localScale = Vector3.Lerp(from, Vector3.zero, t);
            yield return null;
        }
        Destroy(gameObject);
    }

    protected static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = 2.70158f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    // =================================================================== PALETTE =====================
    // Ember's Edge fixed palette (see the palette memory): every visual = D1 purple base + ONE element.
    public static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString(h[0] == '#' ? h : "#" + h, out var c);
        return c;
    }
    // D1 purples (dark -> bright); #43395e is also the universal sprite outline.
    public static readonly Color D1a = Hex("43395e"), D1b = Hex("564d8f"), D1c = Hex("8b80c6"), D1d = Hex("c9a8e7");
    public static readonly Color Outline = Hex("43395e");
    // Elemental ramps (dark -> bright).
    public static readonly Color R1 = Hex("510216"), R2 = Hex("70032c"), R3 = Hex("ae052f"), R4 = Hex("e4073e");
    public static readonly Color G1 = Hex("1d5514"), G2 = Hex("26701b"), G3 = Hex("3bae2a"), G4 = Hex("4ee437");
    public static readonly Color B1 = Hex("041748"), B2 = Hex("062982"), B3 = Hex("093cbc"), B4 = Hex("0d51ff");
    // White/light ramp — the fourth element (the neutral/restorative pole). Tuned around the project's
    // StandardWhite (a pale mint, #ABE0D3) so it reads as "pure energy" rather than flat grey.
    public static readonly Color W1 = Hex("3f5b55"), W2 = Hex("6a958b"), W3 = Hex("abe0d3"), W4 = Hex("e8fbf5");

    // =================================================================== SPRITE BAKER ================
    /// <summary>
    /// Bake a square sprite from a per-pixel shader function. <paramref name="shade"/> receives normalized
    /// coordinates in [-1,1] (centre = 0,0) and returns the pixel's RGBA. Helper SDFs (PolyRadius, Ring…)
    /// are provided below for use inside the lambda. Returns a Sprite sized to <paramref name="worldUnits"/>.
    /// </summary>
    public static Sprite BakeSprite(int size, float worldUnits, System.Func<float, float, Color> shade)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float inv = 2f / (size - 1);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv - 1f;
                float ny = y * inv - 1f;
                px[y * size + x] = shade(nx, ny);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / Mathf.Max(0.01f, worldUnits));
    }

    /// <summary>Anti-aliased fill of a signed-distance value (negative = inside). aa ≈ 2/size.</summary>
    public static float Fill(float signedDist, float aa) => Mathf.Clamp01(0.5f - signedDist / Mathf.Max(1e-4f, 2f * aa));

    /// <summary>Circum-radius of a regular N-gon (unit max radius) at angle <paramref name="ang"/> (radians).</summary>
    public static float PolyRadius(float ang, int sides, float rot)
    {
        float seg = Mathf.PI * 2f / sides;
        float a = Mathf.Repeat(ang - rot, seg) - seg * 0.5f;
        return Mathf.Cos(seg * 0.5f) / Mathf.Max(1e-4f, Mathf.Cos(a));
    }

    /// <summary>Layer colour <paramref name="over"/> on top of <paramref name="under"/> by alpha.</summary>
    public static Color Over(Color under, Color over)
    {
        float a = over.a;
        return new Color(
            Mathf.Lerp(under.r, over.r, a),
            Mathf.Lerp(under.g, over.g, a),
            Mathf.Lerp(under.b, over.b, a),
            under.a + a * (1f - under.a));
    }

    public static Color A(Color c, float a) { c.a = a; return c; }

    // =================================================================== GLOW HALO ===================
    // A reliable, light-independent ground aura: a soft alpha-blended disc (Sprite-Unlit-Default) parented
    // under the body on the Buildings layer, pulsed in code. Avoids URP Light2D sorting-layer targeting
    // pitfalls while still reading as a glow on the dark arena.
    static Sprite _haloSprite;
    public static Sprite HaloSprite
    {
        get
        {
            if (_haloSprite != null) return _haloSprite;
            _haloSprite = BakeSprite(96, 1f, (nx, ny) =>
            {
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.SmoothStep(1f, 0f, r);
                return new Color(1f, 1f, 1f, a * a);
            });
            return _haloSprite;
        }
    }

    protected SpriteRenderer halo;
    protected Color haloTint = Color.white;
    protected float haloBase = 1.6f;

    protected void MakeHalo(Color tint, float worldSize)
    {
        haloTint = tint; haloBase = worldSize;
        var g = new GameObject("Halo");
        g.transform.SetParent(transform, false);
        g.transform.localScale = Vector3.one * worldSize;
        halo = g.AddComponent<SpriteRenderer>();
        halo.sprite = HaloSprite;
        var stock = Resources.Load<Material>("Sprite-Unlit-Default");
        if (stock != null) halo.sharedMaterial = stock;
        halo.sortingLayerName = "Buildings";
        halo.sortingOrder = -1;
        halo.color = A(tint, 0.5f);
    }

    /// <summary>Drive the halo's glow (0 = off, 1 = bright, &gt;1 = flash). Cheap pulse via vertex colour + scale.</summary>
    protected void SetGlow(float intensity)
    {
        if (halo == null) return;
        halo.color = A(haloTint, Mathf.Clamp01(intensity * 0.4f));
        halo.transform.localScale = Vector3.one * (haloBase * (1f + 0.22f * Mathf.Clamp01(intensity)));
    }

    // =================================================================== PARTICLE BURST ==============
    static Texture2D _softDot;
    public static Texture2D SoftDot
    {
        get
        {
            if (_softDot != null) return _softDot;
            const int s = 64;
            _softDot = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float nx = (x + 0.5f) / s * 2f - 1f, ny = (y + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.SmoothStep(1f, 0f, r);          // soft round falloff
                    a *= a;
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            _softDot.SetPixels(px); _softDot.Apply();
            return _softDot;
        }
    }

    /// <summary>
    /// Build a world-space, manual-emit particle system on a child of <paramref name="host"/>. Grains fade and
    /// shrink over life; colour is set per-emit. Uses the stock alpha-blended Sprite-Unlit-Default with our
    /// soft-dot texture (the palette-approved path for fading dots). Drive it with <see cref="EmitBurst"/>.
    /// </summary>
    public static ParticleSystem MakeBurst(Transform host, string sortingLayer = "Power Ups", int sortingOrder = 5)
    {
        var go = new GameObject("burst");
        go.transform.SetParent(host, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var m = ps.main;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.playOnAwake = false;
        m.maxParticles = 1024;
        m.startSpeed = 0f; m.startSize = 0.2f; m.startLifetime = 0.7f;

        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));

        var rnd = go.GetComponent<ParticleSystemRenderer>();
        // Prefer the project's stock unlit sprite material (palette-approved path for fading dots); only
        // fall back to the built-in sprite shader if it isn't found (and avoid a throwaway Shader.Find).
        var stock = Resources.Load<Material>("Sprite-Unlit-Default");
        rnd.material = stock != null ? new Material(stock) { mainTexture = SoftDot }
                                     : new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot };
        rnd.sortingLayerName = sortingLayer;
        rnd.sortingOrder = sortingOrder;
        ps.Play();
        return ps;
    }

    // One reusable burst system per extra (avoids leaking a ParticleSystem child on every chip/pulse).
    ParticleSystem _burst;
    protected ParticleSystem Burst()
    {
        if (_burst == null) _burst = MakeBurst(transform, "Power Ups", 6);
        return _burst;
    }

    /// <summary>Emit <paramref name="count"/> grains radially from <paramref name="pos"/>, coloured from a ramp.</summary>
    public static void EmitBurst(ParticleSystem ps, Vector3 pos, int count, Color[] ramp,
        float speedMin, float speedMax, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
        float spreadDeg = 360f, float baseDeg = 0f)
    {
        if (ps == null) return;
        for (int i = 0; i < count; i++)
        {
            float ang = (baseDeg + Random.Range(-spreadDeg * 0.5f, spreadDeg * 0.5f)) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + (Vector3)(dir * Random.Range(0f, 0.06f)),
                velocity = dir * Random.Range(speedMin, speedMax),
                startLifetime = Random.Range(lifeMin, lifeMax),
                startSize = Random.Range(sizeMin, sizeMax),
                startColor = ramp[Random.Range(0, ramp.Length)],
            };
            ps.Emit(ep, 1);
        }
    }
}
