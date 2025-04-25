using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlowOnCollide : MonoBehaviour, IOnCollide
{
    public float value;
    public float t;

    public void OnCollide(Collision2D collision)
    {
        if (!collision.transform.CompareTag(tag))
        {
            if (collision.transform.GetComponentInParent<ActionScript>() != null)
            {
                collision.transform.GetComponentInParent<ActionScript>().AddCC("slow", t, value);
            }
        }
    }
}
