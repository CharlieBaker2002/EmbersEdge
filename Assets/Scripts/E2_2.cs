using System.Collections;
using UnityEngine;

public class E2_2 : Unit
{
    public ProjectileScript PS;
    float tim = 1f;
    Transform t;
    Vector2 d;
    public LA la;

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E2_2Cycle());
        transform.localRotation = Quaternion.identity;
    }

    IEnumerator E2_2Cycle()
    {
        while (true)
        {
            t = GS.FindNearestEnemy(tag, transform.position, 7.5f, false);
            if (t != null)
            {
                tim = Time.time;
                Vector3 rel = GS.RandCircle(1.5f, 2.5f);
                while (t != null)
                {
                    AS.TryAddForceToward(t.position + rel, 5f * actRate, 5f, 1f);
                    if (Time.time - tim >= 3f * InvActRate())
                    {
                        break;
                    }
                    yield return new WaitForFixedUpdate();
                }
                la.FadeInQuick();
                GS.VP(1, transform, transform.position, 50);
                yield return WFAS(0.15f);
                Vector2 v = (t != null) ? ((Vector2)(transform.position - t.position)).Rotated(20) : Random.insideUnitCircle;
                for(int i = 0; i < 4; i++)
                {
                    Instantiate(PS, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles)).SetValues(v, tag,ActRateProjectileStrength());
                    v = v.Rotated(-15);
                    yield return WFAS(0.065f);
                }
                la.FadeOutQuick();
                yield return StartCoroutine(WaitForActSeconds(0.5f));
            }
            else
            {
                AS.AddPush(1f, false, GS.RandCircleV2(0.5f, 1.5f));
                yield return StartCoroutine(WaitForActSeconds(2f));
            }
        }
    }
}