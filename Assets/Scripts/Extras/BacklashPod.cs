using System.Collections;
using UnityEngine;

/// <summary>
/// NEUTRAL "reactive burst" interactive (white / neutral pole — its allegiance is decided at the moment it
/// is struck). A bristling seed-pod with a single point of durability: the FIRST entity to damage it — a
/// shot of EITHER faction, or an enemy body grinding through it — pops it. On popping it spits a dense ring
/// of homing darts radially AWAY from whatever hit it, and those darts seek the STRIKER's enemies:
///   • a player shot pops it  -> the darts are friendly and hunt the enemy pack,
///   • an enemy shot/body pops it -> the darts turn hostile and hunt the player &amp; allies.
/// So the pod's faction is inverted relative to the side it ends up helping — a gamble the player can turn
/// to their favour by shooting it while enemies cluster, but a trap if an errant enemy round sets it off.
/// Single use; self-destructs after the burst, or at maxLifetime so it never hangs the wave.
///
/// Reuses the Pelter's seeking round (HomingDart / BigHomingDart) so the darts curve onto their targets;
/// the spawn re-tags them to the striker's faction (ProjectileScript.SetValues re-layers + re-tags). The
/// small / large variants (built from the same script) only change the dart count, scale and round strength.
/// Detection is a TRIGGER on the Walls layer (overlaps both projectile factions + enemy bodies, exactly as
/// CinderGeode relies on). Body art is baked in code.
/// </summary>
public class BacklashPod : WaveExtra
{
    [Header("Backlash")]
    [Tooltip("Reused Pelter seeking round — re-tagged to the striker's faction so it homes on their enemies.")]
    public GameObject seekProjectile;
    [Tooltip("How many homing darts the pod spits when it pops.")]
    public int projectileCount = 14;
    [Tooltip("ProjectileScript strength scalar applied to every dart (speed/damage/mass). Large variant > 0.")]
    public float projectileStrength = 0f;
    [Tooltip("Per-dart launch jitter fed to GS.NewP (degrees of perpendicular spread).")]
    public float burstInaccuracy = 7f;
    [Tooltip("Durability lost per enemy-body grind; the pod pops the instant cumulative damage reaches 1.")]
    public float ramChip = 1f;

    static int enemyUnitsLayer = -1;
    static Sprite[] _frames;   // 0 = calm … 2 = bristling/agitated; deterministic, shared across instances

    SpriteRenderer sr;
    float phase;
    float spin;

    void Awake()
    {
        if (enemyUnitsLayer < 0) enemyUnitsLayer = LayerMask.NameToLayer("Enemy Units");
        sr = GetComponent<SpriteRenderer>();
        if (_frames == null) { _frames = new Sprite[3]; for (int i = 0; i < 3; i++) _frames[i] = BakeBody(i); }
        if (sr != null)
        {
            sr.sprite = _frames[0];
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(W3, 1.7f);
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        spin += Time.deltaTime * 18f;
        // A coiled-spring idle: a tense breathing pulse + a slow rotation, brightening on the beat.
        float pulse = 0.5f + 0.5f * Mathf.Sin(phase * 3.4f);
        SetGlow(0.8f + 0.9f * pulse);
        transform.localScale = baseScale * popK * (1f + 0.05f * pulse);
        transform.rotation = Quaternion.Euler(0f, 0f, spin);
        if (sr != null)
        {
            sr.sprite = _frames[pulse > 0.72f ? 1 : 0];                       // a little "flex" on each beat
            sr.color = Color.Lerp(new Color(0.85f, 0.9f, 0.88f), Color.white, 0.4f + 0.4f * pulse);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null) return;
        // Any projectile of EITHER faction sets it off — the dart faction follows the striker's faction.
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p != null) { Pop(p.CompareTag("Allies"), other.transform.position); return; }
        // An enemy body grinding through pops it too (hostile darts toward the player).
        if (other.gameObject.layer == enemyUnitsLayer) Pop(false, other.transform.position);
    }

