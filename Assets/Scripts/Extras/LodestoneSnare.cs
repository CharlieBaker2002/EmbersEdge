using System.Collections;
using UnityEngine;

/// <summary>
/// (5) Lodestone Snare. A dormant interior disc that wakes when the player comes within `detectRadius`,
/// then reels them IN with a rising magnetic pull while a slow keeps them from escaping — and once it has
/// them held, it erupts a Discer-style disc burst point-blank and releases. The pull/slow/blast combo
/// makes it the trap that sets the player up for everything else on the field. Single use.
/// </summary>
public class LodestoneSnare : SentryExtra
{
    [Header("Snare")]
    public GameObject discProjectile;   // reused Discer disc (BossP0)
    public float detectRadius = 5f;
    [Tooltip("Per-FixedUpdate pull force toward the core (external knockback path; f ~ Δv·mass/0.02).")]
    public float pullForce = 11f;
    public float chargeTime = 1.5f;
    [Tooltip("Velocity multiplier of the slow applied while reeling the player in (1 = none).")]
    public float slowMult = 0.55f;
    [Header("Payload")]
    public int discCount = 14;
    public float discSpread = 8f;
    public float discStrength = 0f;
    public float spin = 220f;           // core spin (deg/sec)

    protected override void OnReady()
    {
        wallMounted = false;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // 1) dormant: a slow, dim spin.
        while (DistToPlayer > detectRadius)
        {
            transform.Rotate(0f, 0f, spin * 0.25f * Time.deltaTime);
            SetBody(Dormant, 1f + 0.1f * Mathf.Sin(Time.time * 2f));
            yield return null;
        }

        // 2) arm: slow the player, then drag them inward as the core spins up and brightens.
        if (CharacterScript.CS != null) GS.Stat(CharacterScript.CS, "slow", chargeTime, slowMult);
        SpriteRenderer ring = MakeSprite("Pull", Ring(), Alert, 1f, order: 5);

        for (float t = 0f; t < chargeTime; t += Time.fixedDeltaTime)
        {
            float k = t / chargeTime;
            if (GS.AS != null)
            {
                Vector2 dir = (Vector2)transform.position - PlayerPos;
                if (dir.sqrMagnitude > 0.04f)
                    GS.AS.TryAddForce(dir.normalized * pullForce * (0.5f + k), false);   // reel in
            }
            transform.Rotate(0f, 0f, spin * (0.6f + k) * Time.fixedDeltaTime);
            SetBody(Color.Lerp(Search, Alert, k), 1.5f + 3f * k);
            transform.localScale = baseScale * (1f + 0.15f * k * Mathf.Sin(t * 26f));

            // a ring that keeps contracting onto the core — reads as "being sucked in".
            float cyc = Mathf.Repeat(t * 1.6f, 1f);
            ring.transform.position = transform.position;
            ring.transform.localScale = Vector3.one * Mathf.Lerp(detectRadius * 1.8f, 0.4f, cyc);
            ring.color = new Color(Alert.r, Alert.g, Alert.b, (1f - cyc) * 0.8f);
            yield return GS.WFFU;
        }
        Destroy(ring.gameObject);

        // 3) detonate point-blank.
        Shockwave.Spawn(transform.position, 2f, 0.03f, 0.5f);
        var burst = Resources.Load<GameObject>("StaticFXBurst");
        if (burst != null) Instantiate(burst, transform.position, Quaternion.Euler(90f, 45f, 0f), GS.FindParent(GS.Parent.misc));
        if (discProjectile != null)
        {
            float baseAng = Random.Range(0f, 360f);
            for (int i = 0; i < discCount; i++)
            {
                Vector2 d = GS.VTheta(baseAng + i * (360f / discCount));
                GS.NewP(discProjectile, transform, TAG, d, discSpread, discStrength);
                if (i % 4 == 3) yield return GS.WFFU;
            }
        }
        transform.localScale = baseScale;
        Consume(0.3f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.5f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
