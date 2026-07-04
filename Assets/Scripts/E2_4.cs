using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class E2_4 : Unit, IRoomUnit
{
    private Collider2D bounds;
    private Vector3 goTo;
    public Collider2D col;
    private Transform T = null;
    [SerializeField] private Rotator[] rots;

    public void RecieveRoom(Collider2D boundsp, Vector2 pos)
    {
        bounds = boundsp;
        goTo = pos;
    }

    private void Awake()
    {
        anim.speed = 1f;
        // Ghost-drifter: never avoids or collides with terrain — phases straight through rock,
        // walls and buildings (set before ActionScript.Start so it skips MineField registration).
        GetComponent<ActionScript>().ignoreWalls = true;
    }

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E2_4I());
    }

    private IEnumerator E2_4I()
    {
        yield return null;
        yield return null;
        // ghost drift: straight at the point, through whatever terrain is in the way — the spinner
        // never walks routes (Decide is used for TARGETING only) and never avoids walls
        void Steer(Vector3 point)
        {
            Vector2 dir = ((Vector2)(point - transform.position)).normalized;
            AS.rb.linearVelocity = Vector3.Lerp(AS.rb.linearVelocity, 4f * actRate * dir, 0.5f);
        }
        while (true)
        {
            for(int i = Random.Range(0,3); i < 3; i++)
            {
                MinePathManager.Decide(this, out _);
                if (target == null)
                {
                    // nothing to fight anywhere — aimless drift
                    AS.rb.linearVelocity = Vector3.Lerp(AS.rb.linearVelocity,2f * actRate * Random.Range(1f, 1.5f) * Random.insideUnitCircle.normalized, 0.5f);
                    yield return new WaitForSeconds(1f);
                    AS.Stop();
                }
                else
                {
                    // unanchored: flit between points in a wide band around the current objective
                    Vector3 point = target.position + GS.RandCircle(2f, 7f);
                    Steer(point);
                    float guard = 5f;    // roam points can sit in solid rock — that's fine, it phases through
                    float steerT = 0.4f;
                    while ((transform.position - point).sqrMagnitude > 1 && guard > 0f)
                    {
                        yield return new WaitForFixedUpdate();
                        guard -= Time.fixedDeltaTime;
                        steerT -= Time.fixedDeltaTime;
                        if(Random.Range(0,100) == 0 && target != null)
                        {
                            point = target.position + GS.RandCircle(2f, 7f);
                            steerT = 0f;
                        }
                        if (steerT <= 0f)
                        {
                            steerT = 0.4f;
                            MinePathManager.Decide(this, out _);
                            if (target == null) break;
                            // something attackable is already in strike range — cut the roam short
                            // and go attack instead of finishing every remaining leg first
                            if (GS.FindNearestEnemy(tag, transform.position, 6.5f, false) != null)
                            {
                                i = 3;
                                break;
                            }
                            Steer(point);
                        }
                    }
                }
                yield return StartCoroutine(WaitForActSeconds(0.5f));
            }
            T = GS.FindNearestEnemy(tag, transform.position, 6.5f, false);
            if (T != null)
            {
                // attacking = phased: immaterial for the WHOLE engage (approach through lunge), so
                // it slips through terrain and bodies while striking. Re-applied per phase — GS.Stat
                // merges into the existing status, so the windows chain without a gap.
                GS.Stat(this, "immaterial", 1.6f);
                float t = 1.5f;
                while (t > 0f)
                {
                    t -= Time.fixedDeltaTime * actRate;
                    if (T == null)
                    {
                        break;
                    }
                    AS.TryAddForceToward(T.position, 7*actRate, 5, 3);
                    yield return new WaitForFixedUpdate();
                }
                if(Random.Range(0,5) < 2) //40% chance
                {
                    anim.SetBool("Teleport", true);
                    GS.Stat(this, "immaterial", 5f);   // 1s windup + 2s chase + 0.35s pause + 1.5s lunge
                    yield return new WaitForSeconds(1f);
                    GS.Stat(this,"dodging",1.5f);
                    t = 2f;
                    while (t > 0f)
                    {
                        t -= Time.fixedDeltaTime;
                        if (T == null)
                        {
                            break;
                        }
                        AS.TryAddForceToward(T.position, 15 * actRate, 8, 0);
                        yield return new WaitForFixedUpdate();
                    }
                    AS.Stop();
                    yield return new WaitForSeconds(0.35f);
                    if (T != null)
                    {
                        AS.AddPush(1f, false, actRate * ((T.position - transform.position).normalized * 4f + (Vector3)GS.VectInRange(Vector2.Distance(transform.position, T.position) * T.GetComponentInParent<Rigidbody2D>().linearVelocity, 0.25f, 2f)));
                    }
                    yield return new WaitForSeconds(1.5f);
                    AS.Stop();
                }
                else //60% chance
                {
                    anim.SetBool("Expand", true);
                    GS.Stat(this, "immaterial", 2.2f);   // 0.55s windup + 1.5s strike
                    yield return new WaitForSeconds(0.55f);
                    if(T!= null)
                    {
                        AS.AddPush(1f, false, actRate * ((T.position - transform.position).normalized * 4f + (Vector3)GS.VectInRange(Vector2.Distance(transform.position, T.position) * T.GetComponentInParent<Rigidbody2D>().linearVelocity, 0.25f, 2f)));
                    }
                    yield return new WaitForSeconds(1.5f);
                }
            }
            yield return StartCoroutine(WaitForActSeconds(1f));
        }
    }

    public void VP()
    {
        GS.VP(0, transform, transform.position, 50);
    }

    public override void UpdateActRate()
    {
        base.UpdateActRate();
        for(int i = 0; i < rots.Length; i++)
        {
            rots[i].omega = actRate * 36f * (i+1) * Mathf.Pow(-1, i);
        }

        if (actRate == 0)
        {
            //INSTANTLY SET ANIMATOR CURRENT STATE TO E2_3PULSE (index 0)
            anim.Play("E2_3Pulse", 0, 0);
        }
    }
    
    
}
