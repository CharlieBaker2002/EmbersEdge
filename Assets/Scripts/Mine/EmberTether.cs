using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The harvesting leash. When the player breaches a pocket, the EMBER DISC (Resources/EmberDisc strip —
/// dungeon-specific art: D1=EmberDisc, D2=EmberDiscD2, D3=EmberDiscD3, picked by GS.era same as
/// MineField's d1/d2/d3 tiles) is CAST — fishing-rod style, the ROD (Resources/EmberTether sprite 0) in
/// the player's hand — into the pocket centre, and the player is bound to the pocket: they can't move
/// further than maxLen from the disc. Killing the pocket's enemies feeds their souls into the disc (it spins fast through its
/// frames on every kill) and drains the era-coloured progress bar floating above it — the bar shows
/// how much is LEFT until the room is complete, its bleeding edge flaring bright on each kill.
/// In a BOSS room the same bar is the boss's HP bar instead (Pocket.BossLife), draining hit by hit. Once
/// EVERY enemy is dead the disc sends the pocket's EMBER off with the player (EmberStore.Hold — a new
/// currency, NOT orbs; 1 per pocket by default) and the tether releases. The held ember pays out
/// visibly on the return teleport (PortalScript), flying into whichever buildings want it. Tapping V unteth ers early (PortalScript
/// routes the V press here first), forfeiting the ember. When thrown onto a CORE the line turns
/// era-colour and CANNOT be broken until the core is claimed.
/// </summary>
public class EmberTether : MonoBehaviour
{
    public static EmberTether i;

    const int POINTS = 24;            // line segments
    const float CAST_TIME = 0.45f;    // fishing-rod cast duration
    const float BASE_WIDTH = 0.045f;
    const float PUSH = 0.3f;          // leash push strength (same as PocketConfinement)
    const float DISC_IDLE_FPS = 9f;   // the disc is ALWAYS animating; this is its resting rate
    const float DISC_KILL_SPEED = 5f; // kill spike: one fast lap through the whole strip (~0.4s)
    const float BAR_W = 1.15f;        // progress bar world width
    const float BAR_H = 0.11f;
    const float BAR_LIFT = 0.62f;     // bar height above the disc
    const float BLEED_TIME = 0.5f;    // how long the bright bleeding edge takes to catch the fill

    Pocket pocket;
    Vector2 anchor;                   // where the disc is planted (pocket centre, or the core)
    float maxLen;                     // leash length: max(player@spawn, furthest cavity point) + 1
    int emberValue = 1;               // ember sent home on completion (1 per pocket by default)
    bool coreLocked;                  // hooked on an active core — V cannot break it
    bool released;
    LineRenderer lr;
    float castT;
    Vector2 castFrom;                 // player position at cast start (the rod hand)
    float pulseT;                     // brief width pulse (absorb / denied-untether feedback)
    Color lineCol = new Color(1f, 1f, 1f, 0.45f);   // neutral until a core claims the line

    // rod + disc
    SpriteRenderer rod;               // EmberTether_0, in the player's hand, aimed at the disc
    SpriteRenderer disc;              // EmberDisc strip — always animating, era-glow material
    float discClock;                  // frame accumulator
    float discSpeed = 1f;             // spikes on kills, eases back to 1

    // progress bar (how much LEFT until the room is complete). Driven by POLLING the pocket's
    // RemainingPoints — the same truth the room-clear check uses — so phase/splitting enemies
    // (Discer etc.) firing extra death hooks can never confuse it.
    float totalPoints;                // authored enemy points of the pocket
    float lastFrac = 1f;              // last shown fraction (detects the drop of a kill)
    float bleedFrac = 1f;             // trailing bright edge (chases the true fraction down)
    float barPulseT;
    Transform barRoot;
    SpriteRenderer barBg, barFill, barBleed;

    static Sprite[] rodFrames;               // Resources/EmberTether strip (rod doesn't vary by dungeon)
    static Sprite[][] discFramesByEra = new Sprite[3][];   // EmberDisc / EmberDiscD2 / EmberDiscD3, lazy per era
    Sprite[] discFrames;                     // this tether's dungeon-specific disc strip
    static Sprite solidSprite;               // 1×1-unit white block for the bar
    static Sprite[] wispFrames;              // the Soul art, reused for the fuel wisps

    /// <summary>Cast the tether for a freshly-breached pocket. Replaces any previous tether.</summary>
    public static EmberTether Cast(Pocket p, Vector2 anchorPos, float maxLenP, float pocketPoints, int emberP)
    {
        if (i != null) i.Release();
        var go = new GameObject("EmberTether");
        go.transform.SetParent(p.transform, false);
        go.transform.position = anchorPos;
        var t = go.AddComponent<EmberTether>();
        t.pocket = p;
        t.anchor = anchorPos;
        t.maxLen = maxLenP;
        t.totalPoints = Mathf.Max(0f, pocketPoints);
        t.emberValue = Mathf.Max(1, emberP);
        t.castFrom = GS.CS() != null ? (Vector2)GS.CS().position : anchorPos;
        t.Build();
        i = t;
        return t;
    }

    /// <summary>
    /// V press router — PortalScript calls this FIRST. Order: a passive core near the player gets the
    /// tether thrown onto it; an active core line refuses to break; otherwise a plain tether unteth ers.
    /// Returns true when the press was consumed (so V doesn't also charge a teleport).
    /// </summary>
    public static bool HandleRecall()
    {
        // throw the tether onto a passive core (manual activation)
        if (MineDungeonManager.i != null && MineDungeonManager.i.activePocket != null &&
            MineDungeonManager.i.activePocket.TryActivateCore())
            return true;

        if (i == null || i.released) return false;
        if (i.coreLocked) { i.pulseT = 0.4f; return true; }   // bound to the core — the line won't give
        i.Release();                                          // untether: forfeit the ember
        return true;
    }

    /// <summary>Is the player currently bound (any live tether)?</summary>
    public static bool Tethered => i != null && !i.released;

    // ---- construction --------------------------------------------------------------------------

    void Build()
    {
        LoadArt();
        discFrames = DiscFramesForEra(GS.era);

        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = POINTS;
        lr.widthMultiplier = BASE_WIDTH;
        lr.numCapVertices = 2;
        // Sprites/Default is Always-Included in this project (MapManager/PocketConfinement use it).
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingLayerName = "Power Ups";
        lr.sortingOrder = 40;
        ApplyColor();

        disc = MakeSprite("Disc", discFrames.Length > 0 ? discFrames[0] : null, 45);
        // the ember disc glows in the era colour — same era-glow emissive the Souls/EmberParticles use
        disc.sharedMaterial = GS.MatByEra(GS.era, true, false, true);
        rod = MakeSprite("Rod", rodFrames.Length > 0 ? rodFrames[0] : null, 46);
        BuildBar();
    }

    SpriteRenderer MakeSprite(string n, Sprite s, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = order;
        return sr;
    }

    // The era-coloured "left until the room is complete" bar that floats above the disc: a dark back
    // plate, the era fill, and a white-hot BLEED segment that flares over freshly-drained bar and
    // catches down onto the fill edge after each kill.
    void BuildBar()
    {
        barRoot = new GameObject("Bar").transform;
        barRoot.SetParent(transform, false);
        barBg = MakeBarPart("bg", 41, new Color(0.07f, 0.07f, 0.09f, 0.85f));
        barBg.transform.localScale = new Vector3(BAR_W + 0.05f, BAR_H + 0.05f, 1f);
        Color era = GS.ColFromEra();
        barFill = MakeBarPart("fill", 42, new Color(era.r, era.g, era.b, 0.95f));
        barBleed = MakeBarPart("bleed", 43, Color.white);
        barRoot.gameObject.SetActive(totalPoints > 0f || (pocket != null && pocket.IsBossRoom));
        UpdateBar(anchor);   // initial full-bar layout
    }

    SpriteRenderer MakeBarPart(string n, int order, Color c)
    {
        var go = new GameObject(n);
        go.transform.SetParent(barRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SolidSprite();
        sr.color = c;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = order;
        return sr;
    }

    static Sprite SolidSprite()
    {
        if (solidSprite == null)
        {
            var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            var px = new Color32[16];
            for (int k = 0; k < px.Length; k++) px[k] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);   // = 1×1 unit
        }
        return solidSprite;
    }

    static void LoadArt()
    {
        if (rodFrames == null) rodFrames = LoadStrip("EmberTether", "EmberTether_");
    }

    // Dungeon-1/2/3 disc art (era N == dungeon N, same convention as MineField's d1/d2/d3 tiles):
    // era 0 = default EmberDisc, era 1 = EmberDiscD2, era 2+ = EmberDiscD3.
    static Sprite[] DiscFramesForEra(int era)
    {
        int idx = Mathf.Clamp(era, 0, 2);
        if (discFramesByEra[idx] == null)
        {
            string res = idx == 0 ? "EmberDisc" : idx == 1 ? "EmberDiscD2" : "EmberDiscD3";
            discFramesByEra[idx] = LoadStrip(res, res + "_");
        }
        return discFramesByEra[idx];
    }

    // Load a sliced strip from Resources in NUMERIC frame order (name sort would put _10 before _2),
    // skipping non-frame slices like the sheet's _Emission.
    static Sprite[] LoadStrip(string res, string prefix)
    {
        var all = Resources.LoadAll<Sprite>(res);
        var list = new List<(int n, Sprite s)>();
        foreach (var s in all)
            if (s.name.StartsWith(prefix) && int.TryParse(s.name.Substring(prefix.Length), out int n))
                list.Add((n, s));
        list.Sort((a, b) => a.n.CompareTo(b.n));
        var arr = new Sprite[list.Count];
        for (int k = 0; k < list.Count; k++) arr[k] = list[k].s;
        return arr;
    }

    void ApplyColor()
    {
        lr.startColor = lineCol;
        lr.endColor = new Color(lineCol.r, lineCol.g, lineCol.b, lineCol.a * 0.7f);
    }

    // ---- state changes -------------------------------------------------------------------------

    /// <summary>
    /// The tether is thrown onto a core: the disc re-flies to the core, the line takes the ERA COLOUR
    /// and locks — it cannot be broken (no untether, no leaving) until the core is collected.
    /// </summary>
    public void AttachToCore(Vector2 corePos, float newMaxLen)
    {
        castFrom = anchor;            // the disc whips from its old plant to the core
        anchor = corePos;
        castT = 0f;
        maxLen = Mathf.Max(maxLen, newMaxLen);
        coreLocked = true;
        lineCol = GS.ColFromEra();
        lineCol.a = 0.9f;
        ApplyColor();
        pulseT = 0.5f;
        SpinDisc();
    }

    /// <summary>An enemy of the tethered pocket died — its ember visibly syphons into the disc.
    /// (The bar itself drains via the RemainingPoints poll, not from this hook.)</summary>
    public void AbsorbSoul(Vector3 from, float points)
    {
        if (released) return;
        StartCoroutine(WispsTo(from));
    }

    // Live "left until the room is complete" fraction, straight from the pocket. In a BOSS room the
    // bar is the boss's HP bar instead — full until the boss appears, draining with its health.
    float Frac()
    {
        if (pocket == null) return 0f;
        if (pocket.IsBossRoom)
        {
            var boss = pocket.BossLife;
            if (boss == null) return 1f;   // not on the field yet — nothing drained
            return boss.hasDied ? 0f : Mathf.Clamp01(boss.hp / Mathf.Max(0.0001f, boss.maxHp));
        }
        if (totalPoints <= 0f) return 0f;
        return Mathf.Clamp01(pocket.RemainingPoints() / totalPoints);
    }

    void SpinDisc() => discSpeed = DISC_KILL_SPEED;

    /// <summary>
    /// Pocket fully cleared while still tethered: the pocket's EMBER (a new currency — orbs are
    /// untouched) joins the player for the ride home. It lands at base on the return teleport,
    /// each unit flying into a building that wants it. Then the tether releases.
    /// </summary>
    public void NotifyCleared()
    {
        if (released) return;
        coreLocked = false;
        EmberStore.Hold(emberValue);
        StartCoroutine(CompleteFX());
    }

    /// <summary>Cut the tether (untether / pocket reset / new cast). No ember.</summary>
    public void Release()
    {
        if (released) return;
        released = true;
        if (i == this) i = null;
        StartCoroutine(FadeOut(0.35f));
    }

    // ---- per-frame -----------------------------------------------------------------------------

    void Update()
    {
        if (released || lr == null) return;
        Transform ch = GS.CS();
        if (ch == null) return;

        Vector2 discPos = anchor;
        if (castT < CAST_TIME)   // the cast: the disc flies out of the rod in an arc
        {
            castT += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(castT / CAST_TIME));
            discPos = Vector2.Lerp(castFrom, anchor, u) + Vector2.up * (Mathf.Sin(u * Mathf.PI) * 0.8f);
        }

        // rod in the player's hand, aimed at the disc; the rope runs rod tip -> disc
        Vector2 pPos = ch.position;
        Vector2 aim = discPos - pPos;
        Vector2 dir = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.up;
        rod.transform.position = pPos + dir * 0.35f;
        rod.transform.up = dir;
        Vector2 rodTip = pPos + dir * 0.55f;

        disc.transform.position = discPos;
        DrawRope(discPos, rodTip);
        UpdateDisc();
        UpdateBar(discPos);

        if (pulseT > 0f)
        {
            pulseT -= Time.deltaTime;
            lr.widthMultiplier = BASE_WIDTH * (1f + Mathf.Max(0f, pulseT) * 2.5f);
        }
    }

    // The disc never stops turning; kills whip it into a fast lap that eases back to idle.
    void UpdateDisc()
    {
        if (discFrames.Length == 0) return;
        discSpeed = Mathf.MoveTowards(discSpeed, 1f, Time.deltaTime * 6f);
        discClock += Time.deltaTime * DISC_IDLE_FPS * discSpeed;
        disc.sprite = discFrames[(int)discClock % discFrames.Length];
    }

    // The bar floats above the disc showing how much is LEFT until the room is complete. It polls the
    // pocket's remaining points; when they drop (a kill) the fill snaps down instantly while the
    // white-hot bleed segment covers the freshly-lost span and catches down onto the new edge, the bar
    // pops and the disc whirs.
    void UpdateBar(Vector2 discPos)
    {
        if (barRoot == null || !barRoot.gameObject.activeSelf) return;
        barRoot.position = discPos + Vector2.up * BAR_LIFT;

        float frac = Frac();
        if (frac < lastFrac - 0.0005f)   // the room just got emptier
        {
            bleedFrac = Mathf.Max(bleedFrac, lastFrac);   // bright edge starts at the OLD level
            barPulseT = 1f;
            pulseT = Mathf.Max(pulseT, 0.25f);
            SpinDisc();
        }
        lastFrac = frac;
        bleedFrac = Mathf.MoveTowards(bleedFrac, frac, Time.deltaTime * Mathf.Max(0.2f, (bleedFrac - frac) / BLEED_TIME));

        if (barPulseT > 0f) barPulseT = Mathf.Max(0f, barPulseT - Time.deltaTime * 3f);
        float pop = 1f + barPulseT * 0.35f;
        barRoot.localScale = new Vector3(1f, pop, 1f);

        // fill: left-anchored era colour
        barFill.transform.localScale = new Vector3(BAR_W * frac, BAR_H, 1f);
        barFill.transform.localPosition = new Vector3(-BAR_W * 0.5f + BAR_W * frac * 0.5f, 0f, 0f);

        // bleed: the span between the new level and the trailing bright edge — hotter than the fill
        float span = Mathf.Max(0f, bleedFrac - frac);
        barBleed.enabled = span > 0.0005f;
        if (barBleed.enabled)
        {
            barBleed.transform.localScale = new Vector3(BAR_W * span, BAR_H, 1f);
            barBleed.transform.localPosition = new Vector3(-BAR_W * 0.5f + BAR_W * (frac + span * 0.5f), 0f, 0f);
            Color era = GS.ColFromEra();
            // white-hot right after the hit, cooling toward the era colour as it catches down
            barBleed.color = Color.Lerp(new Color(era.r, era.g, era.b, 0.9f), Color.white, Mathf.Clamp01(barPulseT * 1.5f + 0.35f));
        }
    }

    // Slack rope: sags by the unused length, with a slow ripple along it.
    void DrawRope(Vector2 a, Vector2 b)
    {
        float slack = Mathf.Max(0f, maxLen - Vector2.Distance(a, b)) * 0.06f + 0.04f;
        for (int k = 0; k < POINTS; k++)
        {
            float t = k / (POINTS - 1f);
            Vector2 p = Vector2.Lerp(a, b, t);
            float belly = Mathf.Sin(t * Mathf.PI);
            p += Vector2.down * (belly * slack);
            p.x += Mathf.Sin(Time.time * 2.1f + t * 9f) * 0.02f * belly;
            p.y += Mathf.Sin(Time.time * 1.7f + t * 7f) * 0.02f * belly;
            lr.SetPosition(k, p);
        }
    }

    void FixedUpdate()   // the leash — same push style as PocketConfinement
    {
        if (released) return;
        ActionScript AS = GS.AS;
        if (AS == null) return;
        Vector2 pos = AS.transform.position;
        if (Vector2.Distance(pos, anchor) > maxLen)
            AS.AddPush(PUSH * Time.fixedDeltaTime, false, anchor - pos);
    }

    // ---- visuals -------------------------------------------------------------------------------

    static Sprite[] WispFrames()
    {
        if (wispFrames == null)
        {
            var list = new List<Sprite>();
            for (int k = 0; k < 32; k++)
            {
                var s = Resources.Load<Sprite>($"Sprites/Soul/soul_{k}");
                if (s == null) break;
                list.Add(s);
            }
            wispFrames = list.ToArray();
        }
        return wispFrames;
    }

    // A small soul-swarm syphons from the corpse into the disc (the ember arriving).
    IEnumerator WispsTo(Vector3 from)
    {
        var frames = WispFrames();
        if (frames.Length == 0) yield break;
        const int K = 6;
        const float DUR = 0.9f;
        Material mat = GS.MatByEra(GS.era, true, false, true);   // era glow, same as Soul

        var host = new GameObject("TetherWisps");
        host.transform.position = from;
        var srs = new SpriteRenderer[K];
        var ctrl = new Vector3[K];
        Vector3 to = (Vector3)anchor - from;
        for (int k = 0; k < K; k++)
        {
            var w = new GameObject("wisp");
            w.transform.SetParent(host.transform, false);
            var sr = w.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = mat;
            sr.sprite = frames[Random.Range(0, frames.Length)];
            sr.sortingOrder = 100 + k;
            w.transform.localScale = Vector3.one * Random.Range(0.35f, 0.6f);
            srs[k] = sr;
            Vector3 mid = to * 0.5f;
            Vector3 dir = to.sqrMagnitude > 0.001f ? to.normalized : Vector3.up;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
            ctrl[k] = mid + perp * Random.Range(-0.35f, 0.35f) * to.magnitude * 0.5f;
        }
        for (float t = 0f; t < DUR; t += Time.deltaTime)
        {
            float u = t / DUR;
            float e = u * u;   // ease-in syphon
            for (int k = 0; k < K; k++)
            {
                float it = 1f - e;
                Vector3 p = it * it * Vector3.zero + 2f * it * e * ctrl[k] + e * e * to;
                srs[k].transform.localPosition = p;
                srs[k].color = new Color(1f, 1f, 1f, 1f - u * u);
            }
            yield return null;
        }
        Destroy(host);
    }

    // Completion: a bright surge runs the line while the disc spins its whole strip, then the ember
    // bursts away home and everything fades.
    IEnumerator CompleteFX()
    {
        released = true;
        if (i == this) i = null;
        Color bright = GS.ColFromEra();
        SpinDisc();
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            float u = t / 0.5f;
            lr.widthMultiplier = BASE_WIDTH * (1f + Mathf.Sin(u * Mathf.PI) * 3f);
            Color c = Color.Lerp(lineCol, new Color(bright.r, bright.g, bright.b, 1f), Mathf.Sin(u * Mathf.PI));
            lr.startColor = lr.endColor = c;
            UpdateDisc();
            UpdateBar(disc.transform.position);
            yield return null;
        }
        MineFX.EnemySpawnFlash(anchor);   // the send-off burst at the disc
        yield return StartCoroutine(FadeOut(0.6f));
    }

    IEnumerator FadeOut(float dur)
    {
        Color c0 = lr != null ? lr.startColor : Color.clear;
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        var cols = new Color[srs.Length];
        for (int k = 0; k < srs.Length; k++) cols[k] = srs[k].color;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float u = 1f - t / dur;
            if (lr != null)
            {
                lr.startColor = lr.endColor = new Color(c0.r, c0.g, c0.b, c0.a * u);
                lr.widthMultiplier = BASE_WIDTH * u;
            }
            for (int k = 0; k < srs.Length; k++)
                if (srs[k] != null) srs[k].color = new Color(cols[k].r, cols[k].g, cols[k].b, cols[k].a * u);
            yield return null;
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (i == this) i = null;
    }
}

