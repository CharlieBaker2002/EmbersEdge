using System.Collections;
using UnityEngine;

/// <summary>
/// NEUTRAL interactive entity (blue element). A volatile crystal geode that belongs to no faction: ANY
/// projectile that passes through it — ally OR enemy — chips its durability, and enemy bodies grinding
/// through destabilise it too. When it breaks it shatters in a blue energy burst that damages + knocks back
/// every nearby unit on BOTH sides (it doesn't care whose side you're on) and scatters collectible orbs.
/// Tactical & neutral: shoot it when the pack clusters for a big blast + loot, but mind the splash.
///
/// Self-managed durability via a TRIGGER collider (the prefab sits on the Walls layer, which overlaps both
/// projectile factions + units) — kept off LifeScript precisely because the engine's projectile damage is
/// faction-gated. Spawned through the wave pipeline; self-destructs on shatter or at maxLifetime.
/// </summary>
public class CinderGeode : WaveExtra
{
    [Header("Geode")]
    public float durability = 14f;      // total "HP"; player bullets are the quick way down
    public float blastRadius = 2.6f;
    public float blastDamage = 16f;
    public float knockback = 95f;
    public float ramChip = 1.2f;        // durability lost per enemy body grind (throttled)
    public int[] loot = { 7, 0, 4, 0 }; // orbs scattered on shatter (general / druid / engineer / cult)

    static readonly Collider2D[] hitBuf = new Collider2D[128];
    static int enemyUnitsLayer = -1;
    static Sprite[] crackFrames;   // 0 = pristine … 3 = badly cracked; deterministic, shared across instances

    SpriteRenderer sr;
    float maxDur;
    float lastRam;
    float phase;

    void Awake()
    {
        if (enemyUnitsLayer < 0) enemyUnitsLayer = LayerMask.NameToLayer("Enemy Units");
        sr = GetComponent<SpriteRenderer>();
        maxDur = durability;
        if (crackFrames == null)
        {
            crackFrames = new Sprite[4];
            for (int i = 0; i < 4; i++) crackFrames[i] = BakeBody(i);
        }
        if (sr != null)
        {
            sr.sprite = crackFrames[0];
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(B4, 1.9f);
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        float frac = Mathf.Clamp01(durability / maxDur);
        float shimmer = 0.5f + 0.5f * Mathf.Sin(phase * 2.2f);
        SetGlow(Mathf.Lerp(1.8f, 0.8f, frac) + 0.7f * shimmer);   // brightens as it nears breaking
        if (sr != null) sr.color = Color.Lerp(new Color(1f, 1f, 1f), new Color(0.78f, 0.85f, 1f), 0.4f + 0.3f * shimmer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null) return;
        // A projectile of EITHER faction passing through chips it by its damage.
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p != null) { Chip(Mathf.Max(1f, Mathf.Abs(p.damage)), other.transform.position); return; }
        // An enemy body grinding through chips a little (throttled so leaning doesn't instantly pop it).
        if (Time.time - lastRam > 0.4f && other.gameObject.layer == enemyUnitsLayer)
        {
            lastRam = Time.time;
            Chip(ramChip, other.transform.position);
        }
    }

    void Chip(float amount, Vector2 where)
    {
        durability -= amount;
        int stage = Mathf.Clamp(3 - Mathf.CeilToInt(Mathf.Clamp01(durability / maxDur) * 3f), 0, 3);
        if (sr != null && crackFrames != null) sr.sprite = crackFrames[stage];
        var ps = Burst();
        EmitBurst(ps, where, 6, new[] { B4, B3 }, 1.2f, 3.2f, 0.25f, 0.5f, 0.1f, 0.2f);
        StartCoroutine(Knock());
        if (durability <= 0f) Shatter();
    }

