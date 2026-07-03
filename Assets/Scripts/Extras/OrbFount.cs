using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ALLY-ONLY "orb fount" interactive WITH CHARACTER. A squat little bowl-spirit that the player SHOOTS to
/// harvest: every ally round that strikes it makes it squeal, bob and spit a handful of glowing motes —
/// the number scaling with the damage of the round. The motes always launch AWAY from the player, so the
/// reward is something you have to chase; each mote, when it finally settles (or the player catches it),
/// crystallises into a real collectible orb of the fount's colour. It is only briefly generous: it bleeds
/// HP from the moment it spawns and wilts away after ~10 seconds.
///
/// FOUR colour variants share this one script (set <see cref="variant"/>); they differ only in how their
/// motes travel and what they leave in their wake — the catch that balances the free orbs:
///   • WHITE  — a clean ballistic scatter, no strings attached.
///   • GREEN  — motes smear a SLOWING trail; anything that wades through it is bogged down.
///   • BLUE   — motes spit slow ARCING bolts back over their shoulder that STUN the player on contact.
///   • RED    — motes drop rooting + damage MINES (2 HP, shootable, 10s) along their path.
///
/// Reward orbs go through GS.CallSpawnOrbs (white=general, green=druid, blue=engineer, red=cult). The CC /
/// mine effects reuse the slow/stun/root CCs and the LifeScript damage path. The fount's body + face are
/// baked in code; the motes/trails/bolts/mines are lightweight runtime objects (NOT wave 'alives', so they
/// never hold the wave open). Detection is a TRIGGER on the Walls layer filtered to ally projectiles.
/// </summary>
public class OrbFount : WaveExtra
{
    public enum FountColor { White, Green, Blue, Red }

    [Header("Fount")]
    public FountColor variant = FountColor.White;
    [Tooltip("Seconds of generosity: HP bleeds from full to zero over this, then it wilts.")]
    public float lifeTime = 10f;
    [Tooltip("Motes spat per point of incoming damage (clamped by maxMotesPerHit).")]
    public float orbsPerDamage = 1.3f;
    public int maxMotesPerHit = 6;
    public float moteSpeed = 6.5f;
    [Tooltip("Half-angle (degrees) of the away-from-player fan the motes scatter into.")]
    public float moteSpread = 38f;

    static Sprite[,] _frames;     // [variant, blink] baked once, shared across instances

    SpriteRenderer sr;
    float hp = 1f;                // normalized; bleeds to 0 over lifeTime
    float phase, flash, hop;
    int colorIndex;
    Color ramp1, ramp2, ramp3;

    // ---- palette plumbing -------------------------------------------------------------------------------
    void ResolvePalette()
    {
        switch (variant)
        {
            case FountColor.Green: colorIndex = 1; ramp1 = G2; ramp2 = G3; ramp3 = G4; break;
            case FountColor.Blue:  colorIndex = 2; ramp1 = B2; ramp2 = B3; ramp3 = B4; break;
            case FountColor.Red:   colorIndex = 3; ramp1 = R2; ramp2 = R3; ramp3 = R4; break;
            default:               colorIndex = 0; ramp1 = W2; ramp2 = W3; ramp3 = W4; break;
        }
    }

