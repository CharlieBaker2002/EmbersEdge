using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_4 : Unit, IOnCollide
{
    public Animator engine;
    public Animator body;
    [HideInInspector]
    float timer;
    public Transform[] ts;
    public GameObject p0;
    public GameObject p1;
    Vector2 dir = Vector2.down;
    public GameObject vp;
    public GameObject bigVp;
    public ParticleSystem pfx;
    bool charging = false;

    private void Awake()
    {
        AS = GetComponent<ActionScript>();
    }

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E1_4_Main());
    }

    // Every shot is gated on projectile-width LOS to the target — the volley loop, spin sprays and
    // animation events all funnel through here, so none of them can fire into a wall the target is
    // hiding behind (no target at all also means no shot).
    private bool CanHitTarget(Vector2 muzzle) => MinePath.CanSee(muzzle, target, 0.15f);

    public void ShootSmall(int ind)
    {
        if (!CanHitTarget(ts[ind].position)) return;
        for(int i = Random.Range(0,4); i < 4; i++)
        {
            GS.NewP(p0, ts[ind], tag, 0.4f, 2*Random.value + ActRateProjectileStrength());
        }
    }

    public void ShootBig()
    {
        if (!CanHitTarget(transform.position)) return;
        GS.NewP(p1, transform,tag, 0.1f, ActRateProjectileStrength());
    }

    private void MakeVps()
    {
        foreach(Transform t in ts)
        {
            if(Random.Range(0,4) == 0)
            {
                Instantiate(vp, t.position, t.rotation, transform);
            }
        }
    }

    IEnumerator E1_4_Main()
    {
        // the ONE decision, everywhere in this loop: Unit.target + route from the fields — no
        // range, no memory, no physics overlap. Reprioritising onto closer things happens by itself.
        MinePathManager.Decide(this, out _);
        AS.FaceEnemyOverT(1.5f,5 * actRate, target, true);
        yield return StartCoroutine(WaitForActSeconds(1.5f));
        while (true)
        {
            MinePathManager.Decide(this, out _);
            if (target == null)
            {
                // nothing left to fight anywhere — hold position instead of charging at nothing
                AS.Stop();
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            AS.FaceEnemyOverT(2f, 3.5f * actRate, target, true);
            yield return new WaitForSeconds(1.25f);
            if (Random.Range(0, 2) == 0)
            {
                Instantiate(bigVp, transform.position + transform.up * 0.5f, transform.rotation, transform);
            }
            engine.SetBool("Ignite", true);
            timer = 2f;
            charging = true;
            float refetch = 0f;
            Vector2 pathDir = Vector2.zero;
            while (timer >= 0f)
            {
                if ((refetch -= Time.fixedDeltaTime) <= 0f)
                {
                    refetch = 0.25f;   // re-decide on a throttle, not once per FixedUpdate
                    MinePathManager.Decide(this, out pathDir);
                }
                if (target != null)
                {
                    // steer mid-charge: swing the nose onto the target while the BODY has a clear
                    // line to it (wall targets aim at their nearest span point). No line — charge
                    // down the pathfinding route instead (which may deliberately head INTO a
                    // chewable wall), never nose-first into rock the route walks around.
                    Vector2 aim = MinePath.AimPoint(target, transform.position);
                    Vector2 want = (aim - (Vector2)transform.position).normalized;
                    if (!MinePath.LineOfSightWide(transform.position, aim, size) && pathDir != Vector2.zero)
                    {
                        want = pathDir;
                    }
                    transform.up = Vector2.Lerp(transform.up, want, Mathf.Min(1, 2.5f * Time.fixedDeltaTime * actRate));
                }
                dir = transform.up;
                AS.TryAddForce(9 * timer * actRate * dir, true);
                timer -= Time.fixedDeltaTime * actRate;
                yield return new WaitForFixedUpdate();
            }
            charging = false;
            AS.Decelerate(1.5f, 0.935f);
            engine.SetBool("Ignite", false);
            MinePathManager.Decide(this, out _);
            // volley only when the objective is close AND the shots can actually reach it —
            // never spray-and-spin at a wall the target is hiding behind
            if (target != null && Vector2.Distance(MinePath.AimPoint(target, transform.position), transform.position) < 12f
                && MinePath.CanSee(transform.position, target, 0.15f))
            {
                timer = 2.25f;
                StartCoroutine(ShootSpin());
                while(timer > 0f)
                {
                    transform.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + 280f * Time.deltaTime * actRate);
                    timer -= Time.deltaTime * actRate;
                    yield return null;
                }
                MinePathManager.Decide(this, out _);
                AS.FaceEnemyOverT(1.5f, 5, target, true);
                body.SetBool("Charge", true);
                MakeVps();
                yield return StartCoroutine(WaitForActSeconds(1.25f));
            }
            else
            {
                // objective far — don't waste the volley, just rest and wind up the next charge
                body.SetBool("Charge", true);
                yield return StartCoroutine(WaitForActSeconds(1.5f));
            }
        }
    }

    private IEnumerator ShootSpin()
    {
        float c = 0f;
        for (float t = 0f; t < 2.25f; t += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            c += actRate * 20f;
            if (!GS.Chance(c)) continue;
            c -= 100f;
            ShootSmall(Random.Range(0, 4));
        }
    }
    
    public void OnCollide(Collision2D collision)
    {
        if(charging)
        {
            if (AS.CheckWall(collision.transform))
            {
                timer = -1f;
            }
        }
    }
}
