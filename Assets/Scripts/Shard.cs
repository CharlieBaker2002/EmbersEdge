using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shard : MonoBehaviour
{
    public float flux = 1f;
    public float mana = 5f;
    private void OnTriggerEnter2D(Collider2D col)
    {
        if (col.name == "Character")
        {
            if(flux > 0)
            {
                ResourceManager.instance.AddCores(1);
            }
            ResourceManager.instance.ChangeFuels(mana);
            Destroy(gameObject);
        }
    }
}