    void Awake()
    {
        ResolvePalette();
        sr = GetComponent<SpriteRenderer>();
        if (_frames == null) _frames = new Sprite[4, 2];
        int v = (int)variant;
        if (_frames[v, 0] == null) { _frames[v, 0] = BakeBody(variant, false); _frames[v, 1] = BakeBody(variant, true); }
        if (sr != null)
        {
            sr.sprite = _frames[v, 0];
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(ramp3, 1.8f);
        maxLifetime = Mathf.Max(maxLifetime, lifeTime + 2f);    // watchdog backstop above the natural wilt
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        flash = Mathf.Max(0f, flash - Time.deltaTime * 3.5f);
        hop = Mathf.Max(0f, hop - Time.deltaTime * 2.2f);
        hp -= Time.deltaTime / Mathf.Max(0.5f, lifeTime);       // bleed away its generosity

        // --- personality: a breathing bob, a sproingy hop on harvest, and a periodic blink ---
        int v = (int)variant;
        bool blink = Mathf.Repeat(phase, 2.7f) < 0.11f;         // a quick eyes-shut every couple seconds
        if (sr != null && _frames != null) sr.sprite = _frames[v, blink ? 1 : 0];
        float breath = 0.5f + 0.5f * Mathf.Sin(phase * 2.4f);
        float squashY = 1f + 0.06f * breath + 0.32f * hop + 0.18f * flash;
        float squashX = 1f - 0.04f * breath - 0.16f * hop + 0.10f * flash;
        transform.localScale = Vector3.Scale(baseScale * popK, new Vector3(squashX, squashY, 1f));
        transform.rotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(phase * 1.7f));   // gentle sway
        SetGlow((0.5f + 1.1f * Mathf.Clamp01(hp)) * (0.7f + 0.3f * breath) + 2.5f * flash);
        if (sr != null) sr.color = Color.Lerp(Color.Lerp(new Color(0.6f, 0.62f, 0.62f), Color.white, Mathf.Clamp01(hp)), Color.white, flash);

        if (hp <= 0f) Despawn();                                 // wilts gracefully (base shrink-out)
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null) return;
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p == null || !p.CompareTag("Allies")) return;       // only the player's fire harvests it
        Harvest(Mathf.Abs(p.damage));
    }

    void Harvest(float damage)
    {
        flash = 1f; hop = 1f;
        var ps = Burst();
        EmitBurst(ps, transform.position, 8, new[] { ramp3, ramp2 }, 1.2f, 3.4f, 0.3f, 0.6f, 0.12f, 0.24f);

        int n = Mathf.Clamp(Mathf.RoundToInt(damage * orbsPerDamage), 1, maxMotesPerHit);
        Vector2 home = transform.position;
        Vector2 away = (home - (Vector2)PlayerPos());
        float baseDeg = away.sqrMagnitude > 1e-4f ? Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg : Random.Range(0f, 360f);

        for (int i = 0; i < n; i++)
        {
            float deg = baseDeg + Random.Range(-moteSpread, moteSpread);
            Vector2 vel = GS.VTheta(deg) * (moteSpeed * Random.Range(0.8f, 1.15f));
            var go = new GameObject("FountMote");
            go.transform.position = home + GS.VTheta(deg) * 0.3f;
            go.transform.SetParent(GS.FindParent(GS.Parent.misc), true);
            go.AddComponent<FountMote>().Init(variant, vel, ramp2, ramp3, colorIndex);
        }
    }

    static Vector2 PlayerPos() => GS.CS() != null ? (Vector2)GS.CS().position : Vector2.zero;

    // ---- shared CC application (Unit -> StatusManager; else raw ActionScript), mirrors EmberSnare --------
    public static void ApplyCC(Transform t, string cc, float dur, float val)
    {
        if (t == null) return;
        var u = t.GetComponentInParent<Unit>();
        if (u != null) GS.Stat(u, cc, dur, val);
        else { var a = t.GetComponentInParent<ActionScript>(); if (a != null) a.AddCC(cc, dur, val); }
    }

    // ---- a small glowing orb sprite, cached per (core,rim) -----------------------------------------------
    static readonly Dictionary<int, Sprite> _orbCache = new Dictionary<int, Sprite>();
    public static Sprite OrbSprite(Color core, Color rim)
    {
        int key = (core.GetHashCode() * 397) ^ rim.GetHashCode();
        if (_orbCache.TryGetValue(key, out var s)) return s;
        s = BakeSprite(48, 0.5f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float f = Fill(r - 0.78f, 2f / 48f);
            if (f <= 0f) return new Color(0, 0, 0, 0);
            Color c = Color.Lerp(core, rim, Mathf.Clamp01(r / 0.78f));
            float ring = Mathf.Abs(r - 0.78f) < 0.12f ? 1f : 0f;
            return A(Color.Lerp(c, Outline, ring * 0.5f), f);
        });
        _orbCache[key] = s;
        return s;
    }

    // ----- baked art: a purple bowl-spirit with a bright pool + a little face (open / blinking) -----------
    static Sprite BakeBody(FountColor variant, bool blink)
    {
        Color p1, p2, p3;
        switch (variant)
        {
            case FountColor.Green: p1 = G2; p2 = G3; p3 = G4; break;
            case FountColor.Blue:  p1 = B2; p2 = B3; p3 = B4; break;
            case FountColor.Red:   p1 = R2; p2 = R3; p3 = R4; break;
            default:               p1 = W2; p2 = W3; p3 = W4; break;
        }
        const float aa = 2f / 128f;
        return BakeSprite(128, 1.2f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color col = new Color(0, 0, 0, 0);

            // bowl: a purple chalice — a rounded cup, fuller at the base
            float cup = r - (0.78f - 0.12f * Mathf.Clamp01(ny));     // narrower toward the top rim
            float cf = Fill(cup, aa);
            if (cf > 0f)
            {
                Color body = Color.Lerp(D1c, D1a, Mathf.Clamp01((r) / 0.78f));
                float ring = Mathf.Abs(cup) < 0.06f ? 1f : 0f;
                col = Over(col, A(Color.Lerp(body, Outline, ring), cf));
            }

            // the luminous pool sitting in the cup (element colour), domed bright at its centre
            float pool = r - 0.5f;
            float pf = Fill(pool, aa);
            if (pf > 0f && ny > -0.15f)
            {
                Color glow = Color.Lerp(p3, p1, Mathf.Clamp01(r / 0.5f));
                col = Over(col, A(glow, pf * Mathf.Clamp01((ny + 0.15f) / 0.5f)));
            }

            // --- the face, baked into the pool: two eyes + a small mouth ---
            float eyeY = 0.18f;
            float eyeR = blink ? 0.0f : 0.075f;
            // eyes (dark outline pips); when blinking, draw thin closed lids instead
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 e = new Vector2(s * 0.17f, eyeY);
                if (blink)
                {
                    if (Mathf.Abs(ny - eyeY) < 0.018f && Mathf.Abs(nx - e.x) < 0.085f)
                        col = Over(col, A(Outline, 0.95f));
                }
                else
                {
                    float d = (new Vector2(nx, ny) - e).magnitude;
                    if (d < eyeR) col = Over(col, A(Outline, 0.95f));
                    if (d < eyeR && (new Vector2(nx, ny) - (e + new Vector2(0.02f, 0.025f))).magnitude < 0.03f)
                        col = Over(col, A(Color.white, 0.9f));          // catch-light
                }
            }
            // mouth: a small smile arc
            if (ny < 0.05f && ny > -0.06f && Mathf.Abs(nx) < 0.12f)
            {
                float mouth = Mathf.Abs((ny + 0.02f) + 0.18f * (nx * nx) / 0.014f);
                if (mouth < 0.02f) col = Over(col, A(Outline, 0.9f));
            }
            return col;
        });
    }
}

