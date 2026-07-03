using System.Collections;
using UnityEngine;

/// <summary>
/// PLAYER-FRIENDLY interactive entity (green element). A luminous bloom the player SHOOTS to harvest: every
/// ally projectile that strikes it makes it pulse and spit out collectible green (druid) orbs plus a trickle
/// of core energy. It has a finite generosity (a handful of charges); when spent it blooms one last time,
/// healing nearby allies, then wilts away. Only the player's shots harvest it — enemies just pass through.
///
/// Detection via a TRIGGER collider (the prefab sits on the Walls layer, which overlaps ally projectiles),
/// filtered to ally-tagged projectiles. Spawned through the wave pipeline; self-destructs when spent or at
/// maxLifetime. Realises the brief's "shoot to drop orbs / gain energy" idea. Body art baked in code.
/// </summary>
public class VerdantWellspring : WaveExtra
{
    [Header("Wellspring")]
    public int charges = 8;            // how many shots it gives before wilting
    public int orbsPerHit = 3;         // green (druid) orbs dropped each hit
    public float energyPerHit = 4f;    // core energy granted each hit
    public float healOnWilt = 6f;      // final bloom heals nearby allies
    public float healRadius = 3.2f;

    static readonly Collider2D[] hitBuf = new Collider2D[64];
    static Sprite _body;   // deterministic baked art, shared across instances

    SpriteRenderer sr;
    int left;
    float phase;
    float flash;     // decaying bloom-pulse brightness

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        left = Mathf.Max(1, charges);
        if (sr != null)
        {
            if (_body == null) _body = BakeBody();
            sr.sprite = _body;
            var m = Resources.Load<Material>("Sprite-Unlit-Default");
            if (m != null) sr.sharedMaterial = m;
            sr.sortingLayerName = "Buildings";
            sr.sortingOrder = 1;
        }
        MakeHalo(G4, 1.9f);
    }

    void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;
        flash = Mathf.Max(0f, flash - Time.deltaTime * 3f);
        float fertility = (float)left / Mathf.Max(1, charges);          // dims as it's spent
        float breath = 0.5f + 0.5f * Mathf.Sin(phase * 1.8f);
        SetGlow((0.6f + 1f * fertility) * (0.7f + 0.3f * breath) + flash * 2.5f);
        float bloom = 1f + 0.04f * breath + 0.25f * flash;
        transform.localScale = baseScale * popK * bloom;
        if (sr != null) sr.color = Color.Lerp(Color.Lerp(new Color(0.6f, 0.75f, 0.6f), Color.white, fertility), Color.white, flash);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null) return;
        // Only the player's own shots harvest it.
        if (other.CompareTag("Allies") && other.GetComponentInParent<ProjectileScript>() != null)
            Harvest(other.transform.position);
    }

    void Harvest(Vector2 where)
    {
        left--;
        GS.CallSpawnOrbs(transform.position, new int[] { 0, orbsPerHit, 0, 0 }); // druid = green orbs
        if (ResourceManager.instance != null) ResourceManager.instance.UseEnergy(energyPerHit);

        flash = 1f;
        var ps = Burst();
        EmitBurst(ps, transform.position, 14, new[] { G4, G3, G2 }, 1.6f, 4.2f, 0.4f, 0.8f, 0.16f, 0.32f);
        EmitBurst(ps, where, 6, new[] { G4, G3 }, 0.6f, 2f, 0.3f, 0.6f, 0.1f, 0.2f);

        if (left <= 0) Wilt();
    }

    void Wilt()
    {
        if (consumed) return;
        consumed = true;
        Vector3 p = transform.position;

        // final bloom: heal nearby allies (+ the player)
        int mask = LayerMask.GetMask("Ally Units", "Character");
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = mask, useTriggers = true };
        int n = Physics2D.OverlapCircle(p, healRadius, filter, hitBuf);
        for (int i = 0; i < n; i++)
        {
            var col = hitBuf[i];
            if (col == null) continue;
            var ls = col.GetComponentInParent<LifeScript>();
            if (ls != null && ls.hp < ls.maxHp) ls.Change(healOnWilt, 1); // 1 = green dmgType (heal)
        }
        StartCoroutine(WiltFX(p));
    }

    IEnumerator WiltFX(Vector3 p)
    {
        var ps = Burst();
        EmitBurst(ps, p, 30, new[] { G4, G3, G2 }, 2.6f, 6f, 0.5f, 1f, 0.18f, 0.36f);  // seed-burst
        Shockwave.Spawn(p, healRadius * 0.7f, 0.012f, 0.4f);
        SetGlow(3f);

        float dur = 0.5f;
        Vector3 from = transform.localScale;
        for (float k = 0f; k < 1f; k += Time.deltaTime / dur)
        {
            transform.localScale = Vector3.Lerp(from, from * 0.2f, k);   // petals close / wilt
            SetGlow(Mathf.Lerp(3f, 0f, k));
            if (sr != null) sr.color = Color.Lerp(Color.white, new Color(0.35f, 0.45f, 0.3f, 0.4f), k);
            yield return null;
        }
        yield return new WaitForSeconds(0.7f);
        Destroy(gameObject);
    }

    // ----- baked art: purple bulb base + luminous green petals + bright core --------------------------
    static Sprite BakeBody()
    {
        const float aa = 2f / 128f;
        const int petals = 6;
        float rot = 0.2f;
        return BakeSprite(128, 1.15f, (nx, ny) =>
        {
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(ny, nx);
            Color c = new Color(0, 0, 0, 0);

            // bulb base (purple)
            float baseE = r - 0.5f;
            float bf = Fill(baseE, aa);
            if (bf > 0f) c = Over(c, A(Color.Lerp(D1b, D1a, Mathf.Clamp01(r / 0.5f)), bf));

            // petals: P lobes via a cosine radius profile
            float lobe = 0.5f + 0.5f * Mathf.Cos((ang - rot) * petals);
            float petalR = 0.42f + 0.42f * lobe;
            float pe = r - petalR;
            float pf = Fill(pe, aa);
            if (pf > 0f && r > 0.18f)
            {
                Color petal = Color.Lerp(G3, G2, Mathf.Clamp01((r - 0.3f) / 0.55f));
                petal = Color.Lerp(petal, G4, lobe * 0.4f);
                float rim = Mathf.Abs(pe) < 0.05f ? 1f : 0f;
                petal = Color.Lerp(petal, Outline, rim * 0.7f);          // purple-tipped petal edge
                float a2 = Mathf.Repeat(ang - rot, Mathf.PI * 2f / petals);
                float vein = Mathf.Abs(a2) < 0.03f ? 1f : 0f;
                petal = Color.Lerp(petal, G1, vein * 0.5f);
                c = Over(c, A(petal, pf));
            }

            // glowing core
            float core = r - 0.2f;
            float cf = Fill(core, aa);
            if (cf > 0f) c = Over(c, A(Color.Lerp(G4, G3, Mathf.Clamp01(r / 0.2f)), cf));
            return c;
        });
    }
}
