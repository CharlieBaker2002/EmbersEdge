using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for the era-1 "sentry" wave extras — enemy-side interactives / traps authored in the Wave Forge
/// and spawned through the ordinary wave pipeline (each is a MarauderSO whose prefab carries one of these
/// scripts). They are NOT Units: no LifeScript, no ActionScript — just a kinematic body + a trigger
/// collider so SpawnEnemy's GetComponentInChildren&lt;Collider2D&gt;()/OnTriggerExit2D call is safe.
///
/// (Formerly named WaveExtra; renamed to SentryExtra so it coexists with the richer code-baked-art
/// <see cref="WaveExtra"/> base used by the briefed pods/caches/founts. This family is the bonus
/// beam/line-of-sight sentry set: AirDropBeacon, the WallSentry trio, and LodestoneSnare.)
///
/// IMPORTANT: EmbersEdge.SpawnEnemy adds every spawned object to SpawnManager.alives, and a wave only
/// ends once alives is empty. A trap that never died would hang the wave forever, so every extra MUST
/// self-destruct after it fires (Consume), and a lifetime watchdog guarantees cleanup even if it is
/// never triggered.
///
/// Visuals follow the palette: a dim warm-grey dormant body, an amber "searching" state and a ramp-red
/// "alert/locked" state, driven through the glow shader's `thecolor` HDR property. Beams/reticles are
/// built in code (LineRenderers + procedural sprites) so no new art assets are needed.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public abstract class SentryExtra : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Wall sentries stay on the rim where the wave spawned them and aim inward. Interior traps " +
             "drop `interiorDepth` units inward from their authored rim column instead.")]
    public bool wallMounted = false;
    [Tooltip("Interior traps only: how far inward (world units) from the spawn rim point to settle.")]
    public float interiorDepth = 5f;

    [Header("Lifetime")]
    [Tooltip("Hard cap: the extra self-destructs after this long even if never triggered, so it can " +
             "never block the wave from completing.")]
    public float maxLifetime = 28f;

    protected SpriteRenderer body;     // base body visual (assigned by the builder)
    protected Vector3 baseScale = Vector3.one; // authored scale; pulse animations multiply this
    protected bool consumed;           // set once it fires / starts dying
    protected const string TAG = "Enemies";

    // Palette — base + elemental ramp (Status.cs ramp red = 255,0,60). Emissions are greyscale-driven
    // through `thecolor`, so these tints double as the glow colour.
    protected static readonly Color Dormant = new Color(0.55f, 0.42f, 0.33f, 1f); // warm grey, inert
    protected static readonly Color Search  = new Color(1f, 0.72f, 0.30f, 1f);    // amber, scanning
    protected static readonly Color Alert   = new Color(1f, 0.12f, 0.26f, 1f);    // ramp red, locked
    static readonly int TheColor = Shader.PropertyToID("thecolor");

    // ------------------------------------------------------------------------------------------
    protected virtual void Start()
    {
        body = GetComponentInChildren<SpriteRenderer>();
        baseScale = transform.localScale;
        PlaceInWorld();
        SetBody(Dormant, 1f);
        StartCoroutine(Lifetime());
        OnReady();
    }

    /// <summary>Subclass hook: begin the trap's behaviour (usually a coroutine that ends in Consume()).</summary>
    protected abstract void OnReady();

    // Painted into a mine pocket at an exact cell — keep that spot; never reposition (nothing self-
    // scatters). Wall-mounted sentries only re-aim toward the room (pocket) interior.
    void PlaceInWorld()
    {
        if (wallMounted) transform.up = InwardDir(transform.position);
    }

    IEnumerator Lifetime()
    {
        yield return new WaitForSeconds(maxLifetime);
        if (!consumed) Consume(0.6f);
    }

    // ---- world helpers ----------------------------------------------------------------------
    protected Vector2 PlayerPos => GS.CS() != null ? (Vector2)GS.CS().position : (Vector2)transform.position;
    protected float PlayerSpeed => GS.AS != null && GS.AS.rb != null ? GS.AS.rb.linearVelocity.magnitude : 0f;
    protected float DistToPlayer => Vector2.Distance(PlayerPos, transform.position);

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

    /// <summary>Drop `depth` units inward from the current (rim) position, clamped inside the map.</summary>
    protected Vector2 InteriorPointInward(float depth)
    {
        Vector2 start = transform.position;
        Vector2 dir = InwardDir(start);
        Vector2[] bnd = MapManager.GetBuildableBoundary(1.5f);
        if (bnd == null) return start + dir * depth;
        // walk inward from the deepest candidate back toward the rim until we land inside the map
        for (float d = depth; d >= 1f; d -= 1f)
        {
            Vector2 p = start + dir * d;
            if (MapManager.PointInPoly(p, bnd)) return p;
        }
        Bounds b = MapManager.MapBounds();
        return MapManager.PointInPoly(b.center, bnd) ? (Vector2)b.center : start;
    }

    // ---- visuals ----------------------------------------------------------------------------
    /// <summary>Tint the body and push a matching HDR colour into the glow shader (`thecolor`).</summary>
    protected void SetBody(Color tint, float emissive)
    {
        if (body == null) return;
        body.color = tint;
        Material m = body.material; // per-instance copy
        if (m != null && m.HasProperty(TheColor))
            m.SetColor(TheColor, new Color(tint.r * emissive, tint.g * emissive, tint.b * emissive, 1f));
    }

    /// <summary>A child sprite (reticle / ring / package), parented to this trap, on the FX layer.</summary>
    protected SpriteRenderer MakeSprite(string name, Sprite spr, Color col, float worldSize, int order = 6, bool worldChild = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, worldPositionStays: false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.color = col;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = order;
        go.transform.localScale = Vector3.one * worldSize;
        return sr;
    }

    /// <summary>A 2-point world-space beam built in code; colour set by the caller each frame.</summary>
    protected LineRenderer MakeBeam(float width, Color col, int order = 5)
    {
        var go = new GameObject("Beam");
        go.transform.SetParent(transform, worldPositionStays: false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = col;
        lr.sortingLayerName = "Power Ups";
        lr.sortingOrder = order;
        return lr;
    }

    protected static void SetBeamColor(LineRenderer lr, Color c)
    {
        if (lr != null) { lr.startColor = c; lr.endColor = c; }
    }

    // ---- death --------------------------------------------------------------------------------
    /// <summary>Fire-once teardown: stop behaviour, fade out, then Destroy so the wave can complete.</summary>
    protected void Consume(float fade = 0.4f)
    {
        if (consumed) return;
        consumed = true;
        StopAllCoroutines();
        StartCoroutine(FadeAndDie(fade));
    }

    IEnumerator FadeAndDie(float t)
    {
        var srs = GetComponentsInChildren<SpriteRenderer>();
        var lrs = GetComponentsInChildren<LineRenderer>();
        var a0 = new List<float>();
        foreach (var s in srs) a0.Add(s.color.a);
        for (float f = 0f; f < 1f; f += Time.deltaTime / Mathf.Max(0.01f, t))
        {
            float k = 1f - f;
            for (int i = 0; i < srs.Length; i++) { var c = srs[i].color; c.a = a0[i] * k; srs[i].color = c; }
            foreach (var lr in lrs)
            {
                Color sc = lr.startColor; sc.a *= 1f - Time.deltaTime / Mathf.Max(0.01f, t);
                lr.startColor = lr.endColor = sc;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    // ---- procedural sprites (built once, shared) ---------------------------------------------
    static Sprite _dot, _ring, _reticle;

    /// <summary>Soft radial dot, 1 world unit, white (tint via SpriteRenderer.color).</summary>
    protected static Sprite Dot()
    {
        if (_dot != null) return _dot;
        _dot = Radial(64, (d) => Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f));
        return _dot;
    }

    /// <summary>Thin glowing ring/annulus.</summary>
    protected static Sprite Ring()
    {
        if (_ring != null) return _ring;
        _ring = Radial(64, (d) => Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.16f));
        return _ring;
    }

    /// <summary>Targeting reticle: a ring with a faint filled core, for the drop beacon.</summary>
    protected static Sprite Reticle()
    {
        if (_reticle != null) return _reticle;
        _reticle = Radial(72, (d) =>
            Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.13f), 0.22f * Mathf.Clamp01(0.45f - d)));
        return _reticle;
    }

    // alpha(d) maps normalised radius [0..1] -> alpha; everything white so colour comes from tint.
    static Sprite Radial(int s, System.Func<float, float> alpha)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
        float r = s * 0.5f;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / r;
                px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(d)));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s); // PPU = s => 1 world unit
    }
}