// =====================================================================================================
// Runtime-only helpers (instantiated via AddComponent, never serialised into a prefab, so they need no
// matching file name). Each is self-cleaning and lives outside SpawnManager.alives.
// =====================================================================================================

/// <summary>A harvested mote: launches away from the player, decelerates, then crystallises into a real
/// collectible orb. Per-variant it leaves a hazard in its wake (slow trail / stun bolts / mines).</summary>
public class FountMote : MonoBehaviour
{
    OrbFount.FountColor variant;
    Vector2 vel;
    Color core, rim;
    int colorIndex;
    SpriteRenderer sr;
    float age, life = 1.7f, sideTimer;
    int sideDrops;
    bool settled;

    public void Init(OrbFount.FountColor v, Vector2 velocity, Color coreC, Color rimC, int orbColorIndex)
    {
        variant = v; vel = velocity; core = coreC; rim = rimC; colorIndex = orbColorIndex;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = OrbFount.OrbSprite(rim, core);
        var m = Resources.Load<Material>("Sprite-Unlit-Default");
        if (m != null) sr.sharedMaterial = m;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = 7;
        transform.localScale = Vector3.one * 0.7f;
    }

    void Update()
    {
        if (settled) return;
        float dt = Time.deltaTime;
        age += dt;

        // travel: ballistic with drag; white curls a touch for a livelier scatter
        if (variant == OrbFount.FountColor.White)
            vel = vel.Rotated(35f * dt);                         // gentle curl
        vel = Vector2.Lerp(vel, Vector2.zero, dt * 1.8f);        // drag
        transform.position += (Vector3)(vel * dt);
        transform.localScale = Vector3.one * (0.7f + 0.12f * Mathf.Sin(age * 18f));   // shimmer

        // per-variant wake
        sideTimer += dt;
        switch (variant)
        {
            case OrbFount.FountColor.Green:
                if (sideTimer >= 0.11f) { sideTimer = 0f; SpawnSlowPatch(); }
                break;
            case OrbFount.FountColor.Blue:
                if (sideTimer >= 0.32f && sideDrops < 5) { sideTimer = 0f; sideDrops++; SpawnStunBolt(); }
                break;
            case OrbFount.FountColor.Red:
                if (sideTimer >= 0.5f && sideDrops < 3) { sideTimer = 0f; sideDrops++; SpawnMine(); }
                break;
        }

        // collected by the player, or slowed to a settle, or timed out -> crystallise into a real orb
        Vector2 pp = GS.CS() != null ? (Vector2)GS.CS().position : (Vector2)transform.position;
        bool caught = ((Vector2)transform.position - pp).sqrMagnitude < 0.5f * 0.5f;
        if (caught || age >= life || vel.sqrMagnitude < 1.1f * 1.1f) Settle();
    }

