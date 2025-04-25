using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shrine : MonoBehaviour
{
    [SerializeField]
    protected GameObject FX;
    protected bool active = true;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if(collision.transform.name == "Character")
        {
            GetComponent<Animator>().SetBool("Trigger", true);
            Trigger(collision.transform);
        }
    }

    public virtual void Trigger(Transform t)
    {
        if (FX)
        {
            Instantiate(FX, t.position, t.rotation, t);
        }
    }
}
