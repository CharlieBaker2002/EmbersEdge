using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The wall projected by the Force Field building. One LifeScript covers the whole span — when any
// part of it falls, all of it falls. Geometry is a gentle bezier between two endpoints (bowing away
// from the tower); an EdgeCollider2D plus ActionScript.building makes it block and be attackable.
//
// Visuals: a custom CAPSULE ribbon mesh running the ForceField2D screen-refraction shader (the same
// trick as the map-boundary waterfall). uv.x = world arc-length along the wall, uv.y = 0..1 across
// the thickness, and per-vertex normals carry the in-plane outward direction so the shader can bend
// the scene behind it. _Intensity tracks stored charge: the field is translucent when nearly drained
// and brighter (never blinding) when full, on a single constant hue; enemy contacts paint white
// pulses where the wall is struck.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class EnergyWall : MonoBehaviour, IOnCollide
{
    public MeshFilter mf;
    public MeshRenderer mr;
    public EdgeCollider2D col;
    public LifeScript ls;
    [Tooltip("On-screen thickness of the wall (its 3D 'height'), world units.")]
    [SerializeField] private float thickness = 0.8f;
    [SerializeField] private int segments = 24;
    [Tooltip("Must be ABOVE the renderer's Camera-Sorting-Layer bound (Projectiles) to read the scene copy.")]
    [SerializeField] private string sortingLayer = "Power Ups";
    [SerializeField] private int sortingOrder = 3;

    // Constant energy hue (no red). Health drives how PRESENT the field reads — faint & translucent
    // when low, bright & solid when full — via _Intensity, not a hue shift.
    static readonly Color tintBright = new(0.78f, 1f, 0.95f);
    static readonly Color tintDeep   = new(0.40f, 0.62f, 0.60f);

    private Vector2 a, b;
    private float arcLen = 2.4f;
    private bool weaving;
    private float flickerT;
    private Mesh mesh;
    private MaterialPropertyBlock mpb;
    private ActionScript act;
    private bool collideHooked;
    private Vector3[] shapePts;

    // Up to four simultaneous white hit-pulses, decayed on the CPU.
    private struct Hit { public float u; public float t; }
    private readonly Hit[] hits = new Hit[4];
    private const float HitDecay = 0.35f;

    static readonly int ID_Intensity = Shader.PropertyToID("_Intensity");
    static readonly int ID_WallLen   = Shader.PropertyToID("_WallLen");
    static readonly int ID_Thick     = Shader.PropertyToID("_Thick");
    static readonly int ID_Aspect    = Shader.PropertyToID("_Aspect");
    static readonly int ID_Color     = Shader.PropertyToID("_Color");
    static readonly int ID_Color2    = Shader.PropertyToID("_Color2");
    static readonly int[] ID_Hit =
    {
        Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"),
        Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3"),
    };

    public float Hp => ls.hp;
    public float MaxHp => ls.maxHp;
    public bool Weaving => weaving;
    /// <summary>Live span polyline (world-space bezier samples) — placement checks read it to keep
    /// new spans clear of standing walls. Null until the first weave.</summary>
    public IReadOnlyList<Vector3> ShapePoints => shapePts;
    /// <summary>Where the span WILL sit: the reweave destination while moving, the live span
    /// otherwise. Placement checks and no-go paint read this so a mid-flight wall reserves its
    /// landing spot rather than vanishing from the map.</summary>
    public IReadOnlyList<Vector3> PlannedShapePoints => weaving && plannedPts != null ? plannedPts : shapePts;
    private Vector3[] plannedPts;
    /// <summary>Physically present: collider live and charge not emptied. Dead or mid-reshape
    /// walls block nothing — matching their path registration.</summary>
    public bool Standing => col != null && col.enabled && ls != null && !ls.hasDied;

    private void Awake()
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();
        mesh = new Mesh { name = "EnergyWall" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder = sortingOrder;
        for (int i = 0; i < hits.Length; i++) hits[i].t = -999f;
        HookCollide();
    }

    // Register with the ActionScript so we get the contact point of whatever rams the wall.
    private void HookCollide()
    {
        if (collideHooked) return;
        act = GetComponent<ActionScript>();
        if (act == null) return;
        act.onCollides ??= new List<MonoBehaviour>();
        if (!act.onCollides.Contains(this)) act.onCollides.Add(this);
        collideHooked = true;
    }

    public void Init(Vector2 aWorld, Vector2 bWorld, float hp, float maxHp)
    {
        ls.maxHp = Mathf.Max(1f, maxHp);
        ls.hp = Mathf.Clamp(hp, 0.5f, ls.maxHp);
        SetShape(aWorld, bWorld);
        RegisterPathCells();
    }

    // Pathfinding: the span is a chewable wall priced by its charge (ls.hp). Registered while the
    // collider is live; dropped during reshapes (enemies may cross the old line while it's down)
    // and on teardown. Death needs no hook — dead walls read as open in the live-wall lookup.
    // chewCostMult: ONE LifeScript covers the whole span (broken anywhere = broken everywhere) and
    // crowds chew a shared bar together, so pricing every cell at the full bar wildly overstates
    // the cost of going through — register well below the per-block Wall's 1.0.
    public static float chewCostMult = 0.3f;
    /// <summary>Extra rings of path cells around the rastered centerline (1 = 3 cells thick, ends
    /// extended 1 cell). The physical capsule has real width + rounded caps — a bare 1-cell line
    /// lets routes thread past it where bodies can't actually fit.</summary>
    public static int pathInflateCells = 1;
    private void RegisterPathCells()
    {
        if (ls == null || shapePts == null || shapePts.Length < 2 || !PathZone.AtBase(transform.position)) return;
        // Register the VISUAL capsule, not the bare centerline: the rendered wall ends in rounded
        // caps ~halfT past each endpoint, so two spans placed "almost touching" look sealed to the
        // player long before their centerlines meet — but a centerline raster leaves a body-wide
        // corridor there and routes thread the joint. Extend the raster line so the TOTAL tip
        // reach (extension + the inflate ring, which already pokes ~1 cell past the end) matches
        // the visual cap radius — no more, or routes give the tips a wider berth than the drawn
        // wall justifies. Aperture pricing then fills anything narrower than a body between hulls.
        int n = shapePts.Length;
        float cap = Mathf.Max(0f, thickness * 0.5f - BaseBlockMap.CellSize * pathInflateCells);
        var pts = new Vector3[n + 2];
        pts[0] = shapePts[0] + (shapePts[0] - shapePts[1]).normalized * cap;
        for (int i = 0; i < n; i++) pts[i + 1] = shapePts[i];
        pts[n + 1] = shapePts[n - 1] + (shapePts[n - 1] - shapePts[n - 2]).normalized * cap;
        // crossing the inflated span traverses ~(1+2r) wall cells instead of 1 — divide the
        // per-cell mult back down so the total chew price of going through stays calibrated
        BaseBlockMap.RegisterWallPath(ls, pts, chewCostMult / (1 + 2 * pathInflateCells), pathInflateCells);
    }

    private void OnDestroy() => BaseBlockMap.UnregisterWall(ls);
    private void OnDisable() => BaseBlockMap.UnregisterWall(ls);
    private void OnEnable() { if (col != null && col.enabled) RegisterPathCells(); }

    /// <summary>Rescale the wall's max hp (e.g. after a width change). Clamps current hp into range.</summary>
    public void SetMaxHp(float maxHp)
    {
        ls.maxHp = Mathf.Max(1f, maxHp);
        if (ls.hp > ls.maxHp) ls.hp = ls.maxHp;
    }

    public void Heal(float amount)
    {
        if (ls.hasDied || amount <= 0f) return;
        ls.Change(Mathf.Min(amount, ls.maxHp - ls.hp), -1, false, false);
    }

    public void Reweave(Vector2 aWorld, Vector2 bWorld, float t)
    {
        StopAllCoroutines();
        StartCoroutine(ReweaveI(aWorld, bWorld, t));
    }

    private IEnumerator ReweaveI(Vector2 aWorld, Vector2 bWorld, float t)
    {
        weaving = true;
        plannedPts = BuildBezier(aWorld, bWorld, (Vector2)transform.position, Mathf.Max(8, segments), out _);
        col.enabled = false;
        BaseBlockMap.UnregisterWall(ls);   // the moving span blocks nothing until it re-lands
        Vector2 a0 = a, b0 = b;
        for (float f = 0f; f < 1f; f += Time.deltaTime / Mathf.Max(0.1f, t))
        {
            float e = f * f * (3f - 2f * f);
            SetShape(Vector2.Lerp(a0, aWorld, e), Vector2.Lerp(b0, bWorld, e));
            yield return null;
        }
        SetShape(aWorld, bWorld);
        col.enabled = true;
        weaving = false;
        RegisterPathCells();
    }

    // ---- geometry ----

    /// <summary>Sample the gentle bezier between a and b (bowing away from origin) and report its arc length.</summary>
    public static Vector3[] BuildBezier(Vector2 a, Vector2 b, Vector2 origin, int n, out float arcLen)
    {
        Vector2 mid = (a + b) * 0.5f;
        Vector2 away = mid - origin;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.up;
        float len = Vector2.Distance(a, b);
        Vector2 ctrl = mid + away * (0.12f * len);
        var pts = new Vector3[n];
        arcLen = 0f;
        Vector2 prev = a;
        for (int i = 0; i < n; i++)
        {
            float u = i / (n - 1f);
            Vector2 p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * ctrl + u * u * b;
            pts[i] = new Vector3(p.x, p.y, 0f);
            if (i > 0) arcLen += Vector2.Distance(prev, p);
            prev = p;
        }
        return pts;
    }

    private void SetShape(Vector2 aWorld, Vector2 bWorld)
    {
        a = aWorld;
        b = bWorld;
        int n = Mathf.Max(8, segments);
        shapePts = BuildBezier(a, b, (Vector2)transform.position, n, out arcLen);

        // EdgeCollider in local space (unchanged behaviour).
        var cpts = new Vector2[n];
        for (int i = 0; i < n; i++) cpts[i] = (Vector2)shapePts[i] - (Vector2)transform.position;
        col.points = cpts;

        BuildMesh(shapePts, arcLen);
    }

    // Capsule ribbon: a body strip following the bezier, plus one extra row past each end so the
    // shader's SDF can round the caps. uv.x = world arc-length, uv.y = 0..1 across, normals = outward.
    private void BuildMesh(Vector3[] pts, float total)
    {
        int n = pts.Length;
        float halfT = thickness * 0.5f;
        Vector2 origin = transform.position;
        int rows = n + 2;                          // [start cap][body 0..n-1][end cap]
        var verts = new Vector3[rows * 2];
        var norms = new Vector3[rows * 2];
        var uvs   = new Vector2[rows * 2];
        var tris  = new int[(rows - 1) * 6];

        var along = new float[n];
        var nrm = new Vector2[n];
        var tan = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 p  = (Vector2)pts[i];
            Vector2 pa = (Vector2)pts[Mathf.Max(0, i - 1)];
            Vector2 pb = (Vector2)pts[Mathf.Min(n - 1, i + 1)];
            Vector2 t = pb - pa;
            if (t.sqrMagnitude < 1e-6f) t = Vector2.right;
            t.Normalize();
            tan[i] = t;
            nrm[i] = new Vector2(-t.y, t.x);
            along[i] = i == 0 ? 0f : along[i - 1] + Vector2.Distance((Vector2)pts[i - 1], p);
        }

        void Row(int r, Vector2 pos, Vector2 normal, float alongW)
        {
            Vector3 lp = (Vector3)(pos - origin);
            Vector3 across = (Vector3)(normal * halfT);
            verts[2 * r]     = lp - across;
            verts[2 * r + 1] = lp + across;
            Vector3 onrm = new(normal.x, normal.y, 0f);
            norms[2 * r] = onrm; norms[2 * r + 1] = onrm;
            uvs[2 * r]     = new Vector2(alongW, 0f);
            uvs[2 * r + 1] = new Vector2(alongW, 1f);
        }

        Row(0, (Vector2)pts[0] - tan[0] * halfT, nrm[0], -halfT);
        for (int i = 0; i < n; i++) Row(i + 1, (Vector2)pts[i], nrm[i], along[i]);
        Row(n + 1, (Vector2)pts[n - 1] + tan[n - 1] * halfT, nrm[n - 1], total + halfT);

        int ti = 0;
        for (int r = 0; r < rows - 1; r++)
        {
            int a0 = 2 * r, b0 = 2 * r + 1, c0 = 2 * (r + 1), d0 = 2 * (r + 1) + 1;
            tris[ti++] = a0; tris[ti++] = c0; tris[ti++] = b0;
            tris[ti++] = b0; tris[ti++] = c0; tris[ti++] = d0;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    private void Update()
    {
        if (ls == null) return;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);

        float hpFrac = ls.maxHp > 0f ? Mathf.Clamp01(ls.hp / ls.maxHp) : 0f;
        // Translucent when nearly drained, brighter (but not blinding) when fully charged. Constant hue.
        float intensity = Mathf.Lerp(0.22f, 0.8f, hpFrac);
        if (weaving)
        {
            flickerT += Time.deltaTime * 9f;
            intensity *= 0.3f + 0.18f * Mathf.Sin(flickerT * Mathf.PI * 2f);
        }

        mpb.SetFloat(ID_Intensity, intensity);
        mpb.SetFloat(ID_WallLen, Mathf.Max(0.1f, arcLen));
        mpb.SetFloat(ID_Thick, thickness);
        mpb.SetFloat(ID_Aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        mpb.SetColor(ID_Color, tintBright);
        mpb.SetColor(ID_Color2, tintDeep);

        for (int i = 0; i < hits.Length; i++)
        {
            float age = Time.time - hits[i].t;
            float inten = age < HitDecay ? 1f - age / HitDecay : 0f;
            mpb.SetVector(ID_Hit[i], new Vector4(hits[i].u, inten, 0f, 0f));
        }

        mr.SetPropertyBlock(mpb);
    }

    // ---- hit pulses ----

    public void OnCollide(Collision2D collision)
    {
        if (collision == null || collision.rigidbody == null) return;
        // Only hostile bodies flash the wall (skip allied units pushed into it, and FX/Misc).
        if (collision.rigidbody.CompareTag("Allies") || collision.rigidbody.CompareTag("Misc")) return;
        if (collision.contactCount == 0) return;
        RegisterHit(collision.GetContact(0).point);
    }

    private void RegisterHit(Vector2 worldPoint)
    {
        float u = NearestU(worldPoint);
        int slot = 0;
        float oldest = Time.time + 1f;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].t < oldest) { oldest = hits[i].t; slot = i; }
        }
        hits[slot].u = u;
        hits[slot].t = Time.time;
    }

    // Nearest u (0..1 along the wall) to a world point, from the cached bezier samples.
    private float NearestU(Vector2 p)
    {
        if (shapePts == null || shapePts.Length < 2) return 0.5f;
        int n = shapePts.Length;
        float best = float.MaxValue;
        int bi = 0;
        for (int i = 0; i < n; i++)
        {
            float dd = ((Vector2)shapePts[i] - p).sqrMagnitude;
            if (dd < best) { best = dd; bi = i; }
        }
        return bi / (n - 1f);
    }
}