    void SpawnSlowPatch()
    {
        var go = new GameObject("SlowPatch");
        go.transform.position = transform.position;
        go.transform.SetParent(transform.parent, true);
        go.AddComponent<FountSlowPatch>().Init(rim);
    }

    void SpawnStunBolt()
    {
        var go = new GameObject("StunBolt");
        go.transform.position = transform.position;
        go.transform.SetParent(transform.parent, true);
        // launched back over the mote's shoulder, with a sideways arc
        Vector2 back = (-vel.normalized).Rotated(GS.PlusMinus() * 28f);
        go.AddComponent<FountStunBolt>().Init(back * 3.2f, rim);
    }

    void SpawnMine()
    {
        var go = new GameObject("FountMine");
        go.transform.position = transform.position;
        go.transform.SetParent(transform.parent, true);
        go.AddComponent<FountMine>().Init(rim, core);
    }

    void Settle()
    {
        if (settled) return;
        settled = true;
        GS.CallSpawnOrbs(transform.position, OneOrb(colorIndex));   // the actual reward, free to scoop
        Shockwave.Spawn(transform.position, 0.5f, 0.01f, 0.2f);     // a small self-cleaning crystallise pop
        Destroy(gameObject);
    }

    static int[] OneOrb(int idx)
    {
        var a = new int[4];
        a[Mathf.Clamp(idx, 0, 3)] = 1;
        return a;
    }
}

/// <summary>GREEN wake: a fading disc that slows anything wading through it (polled, no collider needed).</summary>
public class FountSlowPatch : MonoBehaviour
{
    static readonly Collider2D[] buf = new Collider2D[24];
    static int mask = -1;
    SpriteRenderer sr;
    Color tint;
    float age, life = 1.5f, radius = 0.7f, tick;

    public void Init(Color c)
    {
        if (mask < 0) mask = LayerMask.GetMask("Enemy Units", "Ally Units", "Character");
        tint = c;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = WaveExtra.HaloSprite;
        var m = Resources.Load<Material>("Sprite-Unlit-Default");
        if (m != null) sr.sharedMaterial = m;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = 3;
        transform.localScale = Vector3.one * (radius * 2f);
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = 1f - age / life;
        if (sr != null) sr.color = WaveExtra.A(tint, 0.45f * Mathf.Clamp01(k));
        tick += Time.deltaTime;
        if (tick >= 0.1f)
        {
            tick = 0f;
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = mask, useTriggers = true };
            int n = Physics2D.OverlapCircle(transform.position, radius, filter, buf);
            for (int i = 0; i < n; i++)
                if (buf[i] != null) OrbFount.ApplyCC(buf[i].transform, "slow", 0.35f, 0.55f);
        }
        if (age >= life) Destroy(gameObject);
    }
}

