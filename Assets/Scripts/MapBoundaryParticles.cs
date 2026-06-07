using System.Collections;
using UnityEngine;

/// <summary>
/// Gentle particle motes that drift along the map boundary. Emits soft particles at random points
/// sampled along MapManager's polygon (in world space, so it tracks reshaping + Shrink scaling),
/// nudged slightly outward with a lazy drift. Self-contained: builds its own world-space
/// ParticleSystem, a soft round texture, and material.
///
/// Drop on an empty GameObject (e.g. a child of MapManager).
/// </summary>
public class MapBoundaryParticles : MonoBehaviour
{
    [Header("Emission")]
    [Tooltip("Particles spawned per second around the whole boundary.")]
    public float rate = 14f;

    [Header("Look (gentle)")]
    public Vector2 lifetime = new Vector2(1.5f, 3f);
    public Vector2 size = new Vector2(0.06f, 0.16f);
    [Tooltip("How far off the boundary line particles can spawn.")]
    public float bandJitter = 0.15f;
    [Tooltip("Gentle outward speed range.")]
    public Vector2 outwardSpeed = new Vector2(0.05f, 0.35f);
    [Tooltip("Sideways drift along the edge.")]
    public float tangentDrift = 0.15f;
    [Range(0f, 1f)] public float peakAlpha = 0.55f;
    [Tooltip("Gentle spin, max degrees/second (random sign per particle).")]
    public float spin = 40f;

    [Header("Era material (GS.MatByEra)")]
    public bool bright = true;
    public bool lit = false;
    public bool superBright = false;

    [Header("Wiring")]
    public string sortingLayer = "Projectiles";
    public int sortingOrder = 5;

    ParticleSystem ps;
    ParticleSystemRenderer pr;
    Texture jlTex;
    float accum;

    IEnumerator Start()
    {
        while (MapManager.i == null || MapManager.i.poly == null || MapManager.i.poly.points.Length < 3
               || SpawnManager.instance == null)
            yield return null;

        BuildSystem();
        GS.OnNewEra += SetEra;
    }

    void OnDestroy()
    {
        GS.OnNewEra -= SetEra;
    }

    void SetEra(int era)
    {
        if (pr == null) return;
        var m = new Material(GS.MatByEra(era, bright, lit, superBright));
        if (jlTex != null) m.mainTexture = jlTex;
        pr.sharedMaterial = m;
    }

    void BuildSystem()
    {
        ps = gameObject.GetComponent<ParticleSystem>();
        if (ps == null) ps = gameObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // drift independently of this GO
        main.startSpeed = 0f;                                          // we set velocity per particle
        main.startSize = 0.1f;
        main.startLifetime = 2f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);  // random initial angle (radians)
        main.maxParticles = 2000;
        main.playOnAwake = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.enabled = false;                                      // manual emission in Update

        var shape = ps.shape;
        shape.enabled = false;

        // Soft fade in/out over life (peaks at peakAlpha).
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Gentle grow-then-shrink.
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Gentle spin (random sign). API is radians/sec.
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-spin * Mathf.Deg2Rad, spin * Mathf.Deg2Rad);

        // Animate the JLVisual strip (8x1) across each particle's life.
        jlTex = Resources.Load<Texture2D>("JLVisual");
        var tsa = ps.textureSheetAnimation;
        if (jlTex != null)
        {
            tsa.enabled = true;
            tsa.numTilesX = 8;
            tsa.numTilesY = 1;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.cycleCount = 2;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 8f);   // random start frame
        }

        // Renderer using the era material (instanced) + the JLVisual sprite.
        pr = GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.alignment = ParticleSystemRenderSpace.View;
        var mat = new Material(GS.MatByEra(GS.era, bright, lit, superBright));
        mat.mainTexture = jlTex != null ? jlTex : SoftDot();
        pr.sharedMaterial = mat;
        pr.sortingLayerName = sortingLayer;
        pr.sortingOrder = sortingOrder;
    }

    void Update()
    {
        if (ps == null || MapManager.i == null || MapManager.i.poly == null) return;
        Vector2[] pts = MapManager.i.poly.points;
        int n = pts.Length;
        if (n < 3) return;

        Transform polyT = MapManager.i.poly.transform;

        accum += rate * Time.deltaTime;
        int count = Mathf.FloorToInt(accum);
        if (count <= 0) return;
        accum -= count;

        // Centroid (local) for outward orientation.
        Vector2 c = Vector2.zero;
        for (int k = 0; k < n; k++) c += pts[k];
        c /= n;

        for (int e = 0; e < count; e++)
        {
            int i = Random.Range(0, n);
            int j = (i + 1) % n;
            float ft = Random.value;
            Vector2 pLocal = Vector2.Lerp(pts[i], pts[j], ft);

            Vector2 edge = pts[j] - pts[i];
            Vector2 nrm = new Vector2(edge.y, -edge.x);
            float mag = nrm.magnitude;
            nrm = mag > 1e-5f ? nrm / mag : (pLocal - c).normalized;
            if (Vector2.Dot(nrm, pLocal - c) < 0f) nrm = -nrm;          // outward
            Vector2 tan = new Vector2(-nrm.y, nrm.x);

            // Jitter off the line, then to world.
            pLocal += nrm * Random.Range(-bandJitter, bandJitter);
            Vector3 worldPos = polyT.TransformPoint(pLocal);

            float spd = Random.Range(outwardSpeed.x, outwardSpeed.y);
            Vector2 velLocal = nrm * spd + tan * Random.Range(-tangentDrift, tangentDrift);
            Vector3 worldVel = polyT.TransformVector(velLocal);

            // Depth FX: size tracks speed — slower motes are smaller (read as further away).
            float spdNorm = outwardSpeed.y > outwardSpeed.x
                ? Mathf.InverseLerp(outwardSpeed.x, outwardSpeed.y, spd) : 0.5f;
            float startSize = Mathf.Lerp(size.x, size.y, spdNorm);

            var ep = new ParticleSystem.EmitParams
            {
                position = worldPos,
                velocity = worldVel,
                startColor = Color.white,                    // era colour comes from the material
                startSize = startSize,
                startLifetime = Random.Range(lifetime.x, lifetime.y),
            };
            ps.Emit(ep, 1);
        }
    }

    // 32x32 radial-alpha dot so motes are soft, no asset dependency.
    static Texture2D s_dot;
    static Texture2D SoftDot()
    {
        if (s_dot != null) return s_dot;
        const int R = 32;
        var t = new Texture2D(R, R, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 mid = new Vector2((R - 1) * 0.5f, (R - 1) * 0.5f);
        float maxD = mid.x;
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), mid) / maxD;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                       // softer falloff
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        s_dot = t;
        return s_dot;
    }
}
