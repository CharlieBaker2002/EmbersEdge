using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hauling bag on a drone's back — a sac of water, simulated as a real 2D softbody.
///
/// VISUAL MODEL: DroneBag_0..10 are 11 same-size fill frames (13×12 px). One MeshFilter/
/// MeshRenderer quad grid (GX×GY nodes) is textured with the current fill frame (UVs swap per
/// frame), so deforming the grid deforms the art. The mesh renders with the pipeline's default
/// sprite material (the border/lit graph doesn't carry over to a MeshRenderer); the authored
/// SpriteRenderer becomes a hidden template that only donates its sorting. Three 1-px
/// LineRenderer strings (left/centre/right, second-darkest scheme white) tie the sac to the
/// drone's back and flex with the first free node row.
///
/// MOTION MODEL: a spring-mass lattice in WORLD space, Verlet-integrated and PBD-solved:
///   • the bottom node row is pinned to the drone — every move and turn of the body shakes
///     the sac for free (the bag is driven by how the drone moves, not by time);
///   • distance constraints (structural + shear) hold the lattice together — their stiffness
///     is the rigidity dial;
///   • a boundary AREA constraint conserves the water volume: squash the sac one way and it
///     bulges the other — the water-sac signature;
///   • a shape spring pulls nodes toward the rigid pose (return-to-rest / pendulum mode);
///   • a slosh kick from the drone's measured acceleration makes the contents lag, growing
///     up the bag (the top whips while the base has settled).
///
/// FILL CHARACTER (fill fraction = cargo space used) — same spec as the old banded bag:
///   • empty — amplitude gate is 0: the bag hangs dead still (sim goes dormant).
///   • mid   — loose constraints, strong slosh kick, high damping: fluid sloshing where the
///     surface visibly disagrees with itself.
///   • full  — constraints go rigid (the sac moves as one) but damping drops and the shape
///     spring slows: one heavy underdamped pendulum with overshoot. The REST SHAPE also
///     swells (fullScaleBump), so loading cargo visibly inflates the sac with a soft bounce.
/// Fill also picks the sprite frame (round(fill·10)).
/// </summary>
public class SackWobble : MonoBehaviour
{
    [Header("Cargo (the bag defines hauling)")]
    public int maxSpace = 8;
    public int orbSpace = 1;
    public int chipSmallSpace = 1;
    public int chipBigSpace = 2;
    public int chipLargeSpace = 3;
    public int batterySpace = 4;
    public int equipmentSpace = 3;

    [Header("Softbody feel")]
    [Tooltip("Node kick per m/s² of the drone's acceleration — the contents lag behind the body.")]
    public float sloshKick = 0.5f;
    [Tooltip("Shape spring (s⁻²) toward the rigid pose: quick return (mid) → slow heavy swing (full).")]
    public float shapeKLoose = 26f, shapeKFull = 15f;
    [Tooltip("Velocity damping (s⁻¹): mid-fill slosh settles fast (viscous); a full bag keeps swinging.")]
    public float dragLoose = 5f, dragFull = 1.2f;
    [Tooltip("Distance-constraint stiffness: floppy cloth (mid) → rigid body (full).")]
    public float distLoose = 0.4f, distFull = 1f;
    [Tooltip("Area-conservation strength — the incompressible-water bulge (fades out below half full).")]
    public float pressureK = 0.8f;
    [Tooltip("Node leash from its rigid-pose position, as a fraction of bag height.")]
    public float maxOffsetFrac = 0.55f;
    public float fullScaleBump = 1.08f;

    [Header("Strings (the sac is tied on)")]
    [Tooltip("Sideways offset of the left/right string anchors on the drone's back (world units).")]
    public float stringSpread = 0.1f;
    [Tooltip("Sack-local Y of the string anchors — roughly the drone's back edge.")]
    public float stringAnchorY = -0.17f;

    const int GX = 5, GY = 5, N = GX * GY, ITER = 4;
    const float EMPTY = 1e-3f;
    // second-darkest white of the palette ramp (#90bbb0)
    static readonly Color STRING_WHITE = new Color32(0x90, 0xbb, 0xb0, 0xff);
    // string tips ride the first FREE node row (left/centre/right) so the ties flex with the sac
    static readonly int[] STRING_TIPS = { GX, GX + GX / 2, 2 * GX - 1 };

    // art
    int frameCount;
    int currentFrame = -1;
    Vector2[][] uvCache;                    // [frame][node]
    Mesh mesh;
    float bagW, bagH;

