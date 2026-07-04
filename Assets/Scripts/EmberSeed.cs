using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// A seed-mine thrown by the Sower (E1_7) as a straight projectile at a spot —
// it only starts arming once it lands. Walls stop it dead: it erupts on
// impact instead of bouncing like a normal round. Pulses faster and brighter
// as it arms, then erupts into a radial spark burst — instantly, if the
// character or an ally unit steps on it. Shooting it before it arms snuffs
// it quietly (projectiles don't set it off).
public class EmberSeed : MonoBehaviour, IOnCollide
{
    public Sprite[] pulseSprs;
    public GameObject spark;
    public Light2D glow;
    public float armTime = 2.8f;
    public int sparkCount = 4;
    public float throwSpeed = 6f;

    private SpriteRenderer sr;
    private LifeScript ls;
    private Collider2D col;
    private Rigidbody2D rb;
    private int allyUnitsLayer;
    private int characterLayer;
    private float t;
    private float phase;
    private bool erupted;
    private bool flying;
    private Vector2 flightEnd;
    private Vector2 flightVel;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ls = GetComponent<LifeScript>();
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        allyUnitsLayer = LayerMask.NameToLayer("Ally Units");
        characterLayer = LayerMask.NameToLayer("Character");
        var AS = GetComponent<ActionScript>();
        if (AS != null && !AS.onCollides.Contains(this))
        {
            AS.onCollides.Add(this);
        }
    }

    // stepping on a seed sets it off — units and the character only, never projectiles
    public void OnCollide(Collision2D c)
    {
        if (erupted || flying || ls.hasDied || c.collider == null) return;
        int l = c.collider.gameObject.layer;
        if (l == allyUnitsLayer || l == characterLayer)
        {
            Erupt();
        }
    }

    // Throw the seed at a spot as a straight projectile. Flight is a fixed-step march (mine tiles
    // carry no physics colliders, so walls are polled in code like ProjectileScript does) — but
    // where a normal round bounces off a wall, the seed ERUPTS on contact.
    public void Throw(Vector2 dest)
    {
        flying = true;
        col.enabled = false;
        flightEnd = dest;
        Vector2 to = dest - (Vector2)transform.position;
        flightVel = (to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.up) * throwSpeed;
    }

    private void FixedUpdate()
    {
        if (!flying || erupted) return;
        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 next = pos + flightVel * Time.fixedDeltaTime;
        if (MinePath.IsWallAt(next))   // slammed into a wall — never skips through, goes off on impact
        {
            flying = false;
            Erupt();
            return;
        }
        bool arrived = Vector2.Dot(flightEnd - next, flightVel) <= 0f;
        if (arrived) next = flightEnd;
        if (rb != null) rb.position = next; else transform.position = next;
        if (arrived)
        {
            col.enabled = true;
            flying = false;
        }
    }

    private void Update()
    {
        if (erupted || flying) return;
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / armTime);
        phase += Time.deltaTime * (1.5f + 8f * a * a); // blink accelerates as it arms
        float pulse = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
        sr.sprite = GS.PercentParameter(pulseSprs, Mathf.Clamp01(pulse * (0.3f + 0.7f * a)));
        transform.localScale = Vector3.one * (1f + 0.08f * a * Mathf.Sin(phase * Mathf.PI * 2f));
        if (glow != null)
        {
            glow.lightCookieSprite = sr.sprite;
            glow.intensity = 0.25f + a * (0.35f + 0.65f * pulse);
        }
        if (t >= armTime)
        {
            Erupt();
        }
    }

    private void Erupt()
    {
        erupted = true;
        float baseAng = Random.Range(0f, 90f);
        for (int i = 0; i < sparkCount; i++)
        {
            var p = Instantiate(spark, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(GS.VTheta(baseAng + i * (360f / sparkCount)), tag);
        }
        Shockwave.Spawn(transform.position, 1.1f, 0.012f, 0.4f);
        ls.OnDie();
    }
}
