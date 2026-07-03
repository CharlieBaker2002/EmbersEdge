using System.Collections;
using UnityEngine;

/// <summary>
/// TRAP (red element). An ember-fed snare scattered into the arena, telegraphed by a slow red pulse while
/// it arms. Once armed, the first ENEMY to wander within its trigger radius springs it: the jaws snap shut,
/// hard-ROOTING that enemy in place, and a ring of embers detonates — damage + knockback to the surrounding
/// pack. A player-helping boon among the wave's hazards (a pinned target for turrets / the player). Single
/// use; it burns out after springing, or self-destructs at maxLifetime so it never hangs the wave.
///
/// Detection is a faction-blind OverlapCircle on the Enemy Units layer (no collider interaction needed),
/// so it works regardless of the tag the wave pipeline spawns it under. Body art is baked in code.
/// </summary>
public class EmberSnare : WaveExtra
{
    [Header("Snare")]
    public float armDelay = 1.4f;       // dormant window before it can spring (telegraph)
    public float triggerRadius = 0.95f; // how close an enemy must get to spring it
    public float blastRadius = 2.1f;    // damage / knockback radius on spring
    public float damage = 7f;
    public float rootTime = 1.3f;       // hard root on the enemy that springs it
    public float knockback = 70f;       // push on the surrounding pack (scaled by each target's mass)

    static readonly Collider2D[] buf = new Collider2D[64];
    static int enemyMask = -1;
    static ContactFilter2D enemyFilter;
    static Sprite _body;   // deterministic baked art, shared across instances

    SpriteRenderer sr;
    float t;       // age
    float phase;   // pulse phase
    bool armed;

    void Awake()
    {
        if (enemyMask < 0)
        {
            enemyMask = LayerMask.GetMask("Enemy Units");
            enemyFilter = new ContactFilter2D { useLayerMask = true, layerMask = enemyMask, useTriggers = true };
        }
        sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (_body == null) _body = BakeBody();
            sr.sprite = _body;
            var m = Resources.Load<Material>("Sprite-Unlit-Default"); // render the baked art true-colour
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(R4, 1.7f);
    }

    void Update()
    {
        if (consumed) return;
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / armDelay);
        if (!armed && t >= armDelay) armed = true;

        // Pulse: slow while arming, then a steady armed heartbeat.
        phase += Time.deltaTime * (1.2f + 3.5f * a);
        float pulse = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
        SetGlow(0.5f + (armed ? 1.1f : 0.4f * a) * pulse);
        if (sr != null) sr.color = Color.Lerp(new Color(0.8f, 0.8f, 0.8f), Color.white, (armed ? 0.6f : 0.25f) * pulse + 0.4f);
        transform.localScale = baseScale * popK * (1f + (armed ? 0.05f : 0.02f) * pulse);

