using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberStore : MonoBehaviour
{
    public int ember;
    public int maxEmber;

    private void Start()
    {
        EnergyManager.i.emberStores.Add(this);
    }

    public void OnDestroy()
    {
        EnergyManager.i.emberStores.Remove(this);
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
            return true;
        }
        return false;
    }

    public int Set(int newVal)
    {
        Debug.Log("set:" +newVal);
        if(newVal > maxEmber)
        {
            ember = maxEmber;
            //EnergyManager.i.UpdateEmber();
            return newVal - maxEmber;
        }

        if (ember != newVal)
        {
            ember = newVal;
            //EnergyManager.i.UpdateEmber();
        }
        return 0;
    }
}
