using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageOnCollide : MonoBehaviour, IOnCollide
{
    public bool active = true;
    public float damage = 2;
    public int dmgType = 0;
    public bool enemyOnly = true;

    public void OnCollide(Collision2D collision)
    {
        if (active)
        {
            if (collision.rigidbody.TryGetComponent<LifeScript>(out var ls))
            {
                if (collision.rigidbody.CompareTag(GS.EnemyTag(tag)) && enemyOnly || !enemyOnly)
                {
                    ls.Change(-damage, dmgType);
                }
            }
        }
    }
}
