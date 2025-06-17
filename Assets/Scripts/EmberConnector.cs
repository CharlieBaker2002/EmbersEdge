using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberConnector : MonoBehaviour
{
    public enum typ
    {
        Store,
        Constructor,
        Generator
    }

    [FormerlySerializedAs("Taip")] public typ taip;

    public List<EmberCable> cables;
    public List<EmberConnector> connections;
    public List<bool> cableConnectionDirections;
  
    public int ember;
    public int maxEmber;
    public int desiredEmber = 0;
    public int emberTravel = 0;

    public Action onRefresh;

    private float timer;
    
    public List<List<EmberConnector>> jobs = new List<List<EmberConnector>>();

    public void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            if (jobs.Count > 0)
            {
                int ind = connections.IndexOf(jobs[0][0]);
                cables[ind].StartCoroutine(cables[ind].Animate(cableConnectionDirections[ind], jobs[0]));
                jobs.RemoveAt(0);
                timer = 4 * Time.fixedDeltaTime;
            }
        }
    }
    
    public void Chain(List<EmberConnector> chain)
    {
        chain.RemoveAt(0);        
        jobs.Add(chain);
    }
}