    IEnumerator Knock()
    {
        Vector3 b = baseScale;
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.12f)
        {
            if (consumed) yield break;
            transform.localScale = b * (1f + 0.12f * Mathf.Sin(k * Mathf.PI));
            yield return null;
        }
        transform.localScale = b;
    }

    void Shatter()
    {
        if (consumed) return;
        consumed = true;
        Vector3 p = transform.position;

        // Faction-blind: hurt + shove every nearby unit on BOTH sides (spares buildings).
        int mask = LayerMask.GetMask("Ally Units", "Enemy Units", "Character");
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = mask, useTriggers = true };
        int n = Physics2D.OverlapCircle(p, blastRadius, filter, hitBuf);
        for (int i = 0; i < n; i++)
        {
            var col = hitBuf[i];
            if (col == null) continue;
            float fall = 1f - Mathf.Clamp01(((Vector2)((Vector3)col.transform.position - p)).magnitude / blastRadius);
            var ls = col.GetComponentInParent<LifeScript>();
            if (ls != null) ls.Change(-blastDamage * (0.35f + 0.65f * fall), 2); // 2 = blue dmgType
            var asc = col.GetComponentInParent<ActionScript>();
            if (asc != null && asc.pushable)
            {
                Vector2 dir = ((Vector2)((Vector3)col.transform.position - p)).normalized;
                if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;
                asc.AddPush(0.14f, true, dir * asc.mass * knockback * (0.35f + 0.65f * fall));
            }
        }

        GS.CallSpawnOrbs(p, loot);                 // loot reward, free for the player to scoop
        StartCoroutine(ShatterFX(p));
    }

    IEnumerator ShatterFX(Vector3 p)
    {
        var ps = Burst();
        EmitBurst(ps, p, 40, new[] { B4, B3, B2 }, 3.5f, 8f, 0.4f, 0.85f, 0.2f, 0.42f);  // shard spray
        EmitBurst(ps, p, 18, new[] { B3, B4 }, 0.3f, 2f, 0.6f, 1.1f, 0.14f, 0.3f);        // glittering dust
        Shockwave.Spawn(p, blastRadius, 0.02f, 0.45f);
        SetGlow(3.5f);
        if (sr != null) sr.enabled = false;

        float dur = 0.4f;
        for (float k = 0f; k < 1f; k += Time.deltaTime / dur)
        {
            SetGlow(Mathf.Lerp(3.5f, 0f, k));
            yield return null;
        }
        yield return new WaitForSeconds(0.9f);
        Destroy(gameObject);
    }

    // ----- baked art: purple rock base + faceted blue crystal, with crackLevel damage stages ----------
    static Sprite BakeBody(int crackLevel)
    {
        const float aa = 2f / 128f;
        const float maxR = 0.72f;
        float rot = Mathf.PI / 6f;
        float[] cracks = { 0.6f, 2.4f, -1.3f };   // deterministic crack ray angles
        return BakeSprite(128, 1.2f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color c = new Color(0, 0, 0, 0);

            // rock base / shadow halo
            float baseE = r - 0.86f;
            float bf = Fill(baseE, aa);
            if (bf > 0f) c = Over(c, A(D1a, bf * 0.55f));

            // crystal (hexagonal facets)
            float poly = PolyRadius(ang, 6, rot);
            float edge = r - maxR * poly;
            float cf = Fill(edge, aa);
            if (cf > 0f)
            {
                int sector = Mathf.FloorToInt(Mathf.Repeat(ang - rot, Mathf.PI * 2f) / (Mathf.PI / 3f));
                Color facet = (sector % 2 == 0) ? B3 : B2;
                facet = Color.Lerp(facet, B4, Mathf.Clamp01(1f - r / (maxR * 0.9f)) * 0.7f); // domed/bright core
                float rim = Mathf.Abs(edge) < 0.05f ? 1f : 0f;
                facet = Color.Lerp(facet, Outline, rim * 0.8f);
                float a2 = Mathf.Repeat(ang - rot, Mathf.PI / 3f);
                float seam = Mathf.Abs(a2 - Mathf.PI / 6f) < 0.04f ? 1f : 0f;
                facet = Color.Lerp(facet, B1, seam * 0.5f);
                c = Over(c, A(facet, cf));

                // damage cracks
                for (int k = 0; k < crackLevel; k++)
                {
                    float ca = cracks[k % cracks.Length];
                    Vector2 dirv = new Vector2(Mathf.Cos(ca), Mathf.Sin(ca));
                    float along = nx * dirv.x + ny * dirv.y;
                    float perp = Mathf.Abs(-nx * dirv.y + ny * dirv.x);
                    if (along > 0f && perp < 0.02f + 0.015f * along)
                        c = Over(c, A(Outline, 0.85f));
                }
            }
            return c;
        });
    }
}
