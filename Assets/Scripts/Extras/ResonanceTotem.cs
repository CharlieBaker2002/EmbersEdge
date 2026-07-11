using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ALLY-ONLY "resonance totem" interactive (blue / engineer element) — my invention for the set. A tuned
/// crystal monolith the player CHARGES by shooting: each ally round that strikes it stacks a charge (the
/// resonator rings climb and brighten). The instant it tops out — or a heartbeat after the player stops
/// feeding it — it discharges a single expanding shockwave ring that SLOWS and KNOCKS BACK every enemy the
/// wavefront sweeps through, the radius and force scaling with how many charges it banked. It is the set's
/// crowd-control payoff: dump fire into it while a pack closes, then watch the whole front get flung back
/// and bogged down. Single use; it shatters on discharge, or self-destructs at maxLifetime.
///
/// Reuses the slow CC + external-knockback path (ActionScript.AddPush / GS.Stat, exactly as EmberSnare) and
/// a code-built expanding ring. Detection is a TRIGGER on the Walls layer filtered to ally projectiles.
/// Body art baked in code.
/// </summary>
public class ResonanceTotem : WaveExtra
{
    [Header("Resonance")]
    public int maxCharges = 5;
    [Tooltip("If no further ally shot lands within this window after charging, it discharges what it holds.")]
    public float settleWindow = 0.85f;
    [Tooltip("Nova radius at full charge; partial charges scale this down.")]
    public float novaRadius = 5.8f;
    public float novaTime = 0.5f;          // seconds for the wavefront to reach full radius
    public float knockback = 78f;          // per-target push (scaled by mass + charge fraction)
    public float slowDuration = 2.2f;
    [Range(0f, 1f)] public float slowMult = 0.5f;
    public float damage = 3f;              // light blue damage carried by the wavefront

    static readonly Collider2D[] buf = new Collider2D[128];
    static int enemyMask = -1;
    static ContactFilter2D enemyFilter;
    static Sprite _body;

    SpriteRenderer sr;
    int charge;
    float sinceHit;
    float phase, flash;
    bool releasing;

    void Awake()
    {
        if (enemyMask < 0)
        {
            enemyMask = LayerMask.GetMask("Enemy Units", "Enemy Buildings");
            enemyFilter = new ContactFilter2D { useLayerMask = true, layerMask = enemyMask, useTriggers = false };
        }
        sr = GetComponent<SpriteRenderer>();
        if (_body == null) _body = BakeBody();
        if (sr != null)
        {
            sr.sprite = _body;
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(B4, 1.7f);
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        flash = Mathf.Max(0f, flash - Time.deltaTime * 3.5f);
        float chargeFrac = (float)charge / Mathf.Max(1, maxCharges);

        // Idle hums slowly; the more charge banked, the faster/brighter it resonates (telegraphs the release).
        float beat = 0.5f + 0.5f * Mathf.Sin(phase * (3f + 9f * chargeFrac));
        SetGlow(0.6f + (0.6f + 1.6f * chargeFrac) * beat + 2.5f * flash);
        transform.localScale = baseScale * popK * (1f + (0.03f + 0.06f * chargeFrac) * beat + 0.16f * flash);
        if (sr != null) sr.color = Color.Lerp(new Color(0.7f, 0.78f, 1f), Color.white, 0.35f + 0.45f * (chargeFrac * beat) + 0.5f * flash);

        if (charge > 0 && !releasing)
        {
            sinceHit += Time.deltaTime;
            if (sinceHit >= settleWindow) StartCoroutine(Discharge());
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || releasing || other == null) return;
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p == null || !p.CompareTag("Allies")) return;

        charge = Mathf.Min(maxCharges, charge + 1);
        sinceHit = 0f;
        flash = 1f;
        var ps = Burst();
        EmitBurst(ps, transform.position, 8, new[] { B4, B3 }, 1.4f, 3.6f, 0.3f, 0.6f, 0.12f, 0.24f);
        if (charge >= maxCharges) StartCoroutine(Discharge());
    }

