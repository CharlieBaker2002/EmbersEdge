using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberStore : MonoBehaviour
{
    [FormerlySerializedAs("energy")] public float ember;
    [FormerlySerializedAs("maxEnergy")] public float maxEmber;
    
    public bool Use(float cost)
    {
        if (ember >= cost)
        {
            ember -= cost;
            EnergyManager.i.UpdateEmber();
            return true;
        }
        return false;
    }

    public float Set(float newVal)
    {
        if(newVal > maxEmber)
        {
            ember = maxEmber;
            return newVal - maxEmber;
        }
        ember = newVal;
        return 0f;
    }
}
