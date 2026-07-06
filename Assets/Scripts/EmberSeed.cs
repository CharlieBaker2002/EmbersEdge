using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// A seed-mine thrown by the Sower (E1_7) as a straight projectile at a spot —
// a real rigidbody throw like any other round (collider live, physics velocity),
// it only starts arming once it lands. Walls stop it dead: it erupts on
// impact instead of bouncing like a normal round — and so does slamming into
// the character or an ally unit mid-flight. Pulses faster and brighter
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
    private Rigidbody2D rb;
    private ActionScript AS;
    private int allyUnitsLayer;
    private int characterLayer;
    private float t;
    private float phase;
    private bool erupted;
    private bool flying;
    private Vector2 flightEnd;
    private float flightTimeLeft;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ls = GetComponent<LifeScript>();
        rb = GetComponent<Rigidbody2D>();
        allyUnitsLayer = LayerMask.NameToLayer("Ally Units");
        characterLayer = LayerMask.NameToLayer("Character");
        AS = GetComponent<ActionScript>();
        if (AS != null && !AS.onCollides.Contains(this))
        {
            AS.onCollides.Add(this);
        }
    }

    // Stepping on a seed sets it off — units and the character only, never projectiles.
    // In flight the same contacts count as a direct bomb hit, and slamming into a physics
    // wall (base walls / buildings tagged Walls) goes off instead of bouncing.
    public void OnCollide(Collision2D c)
    {
        if (erupted || ls.hasDied || c.collider == null) return;
        int l = c.collider.gameObject.layer;
        if (l == allyUnitsLayer || l == characterLayer)
        {
            Erupt();
            return;
        }
        if (flying && (c.collider.CompareTag("Walls") ||
            (c.rigidbody != null && c.rigidbody.TryGetComponent<ActionScript>(out var oas) && oas.wall)))
        {
            Erupt();
        }
    }

    // Throw the seed at a spot the way a normal round is fired: a real rigidbody velocity with the
    // collider live (ProjectileScript.SetValues does the same), so in the base it collides like any
    // projectile. It stops and starts arming when it reaches the landing spot; dungeon ore carries no
    // physics colliders, so those walls are polled in code — but where a normal round bounces off a
    // wall, the seed ERUPTS on contact.
    public void Throw(Vector2 dest)
    {
        flying = true;
        flightEnd = dest;
        Vector2 to = dest - (Vector2)transform.position;
        Vector2 dir = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.up;
        flightTimeLeft = to.magnitude / throwSpeed + 0.5f;   // deflection backstop — never flies forever
        if (AS != null) AS.maxVelocity = Mathf.Max(AS.maxVelocity, throwSpeed);   // prefab clamp is walk-speed
        rb.linearVelocity = dir * throwSpeed;
        transform.up = dir;
    }

    private void FixedUpdate()
    {
        if (!flying || erupted) return;
        Vector2 vel = rb.linearVelocity;
        Vector2 next = rb.position + vel * Time.fixedDeltaTime;
        if (MinePath.IsWallAt(next))   // slammed into mine ore — never skips through, goes off on impact
        {
            flying = false;
            Erupt();
            return;
        }
        flightTimeLeft -= Time.fixedDeltaTime;
        if (Vector2.Dot(flightEnd - next, vel) <= 0f || flightTimeLeft <= 0f)   // landed
        {
            rb.linearVelocity = Vector2.zero;
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
