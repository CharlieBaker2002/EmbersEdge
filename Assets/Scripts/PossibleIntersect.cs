using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PossibleIntersect : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D col)
    {
        if(col.TryGetComponent<Door>(out var d))
        {
            d.BecomeWall();
        }
    }
}