using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The Sower: a drifting seed-pod gardener. Orbits its target at mid range and
// periodically plants Ember Seeds (arming mines that erupt into spark bursts).
// When hurt it panics: vents a pair of sparks and dashes away. Script-driven
// sprites (no Animator) — idle drift loop + a one-shot sow animation.
public class E1_7 : Unit
{
    [Header("Sower")]
    public Sprite[] idleSprs;
    public Sprite[] sowSprs;
    public GameObject seed;
    public GameObject spark;
    public int maxSeeds = 3;
    public float orbitRadius = 2.4f;

    private float sowTimer;
    private float jinkTimer;
    private float panicReadyAt;
    private float animT;
    private float orbitSign = 1f;
    private bool sowing;
    private readonly List<GameObject> seeds = new List<GameObject>();

    private void Awake()
    {
        AS = GetComponent<ActionScript>();
        sowTimer = Random.Range(2f, 3.5f);
        jinkTimer = Random.Range(1.1f, 1.9f);
        orbitSign = GS.PlusMinus();
        if (!transform.InDungeon())
        {
            transform.up = -((Vector2)transform.position).normalized;
        }
    }

    protected override void Start()
    {
        base.Start();
        ls.onDamageDelegate += OnDamaged;
    }

    protected override void Update()
    {
        base.Update();
        if (!sowing)
        {
            animT += Time.deltaTime * actRate * 1.1f;
            if (animT >= 1f) animT -= 1f;
            sr.sprite = GS.PercentParameter(idleSprs, animT);
        }

        Move();

        if (sowing) return;
        sowTimer -= Time.deltaTime * actRate;
        if (sowTimer < 0f)
        {
            seeds.RemoveAll(x => x == null);
            if (target != null && seeds.Count < maxSeeds &&
                Vector2.Distance(MinePath.AimPoint(target, transform.position), transform.position) < orbitRadius + 2.5f)
            {
                StartCoroutine(SowI());
            }
            else
            {
                sowTimer = 1f;
            }
        }
    }

    private void Move()
    {
        // the ONE decision: target + route from the same field snapshot — no range, no memory.
        // Null only when nothing ally-side is left at all; then just hold position.
        MinePathManager.Decide(this, out Vector2 pathDir);
        if (target == null) return;
        Vector2 aim = MinePath.AimPoint(target, transform.position);   // wall targets: nearest span point
        Vector2 toT = aim - (Vector2)transform.position;
        float dist = toT.magnitude;
        Vector2 tang = new Vector2(-toT.y, toT.x).normalized * orbitSign;
        // orbit behaviour (ring spring + tangential drift) only near the ring; far out it just
        // closes in flat-out, else the shallow spiral reads as hanging back from the objective
        float near = Mathf.Clamp01(1f - (dist - orbitRadius - 1f) / 2f);
        Vector2 force = toT.normalized * Mathf.Lerp(0.09f, Mathf.Clamp((dist - orbitRadius) * 0.05f, -0.09f, 0.09f), near)
                      + tang * (0.065f * near);
        // orbit only when the BODY has a clear line to the target (size-wide, not sight-wide) —
        // else drift down the pathfinding route (which may deliberately head INTO a chewable wall)
        if (!MinePath.LineOfSightWide(transform.position, aim, size) && pathDir != Vector2.zero)
        {
            force = pathDir * 0.09f;
            near = 0f;
        }
        if (!sowing)
        {
            AS.TryAddForce(actRate * force, true);
            // evasive jink: quick sideways dart every couple of seconds
            jinkTimer -= Time.deltaTime * actRate;
            if (jinkTimer < 0f)
            {
                jinkTimer = Random.Range(1.1f, 1.9f);
                if (GS.Chance(30f))
                {
                    orbitSign = -orbitSign;
                }
                if (near > 0.5f) // jinks are an orbit habit — don't dart off-route while closing in
                {
                    AS.TryAddForce(tang.normalized * Random.Range(60f, 90f), false); // ~1.2-1.8 u/s dart
                }
            }
            if (AS.rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(AS.rb.linearVelocity, Vector2.up));
                transform.rotation = Quaternion.Lerp(transform.rotation, q, 0.08f * actRate);
            }
        }
    }

    private IEnumerator SowI()
    {
        sowing = true;
        AS.Decelerate(0.5f, 0.5f);
        // one in three: lob the seed at the target instead of dropping it.
        // Telegraph the throw by swivelling to stare the victim down first.
        bool lob = target != null && Random.Range(0, 3) == 0;
        if (lob)
        {
            for (float f = 0f; f < 0.45f; f += Time.deltaTime)
            {
                if (target == null) { lob = false; break; }
                Quaternion q = Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(MinePath.AimPoint(target, transform.position) - (Vector2)transform.position, Vector2.up));
                transform.rotation = Quaternion.Lerp(transform.rotation, q, 0.22f * actRate);
                yield return null;
            }
        }
        StartCoroutine(GS.Animate(sr, sowSprs, 0.5f, false)); // coroutine, not LeanTween (pool pressure)
        yield return WFAS(0.22f);
        var s = Instantiate(seed, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
        seeds.Add(s);
        if (lob && target != null)
        {
            Vector2 dest = MinePath.AimPoint(target, transform.position) + Random.insideUnitCircle * 0.6f;
            s.GetComponent<EmberSeed>().Lob(dest);
        }
        yield return WFAS(0.25f);
        sowing = false;
        animT = 0f;
        orbitSign = GS.PlusMinus();
        sowTimer = Random.Range(2.8f, 4.4f);
    }

    // Panic: vent two sparks sideways and dash away from the aggressor's side.
    private void OnDamaged(float value)
    {
        if (value >= 0f || ls.hasDied || Time.time < panicReadyAt) return;
        panicReadyAt = Time.time + 7f;
        Vector2 away = target != null ? (Vector2)(transform.position - target.position) : Random.insideUnitCircle;
        away.Normalize();
        Vector2 perp = new Vector2(-away.y, away.x);
        foreach (Vector2 dir in new[] { perp, -perp })
        {
            var p = Instantiate(spark, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(dir, tag, ActRateProjectileStrength());
        }
        AS.TryAddForce(away * 120f, false); // ~2.4 u/s panic dash
    }
}
