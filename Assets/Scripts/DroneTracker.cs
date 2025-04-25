using System;
using UnityEngine;

public class DroneTracker : MonoBehaviour, IOnDeath
{
    public LifeScript ls;
    
    private void Start()
    {
        this.QA(OnDeath,3f);
    }

    public void OnDeath()
    {
        if(EmitterTurret.targets.ContainsKey(transform))
        {
            EmitterTurret.targets.Remove(transform);
        }
        Destroy(this);
    }
}