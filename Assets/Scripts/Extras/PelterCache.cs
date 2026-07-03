using System.Collections;
using UnityEngine;

/// <summary>
/// ALLY-ONLY "pelter cache" interactive (blue / engineer element). A sealed munitions cache that the player
/// SHOOTS to crack open: every ally round that strikes it dislodges Pelter turret rounds, which spill out
/// already homing on the enemy pack. It holds seven rounds behind twelve points of casing — it pops one
/// round on the very first hit (11.99 HP), one more each time its casing drops past a 2-HP threshold
/// (10 / 8 / 6 / 4 / 2), and the last as the casing fails (&lt;= 0). Enemies that wander through it do
/// nothing — only the player's fire opens it. Single use; self-destructs once emptied or at maxLifetime.
///
/// Reuses the exact rounds the Pelter fires (HomingDart = bullets[0], BigHomingDart = bullets[1]); the
/// spilled rounds are spawned as ALLY projectiles so their Seeking acquires the nearest enemy. Detection is
/// a TRIGGER on the Walls layer filtered to ally projectiles (as VerdantWellspring does). Art baked in code.
/// </summary>
public class PelterCache : WaveExtra
{
    [Header("Cache")]
    [Tooltip("Standard Pelter round (bullets[0]) spilled at most thresholds.")]
    public GameObject roundProjectile;
    [Tooltip("Heavier Pelter round (bullets[1]) spilled on the final, casing-failure pop.")]
    public GameObject bigRoundProjectile;
    public float maxHP = 12f;
    [Tooltip("Per-round launch jitter (degrees) fed to GS.NewP.")]
    public float roundSpread = 12f;
    [Tooltip("Outward speed kick given to a spilled round before its Seeking takes over.")]
    public float spillSpeed = 5.5f;

    // 2-HP casing thresholds; combined with the first-hit pop and the death pop this yields exactly 7 rounds.
    static readonly float[] Thresholds = { 10f, 8f, 6f, 4f, 2f };
    static readonly Collider2D[] enemyBuf = new Collider2D[16];
    static int enemyMask = -1;
    static Sprite _body;

    SpriteRenderer sr;
    float hp;
    bool firstFired;
    bool[] fired;
    float phase, flash;

    void Awake()
    {
        if (enemyMask < 0) enemyMask = LayerMask.GetMask("Enemy Units", "Enemy Buildings");
        sr = GetComponent<SpriteRenderer>();
        hp = maxHP;
        fired = new bool[Thresholds.Length];
        if (_body == null) _body = BakeBody();
        if (sr != null)
        {
            sr.sprite = _body;
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(B4, 1.8f);
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        flash = Mathf.Max(0f, flash - Time.deltaTime * 3.5f);
        float frac = Mathf.Clamp01(hp / maxHP);
        float breath = 0.5f + 0.5f * Mathf.Sin(phase * 2f);
        SetGlow(0.6f + 0.8f * frac * breath + 2.5f * flash);                 // dims as the casing is spent
        transform.localScale = baseScale * popK * (1f + 0.03f * breath + 0.18f * flash);
        if (sr != null) sr.color = Color.Lerp(new Color(0.7f, 0.78f, 1f), Color.white, 0.35f + 0.4f * breath + 0.5f * flash);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null) return;
        // Ally fire only; enemy rounds & bodies pass straight through.
        var p = other.GetComponentInParent<ProjectileScript>();
        if (p == null || !p.CompareTag("Allies")) return;
        Hit(Mathf.Max(0.05f, Mathf.Abs(p.damage)));
    }

    void Hit(float dmg)
    {
        hp -= dmg;
        flash = 1f;
        var ps = Burst();
        EmitBurst(ps, transform.position, 6, new[] { B4, B3 }, 1f, 3f, 0.25f, 0.5f, 0.1f, 0.2f);

        if (!firstFired) { firstFired = true; Spill(false); }                // first hit always cracks one loose
        for (int i = 0; i < Thresholds.Length; i++)
            if (!fired[i] && hp <= Thresholds[i]) { fired[i] = true; Spill(false); }

        if (hp <= 0f) { Spill(true); Empty(); }
    }

    void Spill(bool heavy)
    {
        var proj = heavy && bigRoundProjectile != null ? bigRoundProjectile : roundProjectile;
        if (proj == null) return;
        // Bias the spill toward the nearest enemy when there is one; otherwise just kick it outward.
        Vector2 dir;
        Transform foe = NearestEnemy();
        if (foe != null) dir = ((Vector2)foe.position - (Vector2)transform.position).normalized;
        else dir = GS.VTheta(Random.Range(0f, 360f));

        var g = GS.NewP(proj, transform, "Allies", dir, roundSpread, 0f);
        if (g != null)
        {
            var rb = g.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = dir * spillSpeed;            // a brief outward toss before homing
        }
    }

    Transform NearestEnemy()
    {
        Vector2 p = transform.position;
        int n = Physics2D.OverlapCircle(p, 9f, new ContactFilter2D { useLayerMask = true, layerMask = enemyMask, useTriggers = false }, enemyBuf);
        Transform best = null; float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (enemyBuf[i] == null || enemyBuf[i].isTrigger) continue;
            float d = ((Vector2)enemyBuf[i].transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = enemyBuf[i].transform; }
        }
        return best;
    }

    void Empty()
    {
        if (consumed) return;
        consumed = true;
        StartCoroutine(EmptyFX(transform.position));
    }

    IEnumerator EmptyFX(Vector3 p)
    {
        var ps = Burst();
        EmitBurst(ps, p, 22, new[] { B4, B3, B2 }, 2.5f, 5.5f, 0.35f, 0.7f, 0.16f, 0.32f);
        Shockwave.Spawn(p, 1.4f, 0.016f, 0.4f);
        SetGlow(3f);
        if (sr != null) sr.enabled = false;
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f) { SetGlow(Mathf.Lerp(3f, 0f, k)); yield return null; }
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    // ----- baked art: a rounded purple casing + a bright blue rounds-bundle behind a seam -----------------
    static Sprite BakeBody()
    {
        const float aa = 2f / 128f;
        float rot = Mathf.PI / 4f;
        return BakeSprite(128, 1.2f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color col = new Color(0, 0, 0, 0);

            // rounded-square casing (purple) with a bevelled outline
            float box = r - 0.8f * PolyRadius(ang, 4, rot);
            float bf = Fill(box, aa);
            if (bf > 0f)
            {
                Color body = Color.Lerp(D1c, D1a, Mathf.Clamp01((r) / 0.8f));
                float ring = Mathf.Abs(box) < 0.06f ? 1f : 0f;
                col = Over(col, A(Color.Lerp(body, Outline, ring), bf));
            }

            // bundled rounds: three bright blue tips peeking through the cracked lid (upper band)
            if (ny > 0.04f && r < 0.66f)
            {
                for (int k = -1; k <= 1; k++)
                {
                    Vector2 c = new Vector2(k * 0.3f, 0.28f);
                    float d = (new Vector2(nx, ny) - c).magnitude - 0.16f;
                    float rf = Fill(d, aa);
                    if (rf > 0f)
                    {
                        Color tip = Color.Lerp(B4, B2, Mathf.Clamp01((new Vector2(nx, ny) - c).magnitude / 0.16f));
                        col = Over(col, A(tip, rf));
                    }
                }
            }

            // a glowing horizontal seam where the lid splits
            float seam = Mathf.Abs(ny - 0.02f) < 0.03f && Mathf.Abs(nx) < 0.62f ? 1f : 0f;
            if (seam > 0f && r < 0.78f) col = Over(col, A(B3, 0.8f));
            return col;
        });
    }
}