    // sim state — node positions live in WORLD space
    readonly Vector2[] pos = new Vector2[N];
    readonly Vector2[] prev = new Vector2[N];
    readonly Vector2[] rigid = new Vector2[N];   // rigid-pose targets, rebuilt every step
    Vector2[] restLocal;                         // unswelled local layout (bottom row = mount)
    int[] consA, consB;
    float[] consRest;                            // unswelled rest lengths
    int[] loop;                                  // boundary node indices, CCW
    Vector2[] grad;                              // area-constraint gradients (scratch)
    float baseArea;
    Vector3[] verts;
    LineRenderer[] strings;

    float fill;
    Rigidbody2D droneRb;
    Vector2 prevVel, prevBasePos;
    bool built, dormant;

    void Awake() => Build();

    void Build()
    {
        if (built) return;
        built = true;
        droneRb = GetComponentInParent<Rigidbody2D>();

        var template = GetComponent<SpriteRenderer>();
        Sprite[] frames = DroneManager.LoadStripNumeric("DroneBag");
        if (frames == null || frames.Length == 0)
        {
            // no bag art — keep whatever single sprite is authored, no softbody
            frameCount = 0;
            return;
        }
        frameCount = frames.Length;

        float ppu = frames[0].pixelsPerUnit;
        bagW = frames[0].rect.width / ppu;
        bagH = frames[0].rect.height / ppu;

        // rest layout: rect centred on the transform, row 0 (the mount side) at the bottom
        restLocal = new Vector2[N];
        for (int j = 0; j < GY; j++)
            for (int i = 0; i < GX; i++)
                restLocal[j * GX + i] = new Vector2(
                    (i / (float)(GX - 1) - 0.5f) * bagW,
                    j / (float)(GY - 1) * bagH - bagH * 0.5f);

        // distance constraints: structural (neighbours) + shear (both diagonals)
        var ca = new List<int>(); var cb = new List<int>();
        for (int j = 0; j < GY; j++)
            for (int i = 0; i < GX; i++)
            {
                int n = j * GX + i;
                if (i < GX - 1) { ca.Add(n); cb.Add(n + 1); }
                if (j < GY - 1) { ca.Add(n); cb.Add(n + GX); }
                if (i < GX - 1 && j < GY - 1)
                {
                    ca.Add(n); cb.Add(n + GX + 1);
                    ca.Add(n + 1); cb.Add(n + GX);
                }
            }
        consA = ca.ToArray(); consB = cb.ToArray();
        consRest = new float[consA.Length];
        for (int c = 0; c < consA.Length; c++)
            consRest[c] = (restLocal[consB[c]] - restLocal[consA[c]]).magnitude;

        // boundary loop (CCW) for the area constraint
        loop = new int[2 * (GX + GY) - 4];
        int k = 0;
        for (int i = 0; i < GX; i++) loop[k++] = i;
        for (int j = 1; j < GY; j++) loop[k++] = j * GX + GX - 1;
        for (int i = GX - 2; i >= 0; i--) loop[k++] = (GY - 1) * GX + i;
        for (int j = GY - 2; j >= 1; j--) loop[k++] = j * GX;
        grad = new Vector2[loop.Length];
        baseArea = 0f;
        for (int b = 0; b < loop.Length; b++)
        {
            Vector2 p0 = restLocal[loop[b]], p1 = restLocal[loop[(b + 1) % loop.Length]];
            baseArea += p0.x * p1.y - p1.x * p0.y;
        }
        baseArea *= 0.5f;

        // the deforming mesh — one renderer replaces the authored (whole-bag) sprite
        var go = new GameObject("sack_softbody") { layer = gameObject.layer };
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mesh = new Mesh { name = "sack_softbody" };
        mesh.MarkDynamic();
        verts = new Vector3[N];
        for (int n = 0; n < N; n++) verts[n] = restLocal[n];
        mesh.vertices = verts;
        var tris = new int[(GX - 1) * (GY - 1) * 6];
        int t = 0;
        for (int j = 0; j < GY - 1; j++)
            for (int i = 0; i < GX - 1; i++)
            {
                int a = j * GX + i, b = a + 1, c = a + GX, d = c + 1;
                tris[t++] = a; tris[t++] = d; tris[t++] = b;
                tris[t++] = d; tris[t++] = a; tris[t++] = c;
            }
        mesh.triangles = tris;
        var cols = new Color32[N];
        for (int n = 0; n < N; n++) cols[n] = new Color32(255, 255, 255, 255);
        mesh.colors32 = cols;
        float leash = bagH * maxOffsetFrac;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(
            bagW * fullScaleBump + 2f * leash, bagH * fullScaleBump + 2f * leash, 1f));
        mf.sharedMesh = mesh;

