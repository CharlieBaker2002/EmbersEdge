using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberStore : MonoBehaviour
{
    public int ember;
    public int maxEmber;
    
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
        if(newVal > maxEmber)
        {
            ember = maxEmber;
            return newVal - maxEmber;
        }
        ember = newVal;
        return 0;
    }
}
