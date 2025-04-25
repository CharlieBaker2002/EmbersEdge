using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E2OR3 : MonoBehaviour
{
    public MarauderSO E2;
    public MarauderSO E3;
    public Room r;
    public bool upDefault = true;

    private void Start()
    {
        if (Mathf.Abs(transform.rotation.eulerAngles.z) % 180 == 0)
        {
            r.enemySOs.Add(upDefault? E3 : E2);
        }
        else
        {
            r.enemySOs.Add(upDefault ? E2 : E3);
        }
    }
}
