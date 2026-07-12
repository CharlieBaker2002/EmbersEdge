using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_1 : Unit
{
    private bool canRetreat = true;
    public GameObject proj;
    public Transform sp; //shoot point
    private float timer = 1.5f;
    private Quaternion qSave;
    private float attackRange;
    private bool losToTarget = true;
    private Collider2D tCol;          // target's collider, cached per target
    private Transform tColOf;
    int n;

    private Vector2 aimPos;           // where on the target to look/shoot (wall targets: nearest span point)
    private bool wallAim;

    // Distance to the target's SURFACE, not its centre — a big building's transform sits cells deep
    // inside its own hull (and a wall's transform can sit at its tower), so centre distance never
    // satisfies attackRange when touching it.
    float DistToTarget()
    {
        if (wallAim)
        {
            return Mathf.Max(0f, Vector2.Distance(aimPos, transform.position) - 0.5f);   // span cell centre -> face
        }
        float dist = Vector2.Distance(target.position, transform.position);
        if (tColOf != target) { tColOf = target; tCol = target.GetComponent<Collider2D>(); }
        if (tCol != null)
        {
            dist = Mathf.Max(0f, dist - Mathf.Min(tCol.bounds.extents.x, tCol.bounds.extents.y));
        }
        return dist;
    }
    
    private void Awake()
    {
        attackRange = Random.Range(1.25f, 2.5f);
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
        if (!transform.InDungeon())
        {
            transform.up = -((Vector2)transform.position).normalized;
        }
    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();
        // the ONE decision: target + route from the same field snapshot — no range, no memory,
        // no physics overlap. Reprioritising onto closer things happens by itself; the target is
        // null only when nothing ally-side is left at all (then just hold position).
        MinePathManager.Decide(this, out Vector2 pathDir);
        if(target != null)
        {
            aimPos = MinePath.AimPoint(target, transform.position);
            wallAim = aimPos != (Vector2)target.position;   // AimPoint returns position verbatim for non-walls
            bool centreLos = MinePath.LineOfSight(transform.position, aimPos); //shared by both width tests
            losToTarget = MinePath.LineOfSightWide(transform.position, aimPos, 0.1f, centreLos);
            Vector2 approach = (aimPos - (Vector2)transform.position).normalized;
            // walk straight only if the BODY fits the straight line (size), not just the bullet —
            // otherwise follow the field, which prices cracks as the walls that pinch them
            if (pathDir != Vector2.zero && !MinePath.LineOfSightWide(transform.position, aimPos, size, centreLos))
            {
                approach = pathDir;
            }
            qSave = transform.rotation;
            // Always face the current objective — the thing MinePath chose to go for (a building we're
            // detouring toward, or a wall we've switched to chewing), NOT the way we happen to be
            // stepping around obstacles. So while moving it stares down its ultimate target, and the
            // instant that target becomes a wall it looks there instead.
            transform.up = aimPos - (Vector2)transform.position;
            transform.rotation = Quaternion.Euler(0, 0, transform.rotation.eulerAngles.z);
            transform.rotation = Quaternion.Lerp(qSave, transform.rotation, 0.05f * actRate);
            float dist = DistToTarget();
            if (dist > attackRange || !losToTarget)
            {
                AS.TryAddForce(actRate * 0.1f * (7f - 0.5f * attackRange) * approach, true);
            }
            else
            {
                if (canRetreat)
                {
                    AS.TryAddForce(actRate * -0.1f * (4.5f - attackRange) * (aimPos - (Vector2)transform.position).normalized, true);
                }
                else
                {
                    AS.TryAddForce(actRate * -0.04f * (aimPos - (Vector2)transform.position).normalized, true);
                }
            }
        }
        timer -= Time.deltaTime * actRate;
        if(timer < 0f)
        {
            timer = 1f;
            if (target != null && DistToTarget() < attackRange && losToTarget)
            {
                canRetreat = false;
                anim.SetBool("Trigger", true);
                AS.maxVelocity = 2f;
                n = 1 + Mathf.FloorToInt(0.1f + 4f* RandomManager.Rand(2));
                timer = Random.Range(4f, 6f);
                timer += 2 * (n-1);
                AS.Decelerate(0.5f, 0.5f);
            }
        }
    }

    public void Shoot()
    {
        AS.Stop();
        canRetreat = true;
        anim.SetBool("Trigger", false);
        StartCoroutine(ShootI());
    }

    static readonly WaitForSeconds shotGap = new WaitForSeconds(0.25f);

    IEnumerator ShootI()
    {
        for(int i = 0; i < n; i++)
        {
            AS.TryAddForce(-transform.up * 35f, false);
            var p = Instantiate(proj, sp.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(sp.position + (Vector3) Random.insideUnitCircle * 0.1f - transform.position, tag);
            yield return shotGap;
        }
    }

    public void SharpUp()
    {
        AS.sharpness = 3f;
    }

    public void SharpDown()
    {
        AS.sharpness = 1f;
    }
}
