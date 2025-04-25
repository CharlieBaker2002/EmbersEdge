using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroneRelic : Part
{
    private float ang = 0f;
    public float wait = 1f;
    public float intershootWait = 0f;
    public GameObject[] bullets;
    public Transform[] spawnPoints;
    public float range = 5f;
    public float omega = 60f;
    private bool shooting = false;
    RaycastHit2D hit = new RaycastHit2D();
    LayerMask lm;
    public bool healDrone = false;
    [SerializeField] int ammo = 10;
    [SerializeField] private Transform stick;


    private void Awake()
    {
        lm = LayerMask.GetMask("Enemy Units", "Enemy Buildings", "Enemy Projectiles");
    }

    private void Update()
    {
        stick.rotation = Quaternion.Euler(0f, 0f, ang);
        if (!shooting && omega != 0f)
        {
            ang += Time.deltaTime * omega;
            if (ang > 360f)
            {
                ang -= 360f;
            }
        }
    }

    public override void StartPart(MechaSuit m)
    {
        gameObject.SetActive(true);
        GetComponent<SpriteRenderer>().enabled = true;
        StartCoroutine(Loop());
        stick.gameObject.SetActive(true);
        base.StartPart(m);
    }

    public override void StopPart(MechaSuit m)
    {
        StopAllCoroutines();
        gameObject.SetActive(false);
        stick.gameObject.SetActive(false);
    }
    
    private IEnumerator Loop()
    {
        while (true)
        {
            hit = Physics2D.Raycast(transform.position, stick.TransformDirection(Vector2.up),range,lm.value);
            if (hit.collider != null)
            {
                shooting = true;
                omega *= -1;
                for(int i = 0; i < bullets.Length; i++)
                {
                    if(bullets[i] == null)
                    {
                        yield return new WaitForSeconds(intershootWait);
                        continue;
                    }
                    var PS = Instantiate(bullets[i], spawnPoints[i].position, stick.rotation, GS.FindParent(transform.CompareTag("Allies") ? GS.Parent.allyprojectiles : GS.Parent.enemyprojectiles)).GetComponent<ProjectileScript>();
                    PS.SetValues(stick.TransformDirection(Vector2.up), tag);
                    if(intershootWait > 0f)
                    {
                        yield return new WaitForSeconds(intershootWait);
                    }
                }
                yield return new WaitForSeconds(wait);
                shooting = false;
                ammo--;
                if (ammo <= 0f)
                {
                    MechaSuit.UsedFollower(this);
                }
            }
            yield return null;
        }
    }
}