using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoulCollectOnDeath : MonoBehaviour, IOnDeath
{
    public void OnDeath()
    {
        foreach (SoulGenerator s in SoulGenerator.gs)
        {
            if(s.busy) continue;
            if (Vector2.SqrMagnitude(s.transform.position - transform.position) < s.range * s.range)
            {
                s.Collect(transform);
            }
        }
    }
}
