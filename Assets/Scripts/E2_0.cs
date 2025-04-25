using System.Collections;
using UnityEngine;
public class E2_0 : Unit, IOnCollide
{
    private float timer = 2f;
    private Transform target;
    private static readonly int Pounce1 = Animator.StringToHash("Pounce");
    private static readonly int Swipe1 = Animator.StringToHash("Swipe");
    private Collider2D[] cols = new Collider2D[5];

    protected override void Update()
    {
        base.Update();
        timer -= Time.deltaTime * actRate;
        if (!(timer <= 0f)) return;
        PounceInit();
        timer = Random.Range(6f, 8f);
    }

    public void Jump()
    {
        if (target != null)
        {
            AS.FaceDirectionOverT(target.position - transform.position, 0.15f * actRate);
            StartCoroutine(JumpDelayed());
        }
        else
        {
            AS.RandDirectionOverT(0.15f, 0.6f * actRate);
            StartCoroutine(JumpDelayed());
        }
    }

    private IEnumerator JumpDelayed()
    {
        yield return new WaitForSeconds(0.16f);
        AS.TryAddForce(transform.up * (160 * actRate), false);
    }

    public void PounceInit()
    {
        target = GS.FindEnemy(transform, 6f, GS.searchType.allSearch, cols,target);
        if (target != null)
        {
            AS.FaceEnemyOverT(0.75f, 10f * actRate, target, false);
            anim.SetBool(Pounce1, true);
        }
    }

    public void Pounce()
    {
        StartCoroutine(IPounce());
    }

    private IEnumerator IPounce()
    {
        float t = 1.5f;
        Vector2 pos;
        Vector2 dir;
        if (target != null)
        {
            pos = target.position + (target.position - transform.position).normalized * 0.5f;
            dir = (pos - (Vector2)transform.position).normalized; 
        }
        else
        {
            pos = transform.up * 10f + transform.position;
            dir = transform.up;
        }
        while (((Vector2)transform.position - pos).sqrMagnitude > 0.1f)
        {
            t -= Time.fixedDeltaTime;
            if (t < 0f)
            {
                break;
            }
            if (AS.canAct)
            {
                AS.TryAddForce(20f * actRate * t * t * dir, true);
            }
            yield return new WaitForFixedUpdate();
        }
        anim.SetBool(Pounce1, false);
        AS.Stop();
        AS.Decelerate(1.5f, 0.5f);
        anim.SetBool(Swipe1, true);
    }

    public void Swipe()
    {
        anim.SetBool(Swipe1, false);
        foreach (Transform t in GS.FindEnemies(tag, transform.position + 0.4f * transform.up, 0.6f, true, true,cols))
        {
            if (t.TryGetComponent<LifeScript>(out var lifeScript))
            {
                lifeScript.Change(-2.5f, 0);
            }
        }
    }

    public void OnCollide(Collision2D collision)
    {
        if (!AS.CheckWall(collision.collider.transform))
        {
            if (collision.transform.GetComponent<ProjectileScript>() == null)
            {
                if (anim.GetBool(Pounce1))
                {
                    StopAllCoroutines();
                    anim.SetBool(Pounce1, false);
                    AS.Decelerate(1.5f, 0.8f);
                    anim.SetBool(Swipe1, true);
                }
                else if (anim.GetBool(Swipe1) == false)
                {
                    if (collision.transform.CompareTag(GS.EnemyTag(tag)))
                    {
                        anim.SetBool(Swipe1, true);
                        transform.up = Vector2.Lerp(transform.up, (collision.transform.position - transform.position).normalized, 0.75f);
                    }
                }
            }
        }
        else if (AS.rb.velocity != Vector2.zero)
        {
            transform.up = AS.rb.velocity;
        }
    }
}