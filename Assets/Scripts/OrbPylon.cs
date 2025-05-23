using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System;

public class OrbPylon : Building
{
    public float radius = 5;
    public OrbMagnet mag;
    public List<OrbMagnet> magnetsOfficial = new List<OrbMagnet>();
    private List<OrbMagnet> magnets = new List<OrbMagnet>();
    public bool cloneNecessary = false;
    public int orbType;
    public float refreshRate = 0.15f;

    float t = 1f;

    public override void Start()
    {
        if (RefreshManager.i.CASUALNOTREALTIME)
        {
            refreshRate = 0.02f;
        }
        base.Start();
        buildingBehaviours.Add(mag);
        ResourceManager.instance.ChangePylons(this, true);
        StartCoroutine(Pylon());
        if (BM.i.UI.activeInHierarchy) //refresh building UI if built
        {
            BM.i.AltUI();
            BM.i.AltUI();
        }
    }

    IEnumerator Pylon()
    {
        while (true)
        {
            mag.demand = Mathf.Max(0,Mathf.FloorToInt(0.75f * mag.demand));
            if (cloneNecessary)
            {
                CloneMaggies();
            }
            if (magnets.Count > 0)
            {
                yield return StartCoroutine(Deploy());
                if (mag.orbs.Count >= mag.capacity || mag.orbs.Count >= Mathf.RoundToInt(0.5f * mag.capacity) && mag.demand == 0)
                {
                    yield return StartCoroutine(SendExcess());
                }
                else if (mag.n < mag.capacity) //so do nothing if transient + orbs = cap
                {
                    yield return StartCoroutine(Obtain());
                }
            }
            yield return new WaitForFixedUpdate();
        }
    }

    //IEnumerator ChiefPylon()
    //{
    //    while (true)
    //    {
    //        for (int i = 0; i < magnets.Count; i++)
    //        {
    //            if(magnets.Count <= i)
    //            {
    //                continue;
    //            }
    //            if (magnets[i] == null)
    //            {
    //                continue;
    //            }
    //            OrbMagnet m = magnets[i];
    //            if (m.typ == "pylon")
    //            {
    //                if (m.n < mag.n)
    //                {
    //                    if (m.n < m.capacity && mag.n > 0)
    //                    {
    //                        mag.SendOrb(m, true, false); //try make all local pylons even
    //                        yield return new WaitForSeconds(refreshRate);
    //                        yield return new WaitForSeconds(refreshRate);
    //                    }
    //                }
    //            }
    //        }
    //        yield return new WaitForSeconds(refreshRate);
    //    }
    //}

    IEnumerator Obtain()
    {
        for (int i = 0; i < magnets.Count; i++)
        {
            if (magnets[i] == null)
            {
                continue;
            }
            OrbMagnet m = magnets[i];
            if (mag.n < mag.capacity)
            {
                if (m.typ == OrbMagnet.OrbType.Store)
                {
                    m.SendOrb(mag, true, false);
                    yield return new WaitForSeconds(refreshRate);
                }
            }
            else
            {
                yield break;
            }
        }
    }
    IEnumerator SendExcess()
    {
        for (int i = 0; i < magnets.Count; i++)
        {
            if (magnets[i] == null)
            {
                continue;
            }
            OrbMagnet m = magnets[i];
            if (m.typ == OrbMagnet.OrbType.Store)
            {
                if (m.n < m.capacity)
                {
                    if (mag.orbs.Count > Mathf.Round(mag.capacity / 2))
                    {
                        mag.SendOrb(m, false, false);
                        yield return new WaitForSeconds(refreshRate);
                     
                    }
                }
            }
        }
    }


    IEnumerator Deploy()
    {
        for (int i = 0; i < magnets.Count; i++)
        {
            if (magnets[i] == null)
            {
                continue;
            }
            OrbMagnet m = magnets[i];
            if (m.typ == OrbMagnet.OrbType.Task)
            {
                mag.demand += Mathf.Max(0, m.capacity - m.n - 0.5f * mag.n) * TypeCoef();
                if (mag.orbs.Count > 0 && m.n < m.capacity)
                {
                    mag.SendOrb(m, false, true);
                    yield return new WaitForSeconds(refreshRate);
                }
            }
        }
    }

    void Update() 
    {
        if (magnets.Count == 0 || t > 0f)
        {
            t -= Time.deltaTime;
            return;
        }
        t = refreshRate;
        for (int i = 0; i < magnets.Count; i++)
        {
            if (magnets[i] == null)
            {
                continue;
            }
            if (magnets.Count <= i)
            {
                continue;
            }
            OrbMagnet m = magnets[i];
            if (m.typ == OrbMagnet.OrbType.Pylon)
            {
                if (m.demand > mag.demand) //send orbs to those with more demand and set demand
                {
                    mag.demand = Mathf.Max(mag.demand, Mathf.Min(m.demand - 2, Mathf.FloorToInt(0.7f * m.demand)));
                    if (m.n < m.capacity)
                    {
                        mag.SendOrb(m, true, false);
                    }
                }
                else if (mag.demand == 0) //distribute without demand
                {
                    if (mag.n > m.n + 1 && m.n < m.capacity)
                    {
                        mag.SendOrb(m, true, false);
                    }
                }
                if (m.n < mag.n && m.n < m.capacity && mag.n > mag.capacity) //if over capacity (throne mag) try make all local pylons even
                {
                    mag.SendOrb(m, true, false);
                }
            }
        }
    }

    private void CloneMaggies()
    {
        magnets.Clear();
        foreach(OrbMagnet om in magnetsOfficial)
        {
            magnets.Add(om);
        }
        cloneNecessary = false;
    }

    public override void OnDeath()
    {
        ResourceManager.instance.ChangePylons(this, false);
        base.OnDeath();
    }

    private int TypeCoef()
    {
        if(orbType == 0)
        {
            return 1;
        }
        if(orbType == 1)
        {
            return 3;
        }
        if (orbType == 2)
        {
            return 15;
        }
        if (orbType == 3)
        {
            return 45;
        }
        throw new Exception("Wrong orbType in pylon!");
    }
}