        // strings under the sac, sac above the strings — same band the old segments used
        if (template != null)
        {
            mr.sortingLayerID = template.sortingLayerID;
            mr.sortingOrder = template.sortingOrder + 1;
            template.enabled = false;
        }
        mr.sharedMaterial = DefaultSpriteMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var tex = frames[0].texture;
        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture("_MainTex", tex);
        mpb.SetVector("_MainTex_TexelSize", new Vector4(1f / tex.width, 1f / tex.height, tex.width, tex.height));
        mr.SetPropertyBlock(mpb);

        // tie-strings: 1-px lines from the drone's back to the sac, splaying out to its sides
        strings = new LineRenderer[STRING_TIPS.Length];
        for (int s2 = 0; s2 < strings.Length; s2++)
        {
            var sgo = new GameObject("sack_string" + s2) { layer = gameObject.layer };
            sgo.transform.SetParent(transform, false);
            var lr = sgo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;            // local endpoints ride the interpolated transform
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = 1f / ppu;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.sharedMaterial = DefaultSpriteMaterial();
            lr.startColor = lr.endColor = STRING_WHITE;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (template != null)
            {
                lr.sortingLayerID = template.sortingLayerID;
                lr.sortingOrder = template.sortingOrder;
            }
            lr.SetPosition(0, new Vector3((s2 - 1) * stringSpread, stringAnchorY, 0f));
            strings[s2] = lr;
        }

        // per-frame UVs across each fill frame's rect
        uvCache = new Vector2[frameCount][];
        for (int f = 0; f < frameCount; f++)
        {
            var r = frames[f].rect;
            float tw = frames[f].texture.width, th = frames[f].texture.height;
            uvCache[f] = new Vector2[N];
            for (int j = 0; j < GY; j++)
                for (int i = 0; i < GX; i++)
                    uvCache[f][j * GX + i] = new Vector2(
                        (r.x + i / (float)(GX - 1) * r.width) / tw,
                        (r.y + j / (float)(GY - 1) * r.height) / th);
        }

