using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExplodeOnCollide : MonoBehaviour, IOnCollide, IOnDeath
{
    public GameObject xpl;
    public bool active = true;

    public void OnCollide(Collision2D collision)
    {
        if (collision.collider.CompareTag(GS.EnemyTag(tag)))
        {
            GetComponent<LifeScript>().OnDie();
        }
    }

    public void OnDeath()
    {
        if (active)
        {
            if (CompareTag("Allies"))
            {
                Instantiate(xpl, transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
            }
            else
            {
                Instantiate(xpl, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
            }
        }
    }
}
