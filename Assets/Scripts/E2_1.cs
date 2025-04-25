using System.Collections;
using UnityEngine;

public class E2_1 : Unit, IOnCollide
{
    private Transform target;
    private static readonly int Enrage = Animator.StringToHash("Enrage");
    private static readonly int Boom = Animator.StringToHash("Boom");

    public void Hover()
    {
        StartCoroutine(IHover());
        target = GS.FindNearestEnemy(tag, transform.position, 7f, true, true);
        if (target != null)
        {
            if((target.position - transform.position).sqrMagnitude < 6)
            {
                anim.SetBool(Enrage, true);
            }
            AS.Decelerate(1f, 0.8f);
            AS.FaceEnemyOverT(0.3f, 15f * actRate, target, false);
        }
    }

    private IEnumerator IHover()
    {
        AS.RandDirectionOverT(0.15f, 0.6f * actRate, 10f);
        yield return new WaitForSeconds(0.2f);
        AS.AddPush(0.25f, false, transform.up * (6f * actRate));
    }

    public void EnrageHover()
    {
        if (target != null)
        {
            AS.FaceEnemyOverT(0.1f, 10f * actRate, target,false);
            AS.AddPush(0.25f, false, (target.position - transform.position).normalized * 9f * actRate);
            if((Vector2.Distance(transform.position,target.position) < 1.5f))
            {
                AS.AddPush(1, false, (target.position - transform.position).normalized * 3 * actRate);
                anim.SetBool(Boom, true);
            }
        }
        else
        {
            Hover();
        }
    }

    public void OnCollide(Collision2D collision)
    {
        if (collision.transform.CompareTag(GS.EnemyTag(tag)))
        {
            if (anim.GetBool(Enrage))
            {
                if (collision.transform.GetComponent<ProjectileScript>() == null)
                {
                    anim.SetBool(Boom, true);
                }
            }

            else
            {
                anim.SetBool(Enrage, true);
            }
        }
    }

    public void TurnOffResources()
    {
        ls.orbs = new float[] { 0, 0, 0, 0 };
    }

    public void DestroyMe()
    {
        Destroy(gameObject);
    }
}
