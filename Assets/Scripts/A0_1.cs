using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class A0_1 : AllyAI
{
    //Movement based on targetPoint.
    //Upgrades: Higher cap. Agility. 2 projectiles, shoot better proj.
    public GameObject zap;
    public GameObject piercer;
    private Transform e;
    public ParticleSystem ps;
    [HideInInspector]
    public int shootN = 1;
    [HideInInspector]
    public float shootSqrDist = 6f;
    [HideInInspector]
    public float detectionRange = 6f;

    protected override void Start()
    {
        base.Start();
        StartCoroutine(A01());
    }

    public void ShootAHoe()
    {
        if(shootN == 4)
        {
            GS.NewP(piercer, transform, tag,0.5f);
        }
        else
        {
            StartCoroutine(Shoot());
        }
        anim.SetBool("Shoot", false);
    }

    private IEnumerator Shoot()
    {
        for(int i = 0; i < shootN; i++)
        {
            GS.NewP(zap, transform, tag, 0.75f,0,shootN);
            yield return new WaitForSeconds(0.1f);
        }
    }

    IEnumerator A01()
    {
        while (true)
        {
            e = GS.FindNearestEnemy(tag, transform.position, detectionRange, false, false);
            if (detectionRange == 9f) { ps.Play(); }
            if (e != null)
            {
                float t = 2f;
                AS.FaceEnemyOverT(3f, detectionRange, e, false);
                while (GS.SqrDist(e, transform) > shootSqrDist)
                {
                    t -= Time.deltaTime;
                    if (t <= 0f || e == null)
                    {
                        break;
                    }
                    AS.TryAddForce((detectionRange - 2) * 0.1f * GS.VectInRange(e.position - transform.position,1f,7f), true);
                    yield return null;
                }
                ps.Stop();
                AS.Decelerate(0.55f, 1 - detectionRange * 0.1f);
                if (e != null)
                {
                    anim.SetBool("Shoot", true);
                    yield return new WaitForSeconds(0.75f);
                }
            }
            yield return StartCoroutine(MoveToSpot());
            yield return new WaitForSeconds(1.2f - detectionRange/9);
        }
    }

    IEnumerator MoveToSpot()
    {
        Vector2 point = new Vector2(targetPoint.x,targetPoint.y);
        if (detectionRange == 9) { ps.Play(); }
        float t2 = 2f;
        AS.FaceDirectionOverT(point - (Vector2)transform.position, 0.75f,10f);
        while((transform.position - (Vector3)point).sqrMagnitude > 1f)
        {
            t2 -= Time.deltaTime;
            if (t2 <= 0f)
            {
                break;
            }
            AS.TryAddForce((detectionRange - 2) * 0.1f * GS.VectInRange(point - (Vector2)transform.position,1f,7f), true);
            yield return null;
        }
        ps.Stop();
    }
}
