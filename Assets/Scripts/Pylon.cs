using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pylon : Building
{
    public float reachDistance;
    public List<Battery> batteries;
    public List<Pylon> connections;
    
    protected override void BEnable()
    { 
        EnergyManager.i.NewPylon(this);
    }
    
    protected override void BDisable()
    {
        EnergyManager.i.RemovePylon(this);
    }
}
