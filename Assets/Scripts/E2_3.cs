using System.Collections;
using UnityEngine;

public class E2_3 : Unit //Either shoots down-grav proj or up-grav projectiles based on relative positioning between character and base.
{
    public ProjectileScript ps;
    public GameObject lil;
    public Transform[] ts;

    Vector2 d;
    float tim = 0f;
    Transform t;
    bool waiting = false;
    float dist = 2.5f;
    public LAMulti la;
    private static readonly int Up = Animator.StringToHash("Up");
    private static readonly int Down = Animator.StringToHash("Down");

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E2_3Cycle());
        transform.localRotation = Quaternion.identity;
    }

    IEnumerator E2_3Cycle()
    {
        while (true)
        {
            dist = Random.Range(2f, 4f);
            t = GS.FindNearestEnemy(tag, transform.position, 7.5f, false);
            if (t != null)
            {
                tim = Time.time;
                while (t != null)
                {
                    d = t.position + (transform.position.y > t.position.y ? new Vector3(0f, dist) : new Vector3(0f, -dist)) - transform.position;
                    AS.TryAddForce(2f * actRate * GS.VectInRange(d, 0f, dist), true);
                    yield return new WaitForFixedUpdate();
                    if (Time.time - tim > dist * InvActRate())
                    {
                        break;
                    }
                }
                if (t == null)
                {
                    t = GS.FindNearestEnemy(tag, transform.position, 7.5f, false);
                    if (t == null)
                    {
                        yield return StartCoroutine(WaitForActSeconds(0.5f));
                        continue;
                    }
                }
                anim.SetBool(transform.position.y > t.position.y ? Up : Down, true);
                if(Random.Range(0,3) == 0)
                {
                    GS.VP(0, transform, ts[0].position);
                    GS.VP(0, transform, ts[1].position);
                }
                else if(Random.Range(0,3) == 0)
                {
                    GS.VP(1, transform, ts[0].position);
                    GS.VP(1, transform, ts[1].position);
                }
                waiting = true;
                while (waiting)
                {
                    if (t != null)
                    {
                        AS.TryAddForceToward(t.position + ((transform.position.y > t.position.y) ? new Vector3(0f, dist) : new Vector3(0f, -dist)), 12f * actRate, 3f, 1f);
                        yield return new WaitForFixedUpdate();
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else
            {
                AS.AddPush(1f, false, GS.RandCircleV2(0.5f, 1.5f) * actRate);
                yield return WFAS(2f);
            }
        }
        
    }
    public void BoolsOff()
    {
        anim.SetBool(Up, false);
        anim.SetBool(Down, false);
        waiting = false;
    }

    public void ShootUp()
    {
        StartCoroutine(Shoot(true));
    }

    public void ShootDown()
    {
        StartCoroutine(Shoot(false));
    }

    private IEnumerator Shoot(bool up)
    {
        for(int i = 0; i < 10; i++)
        {
            Instantiate(ps, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles)).SetValues(up ? GS.QTV(Quaternion.Euler(0f, 0f, Random.Range(-45f, 45f))) : GS.QTV(Quaternion.Euler(0f, 0f, Random.Range(135f, 210f))),tag,ActRateProjectileStrength() + Random.Range(0,5),transform);
            yield return WFAS(Random.Range(0.05f, 0.15f));
        }
    }

    public void Die()
    {
        if (RefreshManager.i.ARENAMODE)
        {
            ArenaManager.i.enemies.Add(Instantiate(lil, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles)));
                ArenaManager.i.enemies[^1].GetComponent<LA>().adjustCoef = la.adjustCoef;
        }
        else
        {
            Instantiate(lil, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles)).GetComponent<LA>().adjustCoef = la.adjustCoef;
        }
        Destroy(gameObject);
    }
    
}
