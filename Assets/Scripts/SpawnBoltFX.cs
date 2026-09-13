using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The enemy-spawn strike: a thin thread of era-coloured light from whatever spawned an enemy
/// (base ember core, dungeon spawner, pocket core) into the enemy as it materialises. Spawns from
/// the same source inside <see cref="CHAIN_WINDOW"/> leave from the PREVIOUS enemy instead of the
/// source, so a burst reads as one running chain — but each chain only carries
/// <see cref="MAX_CHAIN_CREDITS"/> credits' worth of enemy (small 1, medium 3, large 6, by measured
/// size) and runs at most <see cref="MAX_CHAIN_TIME"/> from its first strike, after which the next
/// spawn re-strikes from the source — as it also does when the next enemy lands more than
/// <see cref="MAX_HOP_DIST"/> from the last one, so a chain stays a local cluster rather than
/// leaping the arena. What a chain has spent DECAYS as it ages (<see cref="CHAIN_CREDIT_DECAY"/>
/// credits across that life), so a slow trickle keeps riding one thread while a rapid burst fills
/// the budget and splits off a new one. Budgeting by weight rather than by hop count keeps the wave
/// legible: seven stragglers can ride one thread, a big one nearly fills a thread by itself, and a
/// trickle of spawns can't keep one chain creeping across the map indefinitely.
///
/// MINIMAL BY DESIGN: no crackle, no fork branches, no spark particles. The motion is carried by
/// the SHADER — the hot head is baked into the strip texture at u = 1 and rides the line's
/// stretched UVs as it grows — and by width/brightness envelopes, never by rebuilt geometry.
///
/// The strike also OWNS THE ENEMY'S ENTRANCE: pass the spawned GameObject to <see cref="Chain"/>
/// and the arrival ring is sized to it while the unit itself stays unseen until the thread lands
/// (see <see cref="SpawnStrikeMaterialise"/>).
///
/// Single-element: everything wears the era colour off GS.MatByEra(superBright) — the Glow Unlit
/// graph, so colour lives in `thecolor` (HDR, blooms), alpha rides _MainTex and vertex colour is
/// inert (same contract as CableFlowTint). Fades are width collapse + `thecolor` dimming, never
/// colour alpha (the shader ignores it).
///
/// GEOMETRY RULE — the old bolt spammed "Invalid AABB" / non-finite sort distances: LineRenderers
/// here are LOCAL space (the arena sits ~1000 units from the origin, so world-space points lose
/// the precision that short segments need) with NO cap/corner vertices, and no two consecutive
/// points may coincide (see <see cref="MIN_SEG"/>). Keep it that way in anything added here.
/// </summary>
public static class SpawnBoltFX
{
    const float CHAIN_WINDOW = 0.5f;    // spawns closer together than this leave from the previous enemy
    const float MAX_CHAIN_TIME = 1.5f;  // ...and no chain runs longer than this from its first strike
    const float MAX_HOP_DIST = 3f;      // ...and a hop never reaches further than this to the next enemy

    // ---- chain budget ------------------------------------------------------------------------
    // A chain carries CREDITS, not a hop count, so its length reads by WEIGHT: seven scrappy little
    // things can ride one thread, but a big one all but fills it on its own. When the next enemy
    // would take the running total past the budget, the source throws a fresh thread instead.
    /// <summary>Most credits one chain may carry at any instant.</summary>
    public const int MAX_CHAIN_CREDITS = 7;
    public const int CREDITS_SMALL = 1, CREDITS_MEDIUM = 3, CREDITS_LARGE = 6;
    /// <summary>Credits a chain sheds over its whole life — what it has spent decays as it ages, so
    /// a drawn-out trickle keeps riding one thread while a rapid burst fills the budget and splits.
    /// Charged PER GAP (against the previous strike, not the chain's start): the gaps sum to the
    /// chain's age, so a chain that runs the full MAX_CHAIN_TIME sheds exactly this much. Decaying
    /// from the start instead re-bills the same elapsed time at every hop, and a chain evaporates
    /// its debt several times over.</summary>
    public const float CHAIN_CREDIT_DECAY = 3.5f;
    static float DecayPerSecond => CHAIN_CREDIT_DECAY / MAX_CHAIN_TIME;
    // Measured against the live roster: smalls run 0.23–0.38 (Patroller/Quader/Shooter/Sower/
    // Spinner/Waggler), mediums 0.50–0.64 (Scratcher/BombWinger), larges 0.95–1.00 (Jumper/Crosser).
    const float MEDIUM_RADIUS = 0.45f, LARGE_RADIUS = 0.85f;
    const float MAX_BOLT_DIST = 30f;    // sanity cap (arena spawns can sit across the map) — arrival only
    const float MIN_BOLT_DIST = 0.15f;  // closer than this there's no thread worth drawing

