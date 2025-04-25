using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Pyre : MonoBehaviour
{
    public ProjectileScript PS;
    public Light2D l;
    bool coro = false;
    public CircleCollider2D c;

    private void Awake()
    {
        if (Random.Range(0, 5) <= 2)
        {
            Off();
        }
        else
        {
            c.radius = Random.Range(2f, 3.5f);
        }
       
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (coro)
        {
            return;
        }
        if(collision.name == "Character")
        {
            coro = true;
            StartCoroutine(WaitCheckShoot());
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if(PS != null)
        {
            if (collision.transform.CompareTag("Allies"))
            {
                if (collision.rigidbody.TryGetComponent<LifeScript>(out var ls))
                {
                    if (ls.race == 3)
                    {
                        Off();
                    }
                }
            }
        }
    }

    private void Off(bool inEnumerator = false)
    {
        l.intensity = 0.2f;
        if (!inEnumerator)
        {
            StopAllCoroutines();
            Destroy(PS.gameObject);
        }
        coro = true;
        c.radius = 0;
        PS = null;
    }

    IEnumerator WaitCheckShoot()
    {
        yield return new WaitForSeconds(Random.Range(1f,6f));
        if((CharacterScript.CS.transform.position - transform.position).sqrMagnitude > 25f)
        {
            coro = false;
            yield break;
        }
        else
        {
            if(PS!= null)
            {
                PS.transform.parent = GS.FindParent(GS.Parent.enemyprojectiles);
                PS.GetComponent<DestroyAfterTime>().Destroy();
                PS.SetValues(CharacterScript.CS.transform.position - transform.position, "Enemies", Random.Range(0, 4), transform);
            }
            Off(true);
        }
    }

}
