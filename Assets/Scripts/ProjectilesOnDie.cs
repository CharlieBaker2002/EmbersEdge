using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProjectilesOnDie : MonoBehaviour, IOnDeath
{
    public GameObject p;
    public int n;
    public int randStrength = 0;
    public float randSpread = 0f;
    bool hasActivated = false;

    public void OnDeath()
    {
        if (hasActivated)
        {
            return;
        }
        hasActivated = true;
        for(int i = 0; i < n; i++)
        {
            var g = Instantiate(p, transform.position, Quaternion.identity, GS.FindParent(GS.ProjParent(transform)));
            g.GetComponent<ProjectileScript>().SetValues(GS.Rotated(transform.up,randSpread,true), tag, Random.Range(0, randStrength + 1), transform);
        }
    }
}
