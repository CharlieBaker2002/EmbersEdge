using System.Collections;
using UnityEngine;

public class E2_10 : Unit
{

    [SerializeField] ActionScript spawn;
    [SerializeField] Finder f;

    float t = 10;
    private static readonly int Charge1 = Animator.StringToHash("Charge");
    private static readonly int Spawn1 = Animator.StringToHash("Spawn");

           
    protected override void Start()
    {
        base.Start();
        t = Random.Range(9f, 22f);
        f.OnFound += _ => { anim.SetBool(Charge1, true); };
        f.OnLost += delegate { f.FindFresh(); };
    }

    protected override void Update()
    {
        base.Update();
        if (ls.orbs[1] < 1) return;
        t -= Time.deltaTime;
        if (t <= 0f)
        {
            t += Random.Range(18f, 34f);
            anim.SetBool(Spawn1, true);
        }
    }

    public void Chomp()
    {
        AS.TryAddForceToward(f.T != null ? f.T.position : 2 * GS.IP(transform.position,transform.position + transform.up,1,0.2f,true), actRate * 50f, 2f);
        AS.FaceEnemyOverT(0.2f,actRate * 8f,f.T, true);
    }

    public void VP()
    {
        GS.VP(1, transform, transform.position, 100);
        if (f.T == null) return;
        this.TurnTowards(f.T, 1f, 2f);
    }

    public void Charge()
    {
        AS.AddPush(0.5f,false,transform.up * 10f);
        AS.immaterial = true;
        this.QA(delegate { AS.TryAddForce(GS.PlusMinus() * 0.5f * actRate * transform.right, true); }, 0.65f);
        this.QA(delegate { AS.TryAddForce(GS.PlusMinus() * actRate * transform.right, true); }, 1.1f);
        this.QA( delegate { AS.immaterial = false; }, 1.85f);

    }

    public IEnumerator Spawn()
    {
        float tim = Time.time;
        while (ls.orbs[1] > 1)
        {
            Instantiate(spawn, transform.position, GS.RandRot(), transform.parent).AddPush(1f,false,GS.RandCircleV2(0.5f,1f));
            ls.orbs[1] -= 1;
            yield return WFAS(Random.Range(0.35f, 1f));
            if (Time.time - tim > 3.25f * actRate)
            {
                yield break;
            }
        }
    }
}