        if (!armed) return;
        Vector3 p = transform.position;
        int n = Physics2D.OverlapCircle(p, triggerRadius, enemyFilter, buf);
        Transform first = null; float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (buf[i] == null) continue;
            float d = ((Vector2)(buf[i].transform.position - p)).sqrMagnitude;
            if (d < best) { best = d; first = buf[i].transform; }
        }
        if (first != null) Spring(first);
    }

    void Spring(Transform first)
    {
        if (consumed) return;
        consumed = true;
        Vector3 p = transform.position;

        // Pin the unlucky trigger-er, then detonate the surrounding pack.
        RootIt(first);
        int n = Physics2D.OverlapCircle(p, blastRadius, enemyFilter, buf);
        for (int i = 0; i < n; i++)
        {
            var col = buf[i];
            if (col == null) continue;
            float fall = 1f - Mathf.Clamp01(((Vector2)((Vector3)col.transform.position - p)).magnitude / blastRadius);
            var ls = col.GetComponentInParent<LifeScript>();
            if (ls != null) ls.Change(-damage * (0.4f + 0.6f * fall), 3); // 3 = red dmgType
            var asc = col.GetComponentInParent<ActionScript>();
            if (asc != null && asc.pushable)
            {
                Vector2 dir = ((Vector2)((Vector3)col.transform.position - p)).normalized;
                if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;
                asc.AddPush(0.12f, true, dir * asc.mass * knockback * (0.4f + 0.6f * fall));
            }
        }
        StartCoroutine(SpringFX(p));
    }

    void RootIt(Transform first)
    {
        var u = first.GetComponentInParent<Unit>();
        if (u != null) GS.Stat(u, "root", rootTime, 0f);
        else { var a = first.GetComponentInParent<ActionScript>(); if (a != null) a.AddCC("root", rootTime, 0f); }
    }

    IEnumerator SpringFX(Vector3 p)
    {
        var ps = Burst();
        EmitBurst(ps, p, 26, new[] { R4, R3, R2 }, 3.2f, 6.5f, 0.35f, 0.7f, 0.18f, 0.34f);   // jaws-snap ember ring
        EmitBurst(ps, p, 14, new[] { R3, R4 }, 0.4f, 2.2f, 0.5f, 0.95f, 0.12f, 0.24f);        // lingering cinders
        Shockwave.Spawn(p, blastRadius, 0.018f, 0.4f);
        SetGlow(3f);

        // Jaws snap: a fast punch in then a hard shut.
        float dur = 0.22f;
        for (float k = 0f; k < 1f; k += Time.deltaTime / dur)
        {
            float s = k < 0.4f ? Mathf.Lerp(1f, 1.3f, k / 0.4f) : Mathf.Lerp(1.3f, 0.45f, (k - 0.4f) / 0.6f);
            transform.localScale = baseScale * s;
            SetGlow(Mathf.Lerp(3f, 0.3f, k));
            if (sr != null) sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.2f, k));
            yield return null;
        }
        yield return new WaitForSeconds(0.7f); // let embers fade
        Destroy(gameObject);
    }

    // ----- baked art: purple plate + inward red jaws + glowing rune core ------------------------------
    static Sprite BakeBody()
    {
        const float aa = 2f / 128f;
        const int teeth = 8;
        float seg = Mathf.PI * 2f / teeth;
        return BakeSprite(128, 1.15f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color c = new Color(0, 0, 0, 0);

            // plate (domed purple disc) + outline
            float plate = r - 0.82f;
            float pf = Fill(plate, aa);
            if (pf > 0f)
            {
                Color body = Color.Lerp(D1b, D1a, Mathf.Clamp01((r) / 0.82f));
                float ring = Mathf.Abs(plate) < 0.06f ? 1f : 0f;
                c = Over(c, A(Color.Lerp(body, Outline, ring), pf));
            }

            // inward-pointing jaw teeth in the band [0.5, 0.82]
            if (r > 0.48f && r < 0.84f)
            {
                float a2 = Mathf.Repeat(ang, seg);
                float dCenter = Mathf.Abs(a2 - seg * 0.5f);
                float tt = Mathf.Clamp01((r - 0.5f) / 0.32f);          // 0 inner tip -> 1 outer base
                float halfW = Mathf.Lerp(0.02f, seg * 0.40f, tt);
                if (dCenter < halfW)
                {
                    float tip = 1f - tt;                                 // brighter toward the point
                    Color tooth = Color.Lerp(D1c, D1d, tip * 0.7f);
                    c = Over(c, A(tooth, 0.95f));
                }
            }

            // glowing rune core
            float core = r - 0.36f;
            float cf = Fill(core, aa);
            if (cf > 0f)
            {
                Color hot = Color.Lerp(R4, R2, Mathf.Clamp01(r / 0.36f));
                float rune = (Mathf.Abs(r - 0.26f) < 0.035f) ? 1f : 0f;   // thin inner rune ring
                c = Over(c, A(Color.Lerp(hot, R1, rune), cf));
            }
            return c;
        });
    }
}
