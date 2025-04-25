using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Turret : Unit
{
    private static readonly int Shoot1 = Animator.StringToHash("Shoot");
    private float ang = 0f;
    public float wait = 1f;
    public float refresh = 1f;
    private float refreshTimer = 0f;
    public GameObject bullet;
    public Transform spawnPoint;
    public float range = 5f;
    public float omega = 60f;
    private bool shooting = false;
    RaycastHit2D hit = new RaycastHit2D();
    LayerMask lm;
    public bool healDrone = false;
    public bool blue;
    public int ammo = 5;
    private static List<Turret> ts;

    protected override void Start()
    {
        base.Start();
        if (CompareTag("Allies"))
        {
            if (healDrone)
            {
                lm = LayerMask.GetMask("Character");
            }
            else
            {
                lm = LayerMask.GetMask("Enemy Units", "Enemy Buildings");
            }
        }
        else if (CompareTag("Enemies"))
        {
            if (healDrone)
            {
                lm = LayerMask.GetMask("Enemy Units");
            }
            else
            {
                lm = LayerMask.GetMask("Ally Units", "Ally Buildings","Character");
            }
        }
        StartCoroutine(Loop());
    }

    protected override void Update()
    {
        base.Update();
        refreshTimer -= Time.deltaTime;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
        if (!shooting && omega != 0f)
        {
            ang += Time.deltaTime * omega;
            if (ang > 360f)
            {
                ang -= 360f;
            }
        }
    }

    public void Shoot() //from animation
    {
        if(ammo > 0)
        {
            var PS = Instantiate(bullet, spawnPoint.position, transform.rotation, GS.FindParent(GS.ProjParent(transform))).GetComponent<ProjectileScript>();
            if (blue && ammo % 2 == 0)
            {
                PS.damage *= -1;
            }
            PS.SetValues(transform.TransformDirection(Vector3.up), tag, 0f, transform);
            ammo--;
        }
        else
        {
            anim.SetBool(Shoot1, false);
        }
    }

    private IEnumerator Loop()
    {
        if (blue)
        {
            while (true)
            {
                anim.SetBool(Shoot1, true);
                yield return null;
                anim.SetBool(Shoot1, false);
                yield return StartCoroutine( WaitForActSeconds(Random.Range(refreshTimer,refreshTimer *2)));
            }
        }
        while (true)
        {
            if(refreshTimer <= 0f)
            {
                if (ammo <= 0f)
                {
                    Destroy(gameObject);
                    yield break;
                }
                hit = Physics2D.Raycast(transform.position, transform.TransformDirection(Vector2.up), range, lm.value);
                if (hit.collider != null)
                {
                    refreshTimer = refresh;
                    shooting = true;
                    omega *= -1;
                    anim.SetBool(Shoot1, true);
                    yield return null;
                    anim.SetBool(Shoot1, false);
                    yield return StartCoroutine( WaitForActSeconds(wait));
                    shooting = false;
                }
            }
            yield return null;
        }
    }
}