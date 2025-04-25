using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class ClawBot : AllyAI, IOnCollide
{
    private static readonly int Dash1 = Animator.StringToHash("Dash");
    private static readonly int Shoot1 = Animator.StringToHash("Shoot");
    private static readonly int Attack = Animator.StringToHash("Attack");
    private static readonly int Attack2 = Animator.StringToHash("Attack2");
    private static readonly int Attack3 = Animator.StringToHash("Attack3");
    [SerializeField] float range = 9f;
    [SerializeField] Finder find;
    public ClawBotClaw claw;
    [SerializeField] Transform[] points;
    [SerializeField] ClawBotClaw[] claws;
    [SerializeField] GameObject proj1;
    [SerializeField] GameObject proj2;
    Transform e;
    [SerializeField] float moveForce = 1.5f;

    public bool canShoot = false;
    public bool canDash = false;
    public bool big = false;
    private float clawTime = 0f;
    private float attackTime = 0f;
    private Vector2 colSpot;
    private float healTimer;
    private float shootTimer;
    private float dashTimer;
    public SpriteRenderer[] extraParts;
    [SerializeField] UnityEngine.Rendering.Universal.Light2D li;
    [SerializeField] Sprite[] lvl1s;
    [SerializeField] Sprite[] lvl2s;
    Coroutine lu = null;

    protected override void Start()
    {
        base.Start();
        MakeClaw(0);
        MakeClaw(1);
        if (big) { range = 14f; AS.mass = 2f; moveForce = 2.25f; find.radius = 7f;  ls.maxHp = 12; ls.Change(12,-1); }
        StartCoroutine(ClawBotI());
    }

    IEnumerator ClawBotI()
    {
        while (true)
        {
            Transform temp = find.FindFresh();
            if (temp != null) { e = temp;}
            if (transform.Distance(targetPoint) > range || e == null)
            {
                yield return StartCoroutine(MoveToSpot());
            }
            else if (e != null)
            {
                float d = transform.Distance(e);
                if (canDash && Random.Range(0, 10) == 0 && d < 3.5f && d > 1f && Time.time > dashTimer + 3.5f)
                {
                    attackTime = Time.time - 1.75f;
                    anim.SetBool(Dash1, true);
                    dashTimer = Time.time;
                    this.TurnTowards(e, 1.5f, 2f);
                    LU();
                    yield return StartCoroutine(Dash(e.transform.position));
                }
                else if (canShoot && Random.Range(0,30) == 0 && d > 2.5f && d < 5.5f && Time.time > shootTimer + 14f && claws[0] != null && claws[1] != null)
                {
                    if(Time.time > shootTimer + 14f)
                    {
                        attackTime = Time.time;
                        this.TurnTowards(e, 1.5f, 10f);
                        clawTime = Time.time;
                        LU();
                        anim.SetBool(Shoot1, true);
                        yield return StartCoroutine(Shoot(Random.Range(0, 2)));
                    }
                }
                else
                {
                    if (claws[0] != null || claws[1] != null)
                    {
                        transform.Turn(e, 5f);
                        AS.TryAddForce(moveForce * transform.V(e), true);
                    }
                    else
                    {
                        transform.Turn(e, 5f,true);
                        AS.TryAddForce(-moveForce * transform.V(e), true);
                    }
                }
            }
            if (Time.time > clawTime + 10f)
            {
                MakeClaw(Random.Range(0, 2));
            }
            yield return new WaitForFixedUpdate();
        }
    }

    private void LU()
    {
        if (lu != null) StopCoroutine(lu); 
        lu = StartCoroutine(LightUp());
    }

    private void SetSprite(float x)
    {
        if (big)
        {
            sr.sprite = lvl2s[Mathf.RoundToInt(3 * (x * 2f))];
        }
        else
        {
            sr.sprite = lvl1s[Mathf.RoundToInt(3 * (x * 2f))];
        }
    }


    private IEnumerator LightUp()
    {
        for(float i = 0.5f; i > 0f; i -= Time.fixedDeltaTime) //sprites should be in reverse order in array
        {
            li.pointLightOuterRadius = Mathf.Lerp(li.pointLightOuterRadius, 2f, Time.fixedDeltaTime * 2 * i);
            li.pointLightInnerRadius += Time.fixedDeltaTime;
            SetSprite(i);
            yield return new WaitForFixedUpdate();
        }
        yield return new WaitForSeconds(0.1f);
        for (float i = 0; i < 1f; i += Time.fixedDeltaTime)
        {
            li.pointLightOuterRadius = Mathf.Lerp(li.pointLightOuterRadius, 0.5f, Time.fixedDeltaTime * i);
            li.pointLightInnerRadius = Mathf.Max(0f,li.pointLightInnerRadius - 0.5f * Time.fixedDeltaTime);
            SetSprite(i * 0.5f);
            yield return new WaitForFixedUpdate();
        }
        li.pointLightOuterRadius = 0.5f;
        li.pointLightInnerRadius = 0f;
    }

    protected override void Update()
    {
        base.Update();
        healTimer -= Time.deltaTime;
        if (healTimer <= 0f)
        {
            healTimer += big ? 1.5f : 3f;
            if (claws[0] != null)
            {
                claws[0].GetComponent<LifeScript>().Change(1,-1);
            }
            if (claws[1] != null)
            {
                claws[1].GetComponent<LifeScript>().Change(1, -1);
            }
        }
    }

    void MakeClaw(int index)
    {
        if (claws[index] == null)
        {
            claws[index] = Instantiate(claw, points[index]);
            claws[index].transform.localPosition = Vector3.zero;
            claws[index].transform.localRotation = Quaternion.Euler(Vector3.zero);
            claws[index].GetComponent<ActionScript>().onCollides.Add(this);
            clawTime = Time.time;
            if (index == 0)
            {
                claws[index].GetComponent<SpriteRenderer>().flipX = true;
            }
            if (big)
            {
                claws[index].ls.maxHp = 14;
            }
            claws[index].ls.hp = 0.000001f;
            claws[index].ls.Change(claws[index].ls.maxHp / 2f,-1);
        }
    }

    IEnumerator Dash(Vector2 pos)
    {
        AS.Decelerate(0.45f, 0.6f);
        yield return new WaitForSeconds(0.5f);
        AS.AddPush(1f, false, (pos - (Vector2)transform.position) * (3f * moveForce));
        yield return new WaitForSeconds(0.75f);
        AS.Decelerate(0.5f, 0.5f);
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Shoot(int index)
    {
        AS.Decelerate(0.65f, 0.6f);
        yield return new WaitForSeconds(0.75f);
        if (claws[index] != null)
        {
            if(e!= null)
            {
                shootTimer = Time.time;
                Destroy(claws[index].gameObject);
                claws[index] = null;
                Vector2 v = transform.V(e);
                if (Vector2.SignedAngle(v, transform.up) < 0f)
                {
                    GS.NewP(proj1, points[index], tag, transform.up);
                }
                else
                {
                    GS.NewP(proj2, points[index], tag, transform.up);
                }
            }
        }
        yield return new WaitForSeconds(1f);
        MakeClaw(index);

    }

    IEnumerator MoveToSpot()
    {
        Vector2 point = new Vector2(targetPoint.x, targetPoint.y);
        float t2 = 2f;
        AS.FaceDirectionOverT(point - (Vector2)transform.position, 0.75f, 10f);
        while ((transform.position - (Vector3)point).sqrMagnitude > 1f)
        {
            t2 -= Time.deltaTime;
            if (t2 <= 0f)
            {
                break;
            }
            AS.TryAddForce(moveForce * 0.1f * GS.VectInRange(point - (Vector2)transform.position, 3f, 7f), true);
            yield return null;
        }
    }

    public void OnCollide(Collision2D collision)
    {
        if (Time.time < attackTime + 2.5f || !collision.gameObject.CompareTag(GS.EnemyTag(tag)) || collision.gameObject.layer == 11)
        {
            return;
        }
        if (claws[0] == null && claws[1] == null) { return; }
        LU();
        colSpot = collision.transform.position;
        if (canDash)
        {
            if(Random.Range(0,3) == 0)
            {
                anim.SetBool(Attack, true);
            }
            else if (Random.Range(0,2) == 0)
            {
                anim.SetBool(Attack2, true);
            }
            else
            {
                anim.SetBool(Attack3, true);
            }
        }
        else
        {
            if (Random.Range(0, 2) == 0)
            {
                anim.SetBool(Attack, true);
            }
            else
            {
                anim.SetBool(Attack2, true);
            }
        }
        attackTime = Time.time;
    }

    public void TurnTowardsAttacker()
    {
        transform.up = transform.V(colSpot);
        var ts = GS.FindEnemies(tag, transform.position + transform.up, big ? 2f:1.5f, true, true);
        foreach(Transform t in ts)
        {
            LifeScript l = t.GetComponentInParent<LifeScript>();
            if (l != null)
            {
                l.Change(big ? -2 : -1, 2);
            }
        }
    }
}