    /// <summary>Shortest allowed gap between consecutive LineRenderer points — below this the
    /// strip geometry degenerates and Unity reports invalid AABBs.</summary>
    internal const float MIN_SEG = 0.02f;

    /// <summary>Half-width of a nondescript unit — what the arrival ring is sized against when
    /// there's no enemy to measure.</summary>
    internal const float DEFAULT_RADIUS = 0.5f;

    /// <summary>Seconds between a thread leaving its source and LANDING — the beat the arrival
    /// ring plays on, and the beat the enemy is allowed to appear on.</summary>
    public static float ArrivalDelay => SpawnStrikeThread.TRAVEL;

    internal static readonly int ColorId = Shader.PropertyToID("thecolor");
    internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    internal static readonly int EmissionId = Shader.PropertyToID("_Emission");

    struct ChainState { public Vector2 lastEnd; public float time; public float started; public float credits; }
    static readonly Dictionary<object, ChainState> chains = new Dictionary<object, ChainState>();

    static Texture2D threadTex, bandTex, dotTex;

    // Play-stop destroys the runtime textures but not these static refs (GS.qutting skips
    // teardown) — drop them ourselves or the next play mode renders with dead assets.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        chains.Clear();
        threadTex = null;
        bandTex = null;
        dotTex = null;
    }

    /// <summary>The spawn strike, taking the enemy itself: the arrival ring is sized to the unit,
    /// and the unit stays hidden until the thread lands on it (then pops in over
    /// <see cref="SpawnStrikeMaterialise.GROW"/>). This is the overload spawners should call —
    /// spawn the enemy first, then hand it over.</summary>
    public static void Chain(object source, Vector2 sourcePos, Vector2 enemyPos, GameObject enemy, float intensity = 1f)
    {
        float radius = MeasureRadius(enemy);            // measured BEFORE the unit is hidden/shrunk
        SpawnStrikeMaterialise.Play(enemy, ArrivalDelay);
        Chain(source, sourcePos, enemyPos, intensity, radius);
    }

    /// <summary>What one enemy costs a chain, by measured size: 1 small, 3 medium, 6 large.</summary>
    public static int SizeCredits(float unitRadius) =>
        unitRadius >= LARGE_RADIUS ? CREDITS_LARGE :
        unitRadius >= MEDIUM_RADIUS ? CREDITS_MEDIUM : CREDITS_SMALL;

    /// <summary>The spawn strike. source keys the chain (one chain per core/spawner/pocket);
    /// sourcePos is where a fresh chain leaves from; enemyPos is where the enemy materialised.</summary>
    public static void Chain(object source, Vector2 sourcePos, Vector2 enemyPos, float intensity = 1f,
                             float unitRadius = DEFAULT_RADIUS)
    {
        // hop off the previous enemy only while the chain is RECENT and still has room in its
        // credit budget — otherwise the source throws a new thread, so a big wave arrives as a
        // handful of short strikes rather than one endless daisy chain
        int cost = SizeCredits(unitRadius);
        Vector2 from = sourcePos;
        float credits = cost;
        float started = Time.time;
        if (source != null && chains.TryGetValue(source, out ChainState st))
        {
            // what the chain still has on the books, after the gap since the LAST strike pays
            // some of it back (see CHAIN_CREDIT_DECAY — per gap, never from the chain's start)
            float spent = Mathf.Max(0f, st.credits - DecayPerSecond * (Time.time - st.time));
            if (Time.time - st.time <= CHAIN_WINDOW              // the last hop was recent
                && Time.time - st.started <= MAX_CHAIN_TIME      // and the chain as a whole is still young
                && spent + cost <= MAX_CHAIN_CREDITS             // and it can still afford this one
                && Vector2.Distance(st.lastEnd, enemyPos) <= MAX_HOP_DIST)   // and the next one is near it
            {
                from = st.lastEnd;          // hop off the enemy the last thread landed on
                credits = spent + cost;
                started = st.started;       // the clock runs from the chain's FIRST strike, not this one
            }
        }

        if (source != null)
        {
            chains[source] = new ChainState
            {
                lastEnd = enemyPos,
                time = Time.time,
                started = started,
                credits = credits,
            };
            if (chains.Count > 32) Prune();
        }

        Strike(from, enemyPos, intensity, unitRadius);
    }

    /// <summary>Pocket spawns with no core of their own: the thread arrives from the nearest
    /// UNDISCOVERED core spot — the old core-hunting breadcrumb, dimmed so it stays a hint.</summary>
    public static void ChainToNearestCore(object source, Vector2 enemyPos, GameObject enemy = null)
    {
        float radius = MeasureRadius(enemy);
        SpawnStrikeMaterialise.Play(enemy, ArrivalDelay);
        Vector2? spot = MineDungeonManager.i != null ? MineDungeonManager.i.NearestCoreSpot(enemyPos) : null;
        if (spot == null) { Arrival(enemyPos, 0.5f, radius); return; }
        Chain(source, spot.Value, enemyPos, 0.5f, radius);
    }

    /// <summary>How big the thing being spawned actually is — the arrival ring is drawn around it,
    /// so a Sower and a boss can't share one canned circle. Visual bounds only (trails, lines and
    /// particle systems are excluded: they lie about a unit's footprint), collider as the fallback.</summary>
    public static float MeasureRadius(GameObject enemy)
    {
        if (enemy == null) return DEFAULT_RADIUS;

        bool any = false;
        Bounds b = new Bounds(enemy.transform.position, Vector3.zero);
        foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any)
            foreach (Collider2D c in enemy.GetComponentsInChildren<Collider2D>(true))
            {
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
        if (!any) return DEFAULT_RADIUS;
        return Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y), 0.12f, 6f);
    }

    /// <summary>One thread, no chain bookkeeping.</summary>
    public static void Strike(Vector2 from, Vector2 to, float intensity = 1f, float unitRadius = DEFAULT_RADIUS)
    {
        if (!Application.isPlaying) return;   // an edit-mode call would leave undying junk in the scene
        intensity = Mathf.Clamp(intensity, 0.15f, 2f);
        float dist = Vector2.Distance(from, to);
        if (dist > MAX_BOLT_DIST || dist < MIN_BOLT_DIST) { Arrival(to, intensity, unitRadius); return; }

        NewFX("SpawnStrikeThread", from).AddComponent<SpawnStrikeThread>().Init(from, to, intensity, unitRadius);
    }

    /// <summary>The ring pulse that marks the enemy's end of the strike. The thread fires this
    /// itself as it lands; it's called directly only when there's no thread to draw.</summary>
    public static void Arrival(Vector2 pos, float intensity = 1f, float unitRadius = DEFAULT_RADIUS)
    {
        if (!Application.isPlaying) return;
        SpawnStrikeRing.Ring(pos, intensity, unitRadius);
    }

    /// <summary>A runtime FX object. DontSave is not cosmetic: these live for a third of a second
    /// and are driven by Update, so one serialised into a scene would wake with null refs and never
    /// die (a saved scene full of undying strike objects is exactly how this bit once). Anything
    /// spawned by this file must go through here.</summary>
    internal static GameObject NewFX(string name, Vector2 at)
    {
        // DontSaveInEditor, not the full DontSave: it keeps these out of a saved scene (the bug
        // that once left 45 undying strike objects in World.unity) WITHOUT the survive-scene-load
        // behaviour the full flag drags along.
        var go = new GameObject(name) { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.position = new Vector3(at.x, at.y, 0f);
        return go;
    }

    static void Prune()
    {
        List<object> stale = null;
        foreach (var kv in chains)
            if (Time.time - kv.Value.time > CHAIN_WINDOW) (stale ??= new List<object>()).Add(kv.Key);
        if (stale != null) foreach (object k in stale) chains.Remove(k);
    }

    // ---- shared glow plumbing ----------------------------------------------------------------

    /// <summary>An instance of the era's superbright glow material wearing <paramref name="tex"/>
    /// (alpha rides the texture on this graph). Hands back the material's own HDR colour so the
    /// caller can dim it for the fade; destroy the material when the effect dies.</summary>
    internal static Material NewGlowMat(Texture2D tex, out Color baseCol, float dim = 1f)
    {
        Material src = SpawnManager.instance != null ? GS.MatByEra(GS.era, superBright: true) : null;
        var m = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
        m.SetTexture(MainTexId, tex);
        if (m.HasProperty(EmissionId)) m.SetTexture(EmissionId, tex);
        baseCol = m.HasProperty(ColorId) ? m.GetColor(ColorId) : GS.ColFromEra();
        baseCol = new Color(baseCol.r * dim, baseCol.g * dim, baseCol.b * dim, 1f);
        SetBrightness(m, baseCol, 1f);
        return m;
    }

    /// <summary>Fade/flare a glow material — brightness lives in the HDR `thecolor`, not alpha.</summary>
    internal static void SetBrightness(Material m, Color baseCol, float k)
    {
        if (m == null || !m.HasProperty(ColorId)) return;
        m.SetColor(ColorId, new Color(baseCol.r * k, baseCol.g * k, baseCol.b * k, 1f));
    }

    /// <summary>A LineRenderer set up the safe way: LOCAL space, no cap/corner vertices (see the
    /// geometry rule above), stretched UVs so a strip texture maps once along the whole line.</summary>
    internal static LineRenderer NewLR(GameObject go, Material m, int order)
    {
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = m;
        lr.sortingLayerName = "Power Ups";
        lr.sortingOrder = order;
        return lr;
    }

    // ---- the code-drawn strips ---------------------------------------------------------------

    /// <summary>The thread strip: a dim tail brightening into a hot head at u = 1, soft-edged
    /// across the width. Stretched along the line, so as the line grows the head rides its tip —
    /// the travel is the shader's job, not the geometry's.</summary>
    internal static Texture2D ThreadTex()
    {
        if (threadTex != null) return threadTex;
        const int W = 256, H = 16;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float u = x / (float)(W - 1);
            float head = 0.16f + 0.84f * Mathf.Pow(u, 5f);           // dim thread → hot tip
            head *= Mathf.Clamp01(u / 0.04f);                        // clean start at the source
            for (int y = 0; y < H; y++)
            {
                float v = (y / (float)(H - 1) - 0.5f) * 2f;          // -1 .. 1 across the width
                float edge = Mathf.Exp(-(v * v) / 0.34f);            // soft shoulders
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(head * edge) * 255f));
            }
        }
        threadTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        threadTex.SetPixels32(px);
        threadTex.Apply();
        return threadTex;
    }

    /// <summary>A soft radial dot (16×16) for glow particles — construction sparks, syphon streaks.</summary>
    internal static Texture2D DotTex()
    {
        if (dotTex != null) return dotTex;
        const int S = 16;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = 1f - Mathf.SmoothStep(0.15f, 1f, r);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        dotTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        dotTex.SetPixels32(px);
        dotTex.Apply();
        return dotTex;
    }

    /// <summary>A plain soft-edged band — even along its length, feathered across it. The arrival
    /// ring wears this.</summary>
    internal static Texture2D BandTex()
    {
        if (bandTex != null) return bandTex;
        const int W = 8, H = 32;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = (y / (float)(H - 1) - 0.5f) * 2f;
            float a = Mathf.Clamp01(Mathf.Exp(-(v * v) / 0.30f) * 1.05f);
            for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        bandTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        bandTex.SetPixels32(px);
        bandTex.Apply();
        return bandTex;
    }
}
