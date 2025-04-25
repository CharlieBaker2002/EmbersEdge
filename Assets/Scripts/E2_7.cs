using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E2_7 : Unit
{
    public GameObject p0;
    public GameObject p1;
    private Vector2 pos = Vector2.zero;
    private Transform e;
    float stateTimer = 3f;
    public Transform[] launch;
    private static readonly int Shoot = Animator.StringToHash("Shoot");
    private static readonly int Bite = Animator.StringToHash("Bite");

    protected override void Start()
    {
        base.Start();
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        while (true)
        {
            e = GS.FindNearestEnemy(tag, transform.position, 7f, true);
            if (e != null)
            {
                while (e != null)
                {
                    pos = e.position + (Vector3)e.GetComponentInParent<Rigidbody2D>().velocity;
                    if (Vector2.Distance(pos, transform.position) > 7f)
                    {
                        pos = (pos - (Vector2)transform.position).normalized * 8 + (Vector2)transform.position;
                    }

                    AS.FaceEnemyOverT(1f, 2.5f * actRate, e, true, true);
                    yield return new WaitForSeconds(1f);
                }
            }
            else
            {
                pos = (Vector2)transform.position + GS.RandCircleV2(5f, 7f);
                AS.FaceDirectionOverT((Vector2)transform.position - pos, 1f, 5f * actRate);
                yield return new WaitForSeconds(5f);
            }

            yield return null;
        }
    }

    protected override void Update()
    {
        base.Update();
        stateTimer -= Time.deltaTime;
        if(stateTimer <= 0f)
        {
            if(Random.Range(0,2) == 0)
            {
                anim.SetBool(Shoot, true);
                stateTimer = 4f + Random.Range(0f, 6f);
            }
            else
            {
                anim.SetBool(Bite, true);
                stateTimer = 2f + Random.Range(0f, 7f);
            }
        }
    }

    public void Move()
    {
        AS.AddPush(0.5f, false, actRate * 5 * Random.Range(1f,2f) * (pos - (Vector2)transform.position));
    }

    public void BigMove()
    {
        AS.AddPush(0.5f, false, actRate * 10 * Random.Range(1f, 2f) * (pos - (Vector2)transform.position));
    }

    public void P0()
    {
        foreach(Transform t in launch)
        {
            Instantiate(p0.GetComponent<E2_7P0>(),t.position,t.rotation,GS.FindParent(GS.Parent.enemyprojectiles)).SetDestination(pos + Random.insideUnitCircle * 4f);
        }
    }

    public void P1()
    {
        GS.NewP(p1, transform, tag, -transform.up, 0f, ActRateProjectileStrength());
    }

    public void P1Fast()
    {
        GS.NewP(p1, transform, tag, -transform.up, 0, 5 + ActRateProjectileStrength());
    }
}
