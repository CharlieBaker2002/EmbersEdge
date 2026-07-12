using System.Collections;
using UnityEngine;

/// <summary>
/// (1) Air-Drop Beacon. Invisible until the player strays within `detectRadius`, then a targeting
/// reticle appears and HUNTS the player across the floor — signalling an incoming drop. The reticle
/// keeps chasing until the player stops moving; `dropDelay` seconds after they go still, a package
/// erupts from under the floor at the locked spot and bursts a Discer-style ring of disc projectiles.
/// Single use — self-destructs after the burst.
/// </summary>
public class AirDropBeacon : SentryExtra
{
    [Header("Beacon")]
    public GameObject discProjectile;   // reused Discer disc (BossP0)
    public float detectRadius = 4f;
    public float stopSpeed = 0.7f;      // player counts as "stopped" below this speed
    public float stopHold = 0.25f;      // ...and must hold still this long to commit the drop
    public float dropDelay = 2f;        // telegraph after the player stops, before the package lands
    [Header("Payload")]
    public int discCount = 12;
    public float discSpread = 10f;      // per-disc angular jitter (NewP innaccuracy)
    public float discStrength = 0f;     // scales disc speed/damage (ProjectileScript.SetValues)

    protected override void OnReady()
    {
        wallMounted = false;            // an interior hunter
        if (body != null) body.enabled = false;   // unseen while dormant
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // 1) lie in wait until the player wanders close.
        while (DistToPlayer > detectRadius) yield return null;

        // 2) a reticle blooms in and chases the player — "a drop is coming".
        SpriteRenderer ret = MakeSprite("Reticle", Reticle(), Alert, 1.6f, order: 7);
        ret.transform.position = PlayerPos;
        Vector2 lockPos = PlayerPos;
        float held = 0f;
        while (true)
        {
            lockPos = Vector2.Lerp(lockPos, PlayerPos, Time.deltaTime * 4.5f);   // follow the player
            ret.transform.position = lockPos;
            float spin = (1f + 2.5f * Mathf.Sin(Time.time * 6f));
            ret.transform.localScale = Vector3.one * (1.6f + 0.12f * Mathf.Sin(Time.time * 9f));
            ret.color = Color.Lerp(Search, Alert, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f));

            if (PlayerSpeed < stopSpeed) { held += Time.deltaTime; if (held >= stopHold) break; }
            else held = 0f;
            yield return null;
        }

        // 3) committed: a tense `dropDelay` telegraph — the reticle shrinks onto the point and flashes
        //    ever faster, marking exactly where the package will hit.
        for (float t = 0f; t < dropDelay; t += Time.deltaTime)
        {
            float k = t / dropDelay;
            float flash = Mathf.Sin(t * (10f + 40f * k));
            ret.transform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1.0f, k);
            ret.color = Color.Lerp(Alert, Color.white, 0.5f + 0.5f * flash);
            yield return null;
        }
        Destroy(ret.gameObject);

        // 4) impact.
        yield return StartCoroutine(DropPackage(lockPos));
        Consume(0.3f);
    }

    IEnumerator DropPackage(Vector2 pos)
    {
        Shockwave.Spawn(pos, 1.5f, 0.022f, 0.45f);

        // the package heaves up out of the floor
        var pkg = new GameObject("DropPackage");
        pkg.transform.position = pos;
        pkg.transform.localScale = Vector3.zero;
        var psr = pkg.AddComponent<SpriteRenderer>();
        if (body != null) { psr.sprite = body.sprite; psr.sharedMaterial = body.sharedMaterial; }
        else psr.sprite = Dot();
        psr.color = Alert;
        psr.sortingLayerName = "Power Ups";
        psr.sortingOrder = 6;
        for (float f = 0f; f < 1f; f += Time.deltaTime / 0.18f)
        {
            pkg.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1.3f, f);
            pkg.transform.Rotate(0f, 0f, 540f * Time.deltaTime);
            yield return null;
        }

        var burst = Resources.Load<GameObject>("StaticFXBurst");
        if (burst != null) Instantiate(burst, pos, Quaternion.Euler(90f, 45f, 0f), GS.FindParent(GS.Parent.misc));

        // disc ring, staggered like the Discer's "Boom".
        if (discProjectile != null)
        {
            float baseAng = Random.Range(0f, 360f);
            for (int i = 0; i < discCount; i++)
            {
                Vector2 dir = GS.VTheta(baseAng + i * (360f / discCount));
                GS.NewP(discProjectile, pkg.transform, TAG, dir, discSpread, discStrength);
                if (i % 3 == 2) yield return GS.WFFU;
            }
        }

        // settle and sink away
        for (float f = 0f; f < 1f; f += Time.deltaTime / 0.4f)
        {
            var c = psr.color; c.a = 1f - f; psr.color = c;
            pkg.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.9f, f);
            yield return null;
        }
        Destroy(pkg);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
