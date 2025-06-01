using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberStore : MonoBehaviour
{
    public int ember;
    public int maxEmber;
    public EmberStoreBuilding b;
    public bool isConstructor = false;

    private void Start()
    {
        if(!isConstructor) EnergyManager.i.emberStores.Add(this);
        EnergyManager.i.UpdateEmber();
    }

    public void OnDestroy()
    {
        if(!isConstructor) EnergyManager.i.emberStores.Remove(this);
        EnergyManager.i.UpdateEmber();
    }

    public bool Use(int cost, bool decreaseMax = false)
    {
        if (ember >= cost)
        {
            ember -= cost;
            if (decreaseMax)
            {
                maxEmber -= cost;
            }
            EnergyManager.i.UpdateEmber();
            if(b!=null) b.Refresh();
            return true;
        }
        return false;
    }

    public int Set(int newVal)
    {
        if(newVal > maxEmber)
        {
            ember = maxEmber;
            EnergyManager.i.UpdateEmber();
            if(b!=null) b.Refresh();
            return newVal - maxEmber;
        }

        if (ember != newVal)
        {
            ember = newVal;
            EnergyManager.i.UpdateEmber();
            if(b!=null) b.Refresh();
        }
        return 0;
    }
}
