using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RockScript : MonoBehaviour
{
    float scale;

    void Start()
    {
        int nonlinear = Random.Range(0, 3);

        if(nonlinear < 2)
        {
             scale = Random.Range(0.1f, 0.2f);
        }
        else
        {
             scale = Random.Range(0.2f, 0.4f);
        }
        
        GetComponent<LifeScript>().maxHp = scale * 40;
        GetComponent<LifeScript>().hp = scale * 40;
        GetComponent<Rigidbody2D>().mass = scale * 10f;
        transform.localScale = new Vector3(scale, scale, 0f);
    }
}
