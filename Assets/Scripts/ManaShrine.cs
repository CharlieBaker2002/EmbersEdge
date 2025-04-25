using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ManaShrine : Shrine
{
    public override void Trigger(Transform t)
    {
        if (active == true)
        {
            active = false;
            base.Trigger(t);
            ResourceManager.instance.ChangeFuels(ResourceManager.instance.maxFuel);
            ResourceManager.instance.AddCores(2);
            Instantiate(FX, t.position, t.rotation, t);
        }
    }
}
