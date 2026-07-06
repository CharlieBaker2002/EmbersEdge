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
        RunPersistent(E1_4_Main);
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

    // Charge steering feel: how hard sideways momentum is gripped back under the nose (per second),
    // so the body tracks where it points around corners instead of drifting wide into the wall it
    // is turning away from — and the hard cap on turn rate in angle space.
    const float LateralGrip = 8f;
    const float MaxTurnDegPerSec = 170f;

    // Angle-space facing: proportional ease (error × coef per second) under the turn-rate cap.
    // The old Vector2.Lerp facing was weakest exactly when the error was biggest — a near-180°
    // correction barely moved the nose — so corners were rounded at full momentum, not turned.
    void TurnToward(Vector2 want, float coefPerSec, float dt)
    {
        if (want == Vector2.zero || dt <= 0f) return;
        float err = Vector2.SignedAngle(transform.up, want);
        float cap = MaxTurnDegPerSec * dt;
        transform.Rotate(0f, 0f, Mathf.Clamp(err * coefPerSec * dt, -cap, cap));
    }

    // Wind-up facing: nose onto the target only while the BODY has a clear line to it, otherwise
    // onto the pathfinding route — and run for EXACTLY the wind-up window, so no facing tween ever
    // outlives it and fights the charge steering that follows (FaceEnemyOverT did both wrong: raw
    // target position through walls, on a timer longer than the wait).
    IEnumerator FaceRouteFor(float seconds, float coef)
    {
        float refetch = 0f;
        Vector2 pathDir = Vector2.zero;
        while (seconds > 0f)
        {
            if ((refetch -= Time.fixedDeltaTime) <= 0f)
            {
                refetch = 0.1f;   // tight enough that the clearance-blended route tracks the body
                MinePathManager.Decide(this, out pathDir);
            }
            Vector2 want = pathDir;
            if (target != null)
            {
                Vector2 aim = MinePath.AimPoint(target, transform.position);
                if (want == Vector2.zero || MinePath.LineOfSightWide(transform.position, aim, size))
                {
                    want = (aim - (Vector2)transform.position).normalized;
                }
            }
            TurnToward(want, coef, Time.fixedDeltaTime * actRate);
            seconds -= Time.fixedDeltaTime * actRate;
            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator E1_4_Main()
    {
        // the ONE decision, everywhere in this loop: Unit.target + route from the fields — no
        // range, no memory, no physics overlap. Reprioritising onto closer things happens by itself.
        yield return StartCoroutine(FaceRouteFor(1.5f, 5f));
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
            yield return StartCoroutine(FaceRouteFor(1.25f, 3.5f));
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
                    refetch = 0.1f;   // re-decide on a throttle, not once per FixedUpdate
                    MinePathManager.Decide(this, out pathDir);
                }
                float throttle = 1f;
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
                    TurnToward(want, 2.5f, Time.fixedDeltaTime * actRate);
                    // brake into turns: thrust fades as the nose leaves the route, so momentum is
                    // shed BEFORE the corner instead of carried wide through it
                    throttle = Mathf.Clamp01(0.35f + 0.65f * Vector2.Dot(transform.up, want));
                }
                dir = transform.up;
                AS.TryAddForce(9 * timer * actRate * throttle * dir, true);
                // grip: cancel the sideways component of momentum so the body follows the nose
                Vector2 lat = AS.rb.linearVelocity - Vector2.Dot(AS.rb.linearVelocity, dir) * dir;
                AS.TryAddForce(-LateralGrip * actRate * AS.mass * lat, true);
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
                body.SetBool("Charge", true);
                MakeVps();
                yield return StartCoroutine(FaceRouteFor(1.25f, 5f));
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