    IEnumerator Discharge()
    {
        if (releasing || consumed) yield break;
        releasing = true;
        consumed = true;                                  // claims the slot; FX coroutine ends in Destroy
        Vector3 c = transform.position;
        float chargeFrac = (float)charge / Mathf.Max(1, maxCharges);
        float radius = novaRadius * (0.45f + 0.55f * chargeFrac);

        Shockwave.Spawn(c, radius, 0.03f, 0.55f);
        SetGlow(3.4f);

        // A code-built ring that races outward; each enemy is hit once, as the wavefront passes it.
        SpriteRenderer ring = MakeRing(c);
        var hitSet = new HashSet<EntityId>();
        for (float t = 0f; t < novaTime; t += Time.deltaTime)
        {
            float k = t / novaTime;
            float rad = radius * k;
            if (ring != null)
            {
                ring.transform.localScale = Vector3.one * (rad * 2f);
                ring.color = A(B4, (1f - k) * 0.9f);
            }
            SweepRing(c, rad, chargeFrac, hitSet);
            SetGlow(Mathf.Lerp(3.4f, 0.4f, k));
            yield return null;
        }
        SweepRing(c, radius, chargeFrac, hitSet);          // final pass catches the rim
        if (ring != null) Destroy(ring.gameObject);

        var ps = Burst();
        EmitBurst(ps, c, 30, new[] { B4, B3, B2 }, 3.5f, 7.5f, 0.4f, 0.8f, 0.18f, 0.36f);
        if (sr != null) sr.enabled = false;
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.3f) { SetGlow(Mathf.Lerp(1f, 0f, k)); yield return null; }
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    // Slow + shove every enemy whose centre now sits inside the wavefront and hasn't been caught yet.
    void SweepRing(Vector2 c, float rad, float chargeFrac, HashSet<EntityId> hitSet)
    {
        int n = Physics2D.OverlapCircle(c, rad, enemyFilter, buf);
        for (int i = 0; i < n; i++)
        {
            var col = buf[i];
            if (col == null || col.isTrigger || col.attachedRigidbody == null) continue;
            EntityId id = col.attachedRigidbody.GetEntityId();
            if (!hitSet.Add(id)) continue;

            var ls = col.GetComponentInParent<LifeScript>();
            if (ls != null && damage > 0f) ls.Change(-damage * (0.5f + 0.5f * chargeFrac), 2);   // 2 = blue
            SlowIt(col.transform);
            var asc = col.GetComponentInParent<ActionScript>();
            if (asc != null && asc.pushable)
            {
                Vector2 dir = ((Vector2)col.transform.position - c).normalized;
                if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;
                asc.AddPush(0.13f, true, dir * asc.mass * knockback * (0.5f + 0.5f * chargeFrac));
            }
        }
    }

    void SlowIt(Transform t)
    {
        var u = t.GetComponentInParent<Unit>();
        if (u != null) GS.Stat(u, "slow", slowDuration, slowMult);
        else { var a = t.GetComponentInParent<ActionScript>(); if (a != null) a.AddCC("slow", slowDuration, slowMult); }
    }

    SpriteRenderer MakeRing(Vector3 c)
    {
        var g = new GameObject("ResonanceRing");
        g.transform.position = c;
        var s = g.AddComponent<SpriteRenderer>();
        s.sprite = HaloSprite;                            // soft disc reused as a flat wavefront glow
        var stock = Resources.Load<Material>("Sprite-Unlit-Default");
        if (stock != null) s.sharedMaterial = stock;
        s.sortingLayerName = "Power Ups";
        s.sortingOrder = 4;
        s.color = A(B4, 0.9f);
        return s;
    }

    // ----- baked art: a purple monolith with a stack of bright blue resonator bands + a tuned core --------
    static Sprite BakeBody()
    {
        const float aa = 2f / 128f;
        return BakeSprite(128, 1.2f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color col = new Color(0, 0, 0, 0);

            // upright diamond monolith (purple), outlined
            float diamond = (Mathf.Abs(nx) / 0.62f + Mathf.Abs(ny) / 0.86f) - 1f;
            float df = Fill(diamond * 0.6f, aa);
            if (df > 0f)
            {
                Color body = Color.Lerp(D1c, D1a, Mathf.Clamp01((Mathf.Abs(ny)) / 0.86f));
                float ring = Mathf.Abs(diamond) < 0.08f ? 1f : 0f;
                col = Over(col, A(Color.Lerp(body, Outline, ring), df));
            }

            // resonator bands: three horizontal blue rungs climbing the monolith
            for (int b = 0; b < 3; b++)
            {
                float yb = -0.34f + b * 0.34f;
                float band = Mathf.Abs(ny - yb) - 0.045f;
                float bf = Fill(band, aa);
                float wide = 1f - Mathf.Clamp01(Mathf.Abs(ny) / 0.86f);
                if (bf > 0f && Mathf.Abs(nx) < 0.5f * wide + 0.08f && r < 0.9f)
                    col = Over(col, A(Color.Lerp(B3, B4, 0.5f + 0.5f * b / 2f), bf));
            }

            // tuned core
            float core = r - 0.16f;
            float cf = Fill(core, aa);
            if (cf > 0f) col = Over(col, A(Color.Lerp(B4, B3, Mathf.Clamp01(r / 0.16f)), cf));
            return col;
        });
    }
}