/// <summary>
/// Death hook added to every pocket-spawned enemy: on death its ember (worth its authored price in
/// points) syphons into the live tether. Same registration pattern as SoulCollectOnDeath.
/// </summary>
public class TetherFuelOnDeath : MonoBehaviour, IOnDeath
{
    public float points;

    public void OnDeath()
    {
        if (EmberTether.i != null) EmberTether.i.AbsorbSoul(transform.position, points);
    }
}

/// <summary>
/// Shared one-shot dungeon VFX. EnemySpawnFlash = the ember flash every pocket/spawner enemy appears
/// in; CoreHintLine = the faint line from a spawning enemy toward the nearest undiscovered ember-core
/// spot (a breadcrumb for core-hunting). All code-built, single-element (era colour), unlit sprites.
/// </summary>
public static class MineFX
{
    static Material flashMat;

    static Material FlashMat()
    {
        if (flashMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            flashMat = new Material(sh);
        }
        return flashMat;
    }

    /// <summary>A brief burst of era-coloured embers (enemy materialising / tether send-off).</summary>
    public static void EnemySpawnFlash(Vector2 pos)
    {
        var go = new GameObject("EmberSpawnFlash");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // configure before playing

        Color col = GS.ColFromEra();   // ONE element only — the era ramp colour
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = col;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.08f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(col, 0.35f), new GradientColorKey(col, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colOver.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = FlashMat();
        r.sortingLayerName = "Power Ups";   // the VFX layer (same one Nova/Firestorm use)
        r.sortingOrder = 30;

        ps.Play();
        Object.Destroy(go, 1.2f);
    }

    /// <summary>
    /// A faint, fading line from a spawn flash toward the nearest UNDISCOVERED core pocket — points the
    /// player at ember cores without marking them on any map.
    /// </summary>
    public static void CoreHintLine(Vector2 from)
    {
        if (MineDungeonManager.i == null) return;
        Vector2? spot = MineDungeonManager.i.NearestCoreSpot(from);
        if (spot == null) return;
        HintLine(from, spot.Value);
    }

    /// <summary>The faint fading era-coloured line itself — also used by spawners to show which
    /// direction their enemies emerged from.</summary>
    public static void HintLine(Vector2 from, Vector2 to)
    {
        var go = new GameObject("HintLine");
        go.transform.position = from;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.widthMultiplier = 0.03f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingOrder = 6;   // just above the mine fog (5) so it reads through the dark
        Color c = GS.ColFromEra();
        go.AddComponent<FadingLine>().Init(lr, new Color(c.r, c.g, c.b, 0.12f), 2.5f);
    }
}

/// <summary>Fades a LineRenderer's alpha to zero then destroys it (no tween-pool pressure).</summary>
public class FadingLine : MonoBehaviour
{
    LineRenderer lr;
    Color c0;
    float dur, t;

    public void Init(LineRenderer line, Color col, float duration)
    {
        lr = line;
        c0 = col;
        dur = duration;
        lr.startColor = lr.endColor = c0;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= dur || lr == null) { Destroy(gameObject); return; }
        float u = 1f - t / dur;
        lr.startColor = lr.endColor = new Color(c0.r, c0.g, c0.b, c0.a * u);
    }
}