        SnapToRigid();
        SetFill(fill, true);
    }

    /// <summary>0..1 — cargo space used / maxSpace. Picks the sprite frame; the springs read
    /// the new fill next step (the rest shape swells, so the sac inflates with a soft bounce).</summary>
    public void SetFill(float f, bool force = false)
    {
        fill = Mathf.Clamp01(f);
        if (frameCount == 0 || mesh == null) return;
        int frame = Mathf.Clamp(Mathf.RoundToInt(fill * (frameCount - 1)), 0, frameCount - 1);
        if (frame == currentFrame && !force) return;
        currentFrame = frame;
        mesh.uv = uvCache[frame];
    }

    void OnEnable()
    {
        Build();
        if (frameCount == 0 || mesh == null) return;
        dormant = false;
        SnapToRigid();   // the drone may have been moved while frozen/inactive
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    float Swell() => Mathf.Lerp(1f, fullScaleBump, fill);

    /// <summary>Rest position in local space, swelled about the bottom-centre mount point.</summary>
    Vector2 Swelled(int n, float s)
    {
        Vector2 r = restLocal[n];
        return new Vector2(r.x * s, (r.y + bagH * 0.5f) * s - bagH * 0.5f);
    }

    void SnapToRigid()
    {
        float s = Swell();
        for (int n = 0; n < N; n++)
        {
            rigid[n] = transform.TransformPoint(Swelled(n, s));
            pos[n] = prev[n] = rigid[n];
        }
        prevBasePos = transform.position;
        prevVel = droneRb != null ? droneRb.linearVelocity : Vector2.zero;
        WriteMesh();
    }

    void WriteMesh()
    {
        for (int n = 0; n < N; n++)
        {
            Vector3 l = transform.InverseTransformPoint(pos[n]);
            verts[n] = new Vector3(l.x, l.y, 0f);
        }
        mesh.vertices = verts;   // bounds are fixed (leash bounds every node)
        if (strings != null)
            for (int s2 = 0; s2 < strings.Length; s2++)
                strings[s2].SetPosition(1, verts[STRING_TIPS[s2]]);
    }

    /// <summary>What a plain SpriteRenderer would get (URP 2D: Sprite-Lit-Default) — probed
    /// from a throwaway renderer rather than Shader.Find, so build stripping can't bite.</summary>
    static Material defaultSpriteMat;
    static Material DefaultSpriteMaterial()
    {
        if (defaultSpriteMat == null)
        {
            var probe = new GameObject("sprite_mat_probe") { hideFlags = HideFlags.HideAndDontSave };
            defaultSpriteMat = probe.AddComponent<SpriteRenderer>().sharedMaterial;
            Destroy(probe);
        }
        return defaultSpriteMat;
    }

    void FixedUpdate()
    {
        if (mesh == null || frameCount == 0) return;
        float dt = Time.fixedDeltaTime;
        Vector2 basePos = transform.position;

        if (dormant)
        {
            if (fill <= EMPTY) return;
            dormant = false;
            SnapToRigid();   // world state is stale after sleeping
            return;
        }

        // teleport (dimension hop, thaw reposition): don't whip across the map — restart at rest
        if ((basePos - prevBasePos).sqrMagnitude > 0.5625f) { SnapToRigid(); return; }

        float swell = Swell();
        for (int n = 0; n < N; n++) rigid[n] = transform.TransformPoint(Swelled(n, swell));

        // empty bag: dead still — relax to the rigid pose, then sleep
        if (fill <= EMPTY)
        {
            float relax = 10f * dt, maxSq = 0f;
            for (int n = 0; n < N; n++)
            {
                pos[n] = Vector2.Lerp(pos[n], rigid[n], relax);
                prev[n] = pos[n];
                maxSq = Mathf.Max(maxSq, (pos[n] - rigid[n]).sqrMagnitude);
            }
            prevBasePos = basePos;
            prevVel = droneRb != null ? droneRb.linearVelocity : Vector2.zero;
            if (maxSq < 1e-6f)
            {
                for (int n = 0; n < N; n++) pos[n] = prev[n] = rigid[n];
                dormant = true;
            }
            WriteMesh();
            return;
        }

        // the drive: the DRONE'S own acceleration, world space
        Vector2 vel = droneRb != null ? droneRb.linearVelocity : (basePos - prevBasePos) / dt;
        Vector2 accel = (vel - prevVel) / dt;
        prevVel = vel;
        prevBasePos = basePos;

        // fluidity peaks at half-full (sloshing water); a full bag moves as one rigid mass
        float fluidity = 4f * fill * (1f - fill);
        float shapeK = Mathf.Lerp(shapeKLoose, shapeKFull, fill);
        float damp = Mathf.Clamp01(1f - Mathf.Lerp(dragLoose, dragFull, fill) * dt);
        float distS = Mathf.Lerp(distLoose, distFull, fill);
        float press = pressureK * Mathf.Clamp01(fill * 2f);
        float dt2 = dt * dt;

        // integrate free nodes (Verlet); the pinned bottom row rides the drone exactly
        for (int n = 0; n < N; n++)
        {
            if (n < GX) { prev[n] = pos[n] = rigid[n]; continue; }
            float hFrac = n / GX / (float)(GY - 1);
            Vector2 v = (pos[n] - prev[n]) * damp;
            Vector2 acc = (rigid[n] - pos[n]) * shapeK
                          - accel * (sloshKick * fill * (0.3f + fluidity) * hFrac);
            prev[n] = pos[n];
            pos[n] += v + acc * dt2;
        }

        for (int it = 0; it < ITER; it++)
        {
            // distance constraints
            for (int c = 0; c < consA.Length; c++)
            {
                int a = consA[c], b = consB[c];
                float wa = a < GX ? 0f : 1f, wb = b < GX ? 0f : 1f;
                float w = wa + wb;
                if (w <= 0f) continue;
                Vector2 d = pos[b] - pos[a];
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                Vector2 corr = d * ((len - consRest[c] * swell) / len / w * distS);
                pos[a] += corr * wa;
                pos[b] -= corr * wb;
            }

            // area constraint: the water doesn't compress — squash it and it bulges elsewhere
            if (press > 0f)
            {
                float area = 0f;
                for (int b = 0; b < loop.Length; b++)
                {
                    Vector2 p0 = pos[loop[b]], p1 = pos[loop[(b + 1) % loop.Length]];
                    area += p0.x * p1.y - p1.x * p0.y;
                }
                area *= 0.5f;
                float denom = 0f;
                for (int b = 0; b < loop.Length; b++)
                {
                    Vector2 d = pos[loop[(b + 1) % loop.Length]] - pos[loop[(b - 1 + loop.Length) % loop.Length]];
                    grad[b] = new Vector2(d.y, -d.x) * 0.5f;
                    if (loop[b] >= GX) denom += grad[b].sqrMagnitude;
                }
                if (denom > 1e-8f)
                {
                    float lam = (area - baseArea * swell * swell) / denom * press;
                    for (int b = 0; b < loop.Length; b++)
                        if (loop[b] >= GX) pos[loop[b]] -= grad[b] * lam;
                }
            }
        }

        // leash: a node never strays far from its rigid-pose spot (stability + old maxSwing role)
        float leash = bagH * maxOffsetFrac, leashSq = leash * leash;
        for (int n = GX; n < N; n++)
        {
            Vector2 off = pos[n] - rigid[n];
            if (off.sqrMagnitude > leashSq) pos[n] = rigid[n] + off.normalized * leash;
        }

        WriteMesh();
    }
}