    void Pop(bool allyStruck, Vector2 strikerPos)
    {
        if (consumed) return;
        consumed = true;
        Vector3 c = transform.position;

        string tag = allyStruck ? "Allies" : "Enemies";
        Color tint = allyStruck ? W4 : R4;
        // Orient the ring so its leading face points AWAY from whoever struck it.
        Vector2 away = ((Vector2)c - strikerPos);
        float baseDeg = away.sqrMagnitude > 1e-4f ? Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg : Random.Range(0f, 360f);

        int n = Mathf.Max(1, projectileCount);
        for (int i = 0; i < n; i++)
        {
            Vector2 dir = GS.VTheta(baseDeg + i * (360f / n));
            var g = GS.NewP(seekProjectile, transform, tag, dir, burstInaccuracy, projectileStrength);
            if (g != null)
            {
                // target stays null -> the dart's own Seeking acquires the nearest foe of its (new) faction.
                var dsr = g.GetComponentInChildren<SpriteRenderer>();
                if (dsr != null) dsr.color = tint;       // pale for friendly, red for hostile (palette)
            }
        }

        StartCoroutine(PopFX(c, tint));
    }

    IEnumerator PopFX(Vector3 c, Color tint)
    {
        var ps = Burst();
        EmitBurst(ps, c, 24, new[] { tint, Color.Lerp(tint, Color.white, 0.4f) }, 3.5f, 7f, 0.3f, 0.6f, 0.16f, 0.32f);
        EmitBurst(ps, c, 12, new[] { tint }, 0.4f, 2f, 0.45f, 0.8f, 0.12f, 0.24f);
        Shockwave.Spawn(c, 1.8f, 0.02f, 0.4f);
        SetGlow(3.2f);
        if (sr != null) sr.enabled = false;

        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f) { SetGlow(Mathf.Lerp(3.2f, 0f, k)); yield return null; }
        yield return new WaitForSeconds(0.6f);
        Destroy(gameObject);
    }

    // ----- baked art: purple seed-core + radiating white thorns; frame 1 flexes the thorns outward --------
    static Sprite BakeBody(int agitation)
    {
        const float aa = 2f / 128f;
        const int thorns = 9;
        float flex = 0.06f * agitation;                       // thorns push out slightly when agitated
        return BakeSprite(128, 1.2f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color col = new Color(0, 0, 0, 0);

            // radiating thorns: a star whose radius spikes at each lobe
            float lobe = Mathf.Abs(Mathf.Cos((ang) * thorns * 0.5f));
            float thornR = 0.42f + (0.34f + flex) * Mathf.Pow(lobe, 2.2f);
            float te = r - thornR;
            float tf = Fill(te, aa);
            if (tf > 0f && r > 0.2f)
            {
                Color thorn = Color.Lerp(W2, W4, Mathf.Clamp01(1f - r / thornR));   // bright toward the centre
                float rim = Mathf.Abs(te) < 0.05f ? 1f : 0f;
                thorn = Color.Lerp(thorn, Outline, rim * 0.7f);                     // purple-tipped
                col = Over(col, A(thorn, tf));
            }

            // seed core (purple, domed) with a bright neutral pip
            float core = r - 0.4f;
            float cf = Fill(core, aa);
            if (cf > 0f)
            {
                Color body = Color.Lerp(D1c, D1a, Mathf.Clamp01(r / 0.4f));
                float ring = Mathf.Abs(core) < 0.05f ? 1f : 0f;
                col = Over(col, A(Color.Lerp(body, Outline, ring), cf));
            }
            float pip = r - 0.16f;
            float pf = Fill(pip, aa);
            if (pf > 0f) col = Over(col, A(Color.Lerp(W4, W3, Mathf.Clamp01(r / 0.16f)), pf));
            return col;
        });
    }
}
