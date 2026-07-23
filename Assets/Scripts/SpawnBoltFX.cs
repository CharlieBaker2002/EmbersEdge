using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The enemy-spawn strike: a chain-lightning bolt from the thing that spawned an enemy (base
/// ember core, dungeon spawner, pocket core) into the enemy as it materialises, replacing the old
/// faint HintLine. Spawns from the same source within <see cref="CHAIN_WINDOW"/> seconds hop off
/// the PREVIOUS enemy instead of re-striking from the source, so a rapid wave reads as one chain
/// of lightning (same language as the static status effect's tree).
///
/// All code-built, single-element (era colour only). The bolt is a LineRenderer on an instance of
/// GS.MatByEra(superBright) — the Glow Unlit graph, so colour lives in `thecolor` (HDR, blooms),
/// alpha rides _MainTex and vertex colour is inert (same contract as CableFlowTint). The crackle
/// comes from two places at once: the LR points re-wander every tick, and the material cycles
/// through procedurally drawn lightning-strip frames (the "sprite animation"). Fade-out is width
/// collapse, never colour alpha (the shader ignores it).
/// </summary>
public static class SpawnBoltFX
{
    const float CHAIN_WINDOW = 0.5f;   // spawns closer together than this hop off the previous enemy
    const float MAX_BOLT_DIST = 30f;   // sanity cap (arena spawns can sit across the map) — burst only

    internal static readonly int ColorId = Shader.PropertyToID("thecolor");
    internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    internal static readonly int EmissionId = Shader.PropertyToID("_Emission");

    struct ChainState { public Vector2 lastEnd; public float time; }
    static readonly Dictionary<object, ChainState> chains = new Dictionary<object, ChainState>();

    static Texture2D[] boltFrames;
    static Texture2D hazeTex;
    static Texture2D sparkTex;
    static Material sparkMat;

    // Play-stop destroys the runtime textures/materials but not these static refs (GS.qutting
    // skips teardown) — drop them ourselves or the next play mode renders with dead assets.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        chains.Clear();
        boltFrames = null;
        hazeTex = null;
        sparkTex = null;
        sparkMat = null;
    }

    /// <summary>The spawn strike. source keys the chain (one chain per core/spawner/pocket);
    /// sourcePos is where a fresh chain strikes from; enemyPos is where the enemy materialised.</summary>
    public static void Chain(object source, Vector2 sourcePos, Vector2 enemyPos, float intensity = 1f)
    {
        Vector2 from = sourcePos;
        bool root = true;
        if (source != null && chains.TryGetValue(source, out ChainState st) && Time.time - st.time <= CHAIN_WINDOW)
        {
            from = st.lastEnd;
            root = false;
        }
        if (source != null)
        {
            chains[source] = new ChainState { lastEnd = enemyPos, time = Time.time };
            if (chains.Count > 32) Prune();
        }

        ImpactBurst(enemyPos, intensity);
        float dist = Vector2.Distance(from, enemyPos);
        if (dist > MAX_BOLT_DIST || dist < 0.05f) return;
        if (root) ImpactBurst(from, 0.5f * intensity);   // the source cracks as the chain leaves it
        Strike(from, enemyPos, intensity);
    }

    /// <summary>Pocket spawns with no core of their own: the bolt arrives from the nearest
    /// UNDISCOVERED core spot — the old core-hunting breadcrumb, now electric but dimmed so it
    /// stays a hint rather than a reveal.</summary>
    public static void ChainToNearestCore(object source, Vector2 enemyPos)
    {
        Vector2? spot = MineDungeonManager.i != null ? MineDungeonManager.i.NearestCoreSpot(enemyPos) : null;
        if (spot == null) { ImpactBurst(enemyPos); return; }
        Chain(source, spot.Value, enemyPos, 0.5f);
    }

    /// <summary>One bolt, no chain bookkeeping — full length on the very first frame.</summary>
    public static void Strike(Vector2 from, Vector2 to, float intensity = 1f)
    {
        var go = new GameObject("SpawnBolt");
        go.transform.position = new Vector3(from.x, from.y, 0f);
        go.AddComponent<SpawnBolt>().Init(from, to, Mathf.Clamp(intensity, 0.15f, 2f));
    }

    static void Prune()
    {
        List<object> stale = null;
        foreach (var kv in chains)
            if (Time.time - kv.Value.time > CHAIN_WINDOW) (stale ??= new List<object>()).Add(kv.Key);
        if (stale != null) foreach (object k in stale) chains.Remove(k);
    }

    // ---- impact ------------------------------------------------------------------------------

    /// <summary>The materialise burst at the enemy end: a white-hot flash, fast speed-stretched
    /// sparks and a few slow drifting embers, all fading through the era colour.</summary>
    public static void ImpactBurst(Vector2 pos, float intensity = 1f)
    {
        Color col = GS.ColFromEra();
        var root = new GameObject("SpawnStrikeBurst");
        root.transform.position = new Vector3(pos.x, pos.y, 0f);

        var ep = new ParticleSystem.EmitParams();

        // flash + drifting embers
        ParticleSystem soft = NewBurstPS(root.transform, col, false);
        ep.velocity = Vector3.zero;
        ep.startLifetime = 0.14f;
        ep.startSize = 0.95f * intensity;
        soft.Emit(ep, 1);
        int embers = Mathf.Max(3, Mathf.RoundToInt(7f * intensity));
        for (int k = 0; k < embers; k++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            ep.velocity = (Vector3)(dir * Random.Range(0.35f, 1.2f));
            ep.startLifetime = Random.Range(0.45f, 0.85f);
            ep.startSize = Random.Range(0.07f, 0.15f) * intensity;
            soft.Emit(ep, 1);
        }

        // fast sparks, stretched along their velocity so they read as electric filaments
        ParticleSystem sparks = NewBurstPS(root.transform, col, true);
        int n = Mathf.Max(6, Mathf.RoundToInt(16f * intensity));
        for (int k = 0; k < n; k++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            ep.velocity = (Vector3)(dir * Random.Range(2.4f, 5.2f));
            ep.startLifetime = Random.Range(0.16f, 0.36f);
            ep.startSize = Random.Range(0.05f, 0.11f) * intensity;
            sparks.Emit(ep, 1);
        }

        Object.Destroy(root, 1.6f);
    }

    static ParticleSystem NewBurstPS(Transform parent, Color col, bool stretch)
    {
        var go = new GameObject(stretch ? "sparks" : "glow");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // configure before playing

        var main = ps.main;
        main.loop = false;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var em = ps.emission;
        em.enabled = false;   // everything arrives via Emit()

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(col, 0.3f), new GradientColorKey(col, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
        colOver.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch)
        {
            r.lengthScale = 2.4f;
            r.velocityScale = 0f;
        }
        r.material = SparkMat();
        r.sortingLayerName = "Power Ups";
        r.sortingOrder = 31;

        ps.Play();
        return ps;
    }

    static Material SparkMat()
    {
        if (sparkMat == null)
        {
            // Sprite-Unlit for anything that fades via particle colour — Glow mats ignore alpha.
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            sparkMat = new Material(sh) { mainTexture = SparkTex() };
        }
        return sparkMat;
    }

    // ---- the code-drawn sprite frames --------------------------------------------------------

    /// <summary>Six lightning-strip frames (jagged white core + gaussian glow, alpha-shaped, tips
    /// tapered) the bolt LR cycles through — drawn once, cached for the session.</summary>
    internal static Texture2D[] BoltFrames()
    {
        if (boltFrames != null) return boltFrames;
        const int N = 6, W = 384, H = 96, SEGS = 32;
        boltFrames = new Texture2D[N];
        for (int f = 0; f < N; f++)
        {
            // midpoint-displacement midline, endpoints pinned to the centre row
            var ys = new float[SEGS + 1];
            for (int i = 0; i <= SEGS; i++) ys[i] = H * 0.5f;
            int step = SEGS;
            float disp = H * 0.30f;
            while (step > 1)
            {
                for (int i = 0; i + step <= SEGS; i += step)
                {
                    int mid = i + step / 2;
                    ys[mid] = Mathf.Clamp((ys[i] + ys[i + step]) * 0.5f + Random.Range(-disp, disp),
                                          H * 0.14f, H * 0.86f);
                }
                step /= 2;
                disp *= 0.55f;
            }

            var px = new Color32[W * H];
            float hotAt = Random.Range(0.15f, 0.85f), hotW = Random.Range(0.04f, 0.1f);
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                float fi = u * SEGS;
                int i0 = Mathf.Min((int)fi, SEGS - 1);
                float yc = Mathf.Lerp(ys[i0], ys[i0 + 1], fi - i0);
                float env = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.09f);               // tip taper
                env *= 0.8f + 0.2f * Mathf.Sin(u * 37f + f * 5f);                       // shimmer
                env *= 1f + 0.5f * Mathf.Exp(-Mathf.Pow((u - hotAt) / hotW, 2f));      // hot pinch
                for (int y = 0; y < H; y++)
                {
                    float d = Mathf.Abs(y - yc);
                    float a = d < 2f ? 1f : 0.62f * Mathf.Exp(-Mathf.Pow((d - 2f) / 8.5f, 2f));
                    a = Mathf.Clamp01(a * env);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            boltFrames[f] = tex;
        }
        return boltFrames;
    }

    /// <summary>A soft gaussian band for the wide under-glow pass beneath the bolt.</summary>
    internal static Texture2D HazeTex()
    {
        if (hazeTex != null) return hazeTex;
        const int W = 64, H = 64;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float u = x / (float)(W - 1);
            float env = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.12f);
            for (int y = 0; y < H; y++)
            {
                float a = 0.55f * env * Mathf.Exp(-Mathf.Pow((y - H * 0.5f) / (H * 0.22f), 2f));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        hazeTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        hazeTex.SetPixels32(px);
        hazeTex.Apply();
        return hazeTex;
    }

    static Texture2D SparkTex()
    {
        if (sparkTex != null) return sparkTex;
        const int S = 48;
        var px = new Color32[S * S];
        for (int x = 0; x < S; x++)
            for (int y = 0; y < S; y++)
            {
                float r = Mathf.Sqrt((x - S * 0.5f) * (x - S * 0.5f) + (y - S * 0.5f) * (y - S * 0.5f));
                float a = Mathf.Pow(Mathf.Clamp01(1f - r / (S * 0.48f)), 1.8f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        sparkTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        sparkTex.SetPixels32(px);
        sparkTex.Apply();
        return sparkTex;
    }

    internal static LineRenderer NewLR(GameObject go, Material m, int order)
    {
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = m;
        lr.sortingLayerName = "Power Ups";
        lr.sortingOrder = order;
        return lr;
    }

    internal static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }
}

/// <summary>
/// One bolt of the spawn chain. A quadratic-bezier backbone bows the strike off the straight line;
/// every ~45ms the interior points re-wander (smoothed noise, endpoints pinned), the material hops
/// to a different lightning frame, and the 1–3 fork branches rebuild off the fresh path — so the
/// whole strike crackles without ever moving its endpoints. Life is width: a fat attack pop, then
/// a flickering collapse to nothing (Glow Unlit ignores colour alpha, so width IS the fade).
/// </summary>
public class SpawnBolt : MonoBehaviour
{
    const float LIFE = 0.5f;
    const float REROLL = 0.045f;
    const float ATTACK = 0.07f;
    const float BRANCH_LIFE = LIFE * 0.55f;
    const int BRANCH_PTS = 8;

    LineRenderer main, haze;
    readonly List<LineRenderer> branches = new List<LineRenderer>();
    readonly List<float> branchU = new List<float>();
    readonly List<float> branchSide = new List<float>();
    readonly List<float> branchAng = new List<float>();
    readonly List<float> branchLen = new List<float>();

    Material mainMat, hazeMat;
    Vector3[] basePts, pts;
    Vector2 perp;
    float dist, peakW, t, rerollT, flicker = 1f;
    int frame;

    public void Init(Vector2 from, Vector2 to, float intensity)
    {
        dist = Vector2.Distance(from, to);
        Vector2 dir = (to - from).normalized;
        perp = new Vector2(-dir.y, dir.x);

        // the backbone: a gentle random bow so no two strikes share a path
        int n = Mathf.Clamp(Mathf.RoundToInt(dist * 4f) + 1, 12, 44);
        basePts = new Vector3[n];
        pts = new Vector3[n];
        Vector2 ctrl = (from + to) * 0.5f
                       + perp * (dist * Random.Range(0.10f, 0.22f) * (Random.value < 0.5f ? -1f : 1f));
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1), iu = 1f - u;
            basePts[i] = pts[i] = iu * iu * from + 2f * iu * u * ctrl + u * u * to;
        }

        Material src = GS.MatByEra(GS.era, superBright: true);
        mainMat = new Material(src);
        hazeMat = new Material(src);
        hazeMat.SetTexture(SpawnBoltFX.MainTexId, SpawnBoltFX.HazeTex());
        hazeMat.SetTexture(SpawnBoltFX.EmissionId, SpawnBoltFX.HazeTex());
        if (hazeMat.HasProperty(SpawnBoltFX.ColorId))
        {
            // same hue, a fraction of the HDR punch — an aura, not a second bolt
            Color c = hazeMat.GetColor(SpawnBoltFX.ColorId);
            hazeMat.SetColor(SpawnBoltFX.ColorId, new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 1f));
        }

        var hazeGO = new GameObject("haze");
        hazeGO.transform.SetParent(transform, false);
        haze = SpawnBoltFX.NewLR(hazeGO, hazeMat, 27);
        haze.positionCount = n;
        haze.SetPositions(basePts);   // the aura rides the smooth backbone, not the crackle

        main = SpawnBoltFX.NewLR(gameObject, mainMat, 29);
        main.positionCount = n;

        peakW = Mathf.Min(0.6f, 0.24f + 0.045f * Mathf.Sqrt(dist)) * intensity;

        int nb = Random.Range(1, 4);
        for (int k = 0; k < nb; k++)
        {
            var bg = new GameObject("branch");
            bg.transform.SetParent(transform, false);
            var blr = SpawnBoltFX.NewLR(bg, mainMat, 28);
            blr.positionCount = BRANCH_PTS;
            branches.Add(blr);
            branchU.Add(Random.Range(0.2f, 0.8f));
            branchSide.Add(Random.value < 0.5f ? -1f : 1f);
            branchAng.Add(Random.Range(25f, 55f));
            branchLen.Add(dist * Random.Range(0.16f, 0.32f));
        }

        frame = Random.Range(0, SpawnBoltFX.BoltFrames().Length);
        ReRoll();
        ApplyWidths();   // fully dressed before its first render — the strike is instantaneous
    }

    void ReRoll()
    {
        rerollT = REROLL;
        Texture2D[] frames = SpawnBoltFX.BoltFrames();
        frame = (frame + Random.Range(1, frames.Length)) % frames.Length;   // never the same twice
        mainMat.SetTexture(SpawnBoltFX.MainTexId, frames[frame]);
        mainMat.SetTexture(SpawnBoltFX.EmissionId, frames[frame]);
        flicker = Random.Range(0.82f, 1.15f);

        // smoothed noise wander — white noise per point reads fuzzy, two blur passes make it kink
        int n = basePts.Length;
        float amp = 0.08f + 0.05f * Mathf.Sqrt(dist);
        var noise = new float[n];
        for (int i = 1; i < n - 1; i++) noise[i] = Random.Range(-1f, 1f);
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < n - 1; i++)
                noise[i] = (noise[i - 1] + noise[i] * 2f + noise[i + 1]) * 0.25f;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            float env = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.6f);   // endpoints stay pinned
            pts[i] = basePts[i] + (Vector3)(perp * (noise[i] * amp * env));
        }
        main.SetPositions(pts);

        for (int k = 0; k < branches.Count; k++) BuildBranch(k);
    }

    // A fork: leaves the live path at its anchor, curls away from the main stroke as it goes.
    void BuildBranch(int k)
    {
        LineRenderer blr = branches[k];
        if (!blr.enabled) return;
        int n = pts.Length;
        int i = Mathf.Clamp(Mathf.RoundToInt(branchU[k] * (n - 1)), 1, n - 2);
        Vector2 d = ((Vector2)(pts[i + 1] - pts[i - 1])).normalized;
        d = SpawnBoltFX.Rot(d, branchSide[k] * (branchAng[k] + Random.Range(-8f, 8f)));
        Vector2 bperp = new Vector2(-d.y, d.x);
        Vector2 p = pts[i];
        float step = branchLen[k] / (BRANCH_PTS - 1);
        for (int j = 0; j < BRANCH_PTS; j++)
        {
            blr.SetPosition(j, p);
            d = SpawnBoltFX.Rot(d, branchSide[k] * Random.Range(2f, 9f));
            p += d * step + bperp * (Random.Range(-1f, 1f) * step * 0.35f);
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= LIFE) { Destroy(gameObject); return; }
        rerollT -= Time.deltaTime;
        if (rerollT <= 0f) ReRoll();
        ApplyWidths();
    }

    void ApplyWidths()
    {
        float env = t < ATTACK
            ? Mathf.Lerp(1.5f, 1f, t / ATTACK)
            : Mathf.Pow(1f - (t - ATTACK) / (LIFE - ATTACK), 1.55f);
        float w = peakW * flicker * env;
        main.widthMultiplier = w;
        haze.widthMultiplier = w * 3f;

        float bl = t / BRANCH_LIFE;
        float bw = bl >= 1f ? 0f : w * 0.45f * Mathf.Pow(1f - bl, 1.2f);
        foreach (LineRenderer blr in branches)
        {
            if (bw <= 0f) blr.enabled = false;
            else blr.widthMultiplier = bw;
        }
    }

    void OnDestroy()
    {
        if (mainMat != null) Destroy(mainMat);
        if (hazeMat != null) Destroy(hazeMat);
    }
}
