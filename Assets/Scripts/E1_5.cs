using System.Collections;
using UnityEngine;

public class E1_5 : Unit
{
    public Collider2D bite;
    public ParticleSystem ps;
    private Vector2 dir = new Vector2(0, 0);
    public GameObject vp;
    public Transform vpPos;

    private void Awake()
    {
        ps = GetComponentInChildren<ParticleSystem>();
        AS = GetComponent<ActionScript>();
        ls = GetComponent<LifeScript>();
    }

    protected override void Update()
    {
        base.Update();
        if (dir != Vector2.zero)
        {
            transform.up = Vector2.Lerp(transform.up, AS.rb.linearVelocity, actRate * 4f * Time.deltaTime);
            AS.TryAddForce(dir * (1.5f * actRate), true);
        }
    }

    public void TurnOnBite()
    {
        bite.enabled = true;
        AS.dragCoef = 0.98f;
        ps.Play();
    }

    public void TurnOffBite()
    {
        bite.enabled = false;
        ps.Stop();
    }

    public void DigOn()
    {
        AS.interactive = false;
        GS.Stat(this,"dodging",3f);
        var t = GS.FindNearestEnemy(tag, transform.position, 6f, false, true);
        if (t != null)
        {
            dir = t.position - transform.position;
        }
        else
        {
            dir = Random.insideUnitCircle.normalized;
        }
    }

    public void DigOff()
    {
        if (Random.Range(0, 2) == 0)
        {
            Instantiate(vp, vpPos.transform.position, Quaternion.identity, transform);
        }
        AS.interactive = true;
        dir = Vector2.zero;
        AS.Decelerate(0.5f, 0.75f);
        var t = GS.FindNearestEnemy(tag,  actRate* transform.position, 6f, false, true);
        if (t == null) return;
        AS.dragCoef = 1.01f;
        StartCoroutine(TowardsEnemy(t));
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag(GS.EnemyTag(tag))) return;
        if (!collision.TryGetComponent<ActionScript>(out var ASP)) return;
        if (ASP.immaterial || ASP.PS != null || ASP.ls == null)
        {
            return;
        }
        ASP.ls.Change(-2, 1);
        ASP.ls.ChangeOverTime(-3, 3, 1);
    }

    private IEnumerator TowardsEnemy(Transform t)
    {
        AS.FaceEnemyOverT(1.5f, 6.5f, t, true);
        yield return WFAS(0.25f);
        AS.Stop();
        AS.AddPush(0.9f, false, actRate * GS.VectInRange(t.position - transform.position, 3f, 8));
        yield return WFAS(0.25f);
        float timer = 1.5f;
        while(t!=null & timer > 0f)
        {
            timer -= Time.fixedDeltaTime;
            AS.TryAddForceToward(t.position, 18f * actRate, 3f, 1f);
            yield return new WaitForFixedUpdate();
        }
    }
    
    public override void UpdateActRate()
    {
        base.UpdateActRate();
        if (ls.hasDied) return;
        if (!(actRate < 1f)) return;
        AS.dragCoef = 0.98f;
        if (actRate == 0f)
        {
            anim.Play("E1_5",0,0f);
        }
    } 
}
