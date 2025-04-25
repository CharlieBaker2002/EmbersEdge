using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E2_4B : MonoBehaviour
{
    public GameObject E2_5;

    public void Morph()
    {
        Destroy(gameObject);
        Instantiate(E2_5, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemies));
    }
}