/// <summary>BLUE wake: a slow arcing bolt that stuns the player on contact (polled distance check).</summary>
public class FountStunBolt : MonoBehaviour
{
    SpriteRenderer sr;
    Vector2 vel;
    Color tint;
    float age, life = 2f;

    public void Init(Vector2 v, Color c)
    {
        vel = v; tint = c;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = OrbFount.OrbSprite(c, c);
        var m = Resources.Load<Material>("Sprite-Unlit-Default");
        if (m != null) sr.sharedMaterial = m;
        sr.sortingLayerName = "Power Ups";
        sr.sortingOrder = 6;
        transform.localScale = Vector3.one * 0.5f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        vel = vel.Rotated(120f * dt);                 // lazy arc
        vel = Vector2.Lerp(vel, Vector2.zero, dt * 0.6f);
        transform.position += (Vector3)(vel * dt);
        if (sr != null) sr.color = WaveExtra.A(tint, Mathf.Clamp01(1f - age / life));

        var cs = GS.CS();
        if (cs != null && ((Vector2)cs.position - (Vector2)transform.position).sqrMagnitude < 0.5f * 0.5f)
        {
            if (GS.AS != null) GS.AS.AddCC("stun", 0.7f, 0f);
            Shockwave.Spawn(transform.position, 0.6f, 0.012f, 0.25f);
            Destroy(gameObject);
            return;
        }
        if (age >= life) Destroy(gameObject);
    }
}

/// <summary>RED wake: a rooting, damaging mine. Roots + ticks damage on anything standing on it, lasts 10s,
/// and is shootable (2 HP) so the player can clear it.</summary>
public class FountMine : MonoBehaviour
{
    static readonly Collider2D[] buf = new Collider2D[24];
    static int victimMask = -1;
    SpriteRenderer sr;
    Color core, rim;
    float age, life = 10f, tick, hp = 2f;
    bool dead;

    public void Init(Color rimC, Color coreC)
    {
        if (victimMask < 0) victimMask = LayerMask.GetMask("Enemy Units", "Ally Units", "Character");
        rim = rimC; core = coreC;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.useFullKinematicContacts = true;
        int walls = LayerMask.NameToLayer("Walls");
        if (walls >= 0) gameObject.layer = walls;       // overlaps both projectile factions for being shot
        var col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true; col.radius = 0.4f;

        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = OrbFount.OrbSprite(core, rim);
        var m = Resources.Load<Material>("Sprite-Unlit-Default");
        if (m != null) sr.sharedMaterial = m;
        sr.sortingLayerName = "Buildings";
        sr.sortingOrder = 2;
        transform.localScale = Vector3.one * 0.55f;
    }

    void Update()
    {
        if (dead) return;
        age += Time.deltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(age * 6f);
        transform.localScale = Vector3.one * (0.55f + 0.08f * pulse);
        if (sr != null) sr.color = Color.Lerp(WaveExtra.A(core, 1f), WaveExtra.A(rim, 1f), pulse);

        tick += Time.deltaTime;
        if (tick >= 0.35f)
        {
            tick = 0f;
            var filter = new ContactFilter2D { useLayerMask = true, layerMask = victimMask, useTriggers = true };
            int n = Physics2D.OverlapCircle(transform.position, 0.55f, filter, buf);
            for (int i = 0; i < n; i++)
            {
                if (buf[i] == null) continue;
                OrbFount.ApplyCC(buf[i].transform, "root", 0.4f, 0f);
                var ls = buf[i].GetComponentInParent<LifeScript>();
                if (ls != null && !ls.hasDied) ls.Change(-1f, 3);     // 3 = red dmgType
            }
        }
        if (age >= life) Pop(false);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dead || other == null) return;
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p == null || !p.CompareTag("Allies")) return;          // cleared only by the player's fire
        hp -= Mathf.Max(0.5f, Mathf.Abs(p.damage));
        if (hp <= 0f) Pop(true);
    }

    void Pop(bool shot)
    {
        if (dead) return;
        dead = true;
        Shockwave.Spawn(transform.position, shot ? 0.9f : 0.7f, 0.014f, 0.28f);   // self-cleaning detonation
        Destroy(gameObject);
    }
}
