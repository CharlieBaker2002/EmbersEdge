using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Jelly2 : MonoBehaviour, IOnCollide
{
    public GameObject projectile;
    public Transform shootPoint;
    public Transform target;
    public GameObject vul;

    private Animator anim;
    private ActionScript AS;
    Vector3 nextPoint = new Vector3(0f,0f,0f);
    private float timer = 1f;
    public float reset = 1f;
    public float range = 3f;

    Transform enemy;

    bool shooting = false;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
        nextPoint = transform.position;
    }
    public void Shoot()
    {
        var p = Instantiate(projectile, shootPoint.transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
        p.GetComponent<ProjectileScript>().SetValues(transform.up, tag);
        Destroy(gameObject);
    }

    public void OnCollide(Collision2D col)
    {
        if (col.transform.CompareTag("Allies"))
        {
            AS.Stop();
            anim.SetBool("Shoot", true);
            shooting = true;
            if (!enemy)
            {
                enemy = target;
            }
            if (vul != null)
            {
                vul.SetActive(true);
            }
        }
        
    }

    private void Update()
    {
        if (shooting)
        {
            nextPoint = Vector3.Lerp(nextPoint,enemy.position,0.1f); 
        }
        else
        {
            AS.TryAddForce((target.position - transform.position).normalized, true);
            target.position = Vector3.Lerp(target.position, nextPoint, 0.1f);
            if (enemy)
            {
                if ((enemy.transform.position - transform.position).sqrMagnitude > 2 * range * range)
                {
                    enemy = null;
                }
                else
                {
                    nextPoint = enemy.position;
                }
            }
            else
            {
                if (timer > 0f)
                {
                    timer -= Time.deltaTime;
                }
                else
                {
                    timer = reset;
                    enemy = GS.FindNearestEnemy(tag, transform.position, range, false);
                    if(enemy == null)
                    {
                        nextPoint = transform.position + (Vector3)Random.insideUnitCircle.normalized * range;
                    }
                }
            }
        }
        target.transform.position = Vector2.Lerp(target.transform.position, nextPoint, 0.3f);
        float angle = Mathf.Atan2(target.position.x - transform.position.x, target.position.y - transform.position.y) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.back);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 0.05f);
    }
}
