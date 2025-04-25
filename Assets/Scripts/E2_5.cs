using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E2_5 : Unit, IOnCollide, IRoomUnit, IOnDeath
{
    //becoming a mosquito
    //moving randomly around player then towards player
    // moving away from player and then randomly

    public Collider2D col = null;
    private bool attached = false;
    private Vector2 pos = new Vector2(0,0);
    private static readonly int Attach = Animator.StringToHash("Attach");

    public IEnumerator E2_5I()
    {
        yield return WFAS(0.5f);
        GS.VP(0, transform, transform.position);
        while (anim.InState("Larva"))
        {
            AS.AddPush(0.25f, false, transform.up * (3 * actRate));
            if(Random.Range(0,2) == 0)
            {
                AS.RandDirectionOverT(0.5f, 0.5f * actRate, 3);
            }
            yield return WFAS(0.75f);
        }
        while (true)
        {
            while (attached == false)
            {
                Transform e = GS.FindNearestEnemy(tag, transform.position, 6f, false, false);
                Vector2 endPoint;
                endPoint = e == null ? transform.position + GS.RandCircle(2f, 4f) : e.position;
                Vector2[] dirs = GS.BezesDir(new Vector2[] { transform.position, transform.position.IP(endPoint, 1.2f, 0.4f), endPoint }, Random.Range(5, 9));
                foreach (Vector2 v in dirs)
                {
                    if(attached)
                    {
                        break;
                    }
                    AS.AddPush(0.25f, false, actRate * 12 * RandomManager.Rand(0, 0.1f) * v);
                    AS.FaceDirectionOverT(endPoint - (Vector2)transform.position, 0.2f, 5f * actRate);
                    yield return WFAS(0.2f);
                }
                yield return null;
            }
            yield return null;
        }
    }

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E2_5I());
    }

    public void OnCollide(Collision2D collision)
    {
        if (!collision.rigidbody.TryGetComponent<ActionScript>(out var oAS)) return;
        if(oAS.CompareTag(tag) || oAS.PS != null || !AS.canAct || oAS.wall)
        {
            return;
        }
        if(anim.InState("Larva"))
        {
            return;
        }
        ls.hp = ls.maxHp;
        GS.Stat(this,"dodging",2.5f);
        AS.Stop(1);
        oAS.AddCC("Slow", 2.5f, 0.5f);
        col.enabled = false;
        transform.SetParent(oAS.transform);
        anim.SetBool(Attach, true);
        oAS.ls.ChangeOverTime(-2.5f, 2.5f, 1,false);
        oAS.ls.onDeaths.Add(this);
        attached = true;
    }

    public IEnumerator Detach()
    {
        AS.ignoreWalls = true;
        transform.SetParent(GS.FindParent(GS.Parent.enemies));
        AS.AddPush(1f, false, (pos - (Vector2)transform.position).normalized * 6f * actRate);
        yield return new WaitForSeconds(0.5f);
        col.enabled = true;
        yield return new WaitForSeconds(1f);
        AS.ignoreWalls = false;
        Instantiate((GameObject)Resources.Load("Zit"), transform.position, GS.RandRot(), GS.FindParent(GS.Parent.enemies)).GetComponent<LifeScript>().orbs = new float[] {0,0,0,0};
        attached = false;
    }

    public void RecieveRoom(Collider2D bounds, Vector2 p)
    {
        pos = p;
    }

    public void OnDeath()
    {
        if (attached)
        {
            ls.OnDie();
        }
    }
}
