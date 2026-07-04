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
        // steer at the roam point when the body fits the straight line, else ride the field
        void Steer(Vector3 point, Vector2 pathDir)
        {
            Vector2 dir = ((Vector2)(point - transform.position)).normalized;
            if (!MinePath.LineOfSightWide(transform.position, point, size) && pathDir != Vector2.zero)
            {
                dir = pathDir;
            }
            AS.rb.linearVelocity = Vector3.Lerp(AS.rb.linearVelocity, 4f * actRate * dir, 0.5f);
        }
        while (true)
        {
            for(int i = Random.Range(0,3); i < 3; i++)
            {
                MinePathManager.Decide(this, out Vector2 pathDir);
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
                    Steer(point, pathDir);
                    float guard = 5f;    // roam points can be inside geometry — give up rather than grind
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
                            MinePathManager.Decide(this, out pathDir);
                            if (target == null) break;
                            Steer(point, pathDir);
                        }
                    }
                }
                yield return StartCoroutine(WaitForActSeconds(0.5f));
            }
            T = GS.FindNearestEnemy(tag, transform.position, 6.5f, false);
            if (T != null)
            {
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
                    this.QA(()=>GS.Stat(this,"immaterial",1.5f),0.75f);
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
                    yield return new WaitForSeconds(0.55f);
                    this.QA(()=>GS.Stat(this,"immaterial",1.5f),0.5f);
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
