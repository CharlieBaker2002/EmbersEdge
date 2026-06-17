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

    private Transform t;
    private Collider2D[] cols;
    private float seekTimer = 1f;
    private float sowTimer;
    private float jinkTimer;
    private float panicReadyAt;
    private float animT;
    private float orbitSign = 1f;
    private bool sowing;
    private Vector2 d = new Vector2(0f, -1f);
    private readonly List<GameObject> seeds = new List<GameObject>();

    private void Awake()
    {
        cols = new Collider2D[2];
        AS = GetComponent<ActionScript>();
        sowTimer = Random.Range(2f, 3.5f);
        jinkTimer = Random.Range(1.1f, 1.9f);
        orbitSign = GS.PlusMinus();
        if (!transform.InDungeon())
        {
            transform.up = -((Vector2)transform.position).normalized;
            d = transform.up * 2f;
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

        seekTimer -= Time.deltaTime * actRate;
        if (seekTimer < 0f)
        {
            seekTimer = 1f;
            t = GS.FindEnemy(transform, 6.5f, GS.searchType.allSearch, cols, t);
        }

        if (sowing) return;
        sowTimer -= Time.deltaTime * actRate;
        if (sowTimer < 0f)
        {
            seeds.RemoveAll(x => x == null);
            if (t != null && seeds.Count < maxSeeds &&
                Vector2.Distance(t.position, transform.position) < orbitRadius + 2.5f)
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
        if (t != null)
        {
            Vector2 toT = t.position - transform.position;
            float dist = toT.magnitude;
            // spring onto the orbit ring + tangential drift around it
            Vector2 tang = new Vector2(-toT.y, toT.x).normalized * orbitSign;
            Vector2 force = toT.normalized * Mathf.Clamp((dist - orbitRadius) * 0.05f, -0.09f, 0.09f) + tang * 0.065f;
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
                    AS.TryAddForce(tang.normalized * Random.Range(60f, 90f), false); // ~1.2-1.8 u/s dart
                }
            }
            if (!sowing && AS.rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(AS.rb.linearVelocity, Vector2.up));
                transform.rotation = Quaternion.Lerp(transform.rotation, q, 0.08f * actRate);
            }
        }
        else
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(d, Vector2.up)), actRate * 0.05f);
            AS.TryAddForce(d * (actRate * 0.07f), true);
        }
    }

    private IEnumerator SowI()
    {
        sowing = true;
        AS.Decelerate(0.5f, 0.5f);
        // one in three: lob the seed at the target instead of dropping it.
        // Telegraph the throw by swivelling to stare the victim down first.
        bool lob = t != null && Random.Range(0, 3) == 0;
        if (lob)
        {
            for (float f = 0f; f < 0.45f; f += Time.deltaTime)
            {
                if (t == null) { lob = false; break; }
                Quaternion q = Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(t.position - transform.position, Vector2.up));
                transform.rotation = Quaternion.Lerp(transform.rotation, q, 0.22f * actRate);
                yield return null;
            }
        }
        StartCoroutine(GS.Animate(sr, sowSprs, 0.5f, false)); // coroutine, not LeanTween (pool pressure)
        yield return WFAS(0.22f);
        var s = Instantiate(seed, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
        seeds.Add(s);
        if (lob && t != null)
        {
            Vector2 dest = (Vector2)t.position + Random.insideUnitCircle * 0.6f;
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
        Vector2 away = t != null ? (Vector2)(transform.position - t.position) : Random.insideUnitCircle;
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
