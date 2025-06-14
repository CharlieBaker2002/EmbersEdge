using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EnergyManager : MonoBehaviour
{
    public static EnergyManager i;

    #region Energy
    public List<Pylon> pylons;
    private List<List<Battery>> grids = new(); // batteries sorted in ascending max energy so update grid works
    public List<Battery> allBatteries;
    
    private void Awake()
    {
        i = this;
        GridManager.i?.RefreshEnergyCells();   // initial energy overlay
    }

    public bool NewBattery(Battery b)
    {
        foreach (Pylon p in pylons)
        {
            if (Vector2.Distance(b.transform.position, p.transform.position) < p.reachDistance)
            {
                p.batteries.Add(b);
            }
        }
        CreateGrids();
        allBatteries.Add(b);
        Debug.Log("new battery!");
        return pylons.Any(x => x.batteries.Contains(b));
    }
    
    public void RemoveBattery(Battery b)
    {
        foreach (Pylon p in pylons)
        {
            p.batteries.Remove(b);
        }
        allBatteries.Remove(b);
        CreateGrids();
        Debug.Log("rem battery");
    }
    
    public void NewPylon(Pylon p)
    {
        foreach (Pylon x in pylons)
        {
            if (Vector2.Distance(x.transform.position, p.transform.position) < Mathf.Max(p.reachDistance,x.reachDistance))
            {
                x.connections.Add(p);
                p.connections.Add(x);
            }
        }
        pylons.Add(p);
        foreach (Battery b in allBatteries)
        {
            if(Vector2.Distance(b.transform.position, p.transform.position) < p.reachDistance)
            {
                p.batteries.Add(b);
            }
        }
        CreateGrids();
        GridManager.i.OnPylonChanged();   // update energy overlay
    }

    public void RemovePylon(Pylon p)
    {
        pylons.Remove(p);
        foreach (Pylon x in pylons)
        {
            x.connections.Remove(p);
        }
        CreateGrids();
        Debug.Log("rem pylon");
        GridManager.i.OnPylonChanged();   // update energy overlay
    }
    
    void CreateGrids()
    {
        grids = new List<List<Battery>>();
        var visited = new HashSet<Pylon>();

        foreach (var start in pylons)
        {
            if (visited.Contains(start))
                continue;

            // 1) Flood‑fill connected pylons
            var stack = new Stack<Pylon>();
            var component = new List<Pylon>();
            stack.Push(start);
            visited.Add(start);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);

                foreach (var neighbor in current.connections)
                {
                    if (pylons.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        stack.Push(neighbor);
                    }
                }
            }

            // 2) Gather and sort batteries
            var batteriesInGrid = component
                .SelectMany(p => p.batteries)
                .Distinct()
                .OrderBy(b => b.maxEnergy)
                .ToList();

            // 3) Assign gridID and add to grids
            var gridIndex = grids.Count;
            for (int i = 0; i < batteriesInGrid.Count; i++)
                batteriesInGrid[i].gridID = gridIndex;

            grids.Add(batteriesInGrid);
        }
    }

    public void UpdateGrid(int gridID)
    {
        float sum = 0f;
        foreach (Battery b in grids[gridID])
        {
            sum += b.energy;
        }
        for (int i = 0; i < grids[gridID].Count; i++)
        {
            sum -= grids[gridID][i].Set(sum / (grids[gridID].Count - i)); // distribute energy
        }
    }

    #endregion

    #region Ember
    
    private List<Building> bs = new();
    private readonly Dictionary<Building,int> emberCount = new();
    public List<EmberStore> emberStores;
    public List<Constructor> constructors;

    private bool extracting = false;

    private void Start()
    {
        SpawnManager.instance.onWaveComplete += () => StartCoroutine(DoExtractors());
        SpawnManager.instance.onWaveComplete += () => GS.QA(UpdateEmber, 3);
    }

    public void UpdateEmberStores()
    {
        constructors = constructors.OrderBy(x => x.tasks.Sum(z => z.numIconsTrue)).ToList();
        emberStores = emberStores.OrderBy(x => x.maxEmber).ToList();
    }

    public IEnumerator DoExtractors()
    {
        if(extracting) yield break;
        extracting = true;
        yield return null;
        foreach (Extractor e in Extractor.extractors)
        {
            yield return StartCoroutine(e.Animate());
        }
        extracting = false;
    }

    public void UpdateEmber()
    {
        UpdateEmberStores();
        float sum = 0f;
        foreach (EmberStore e in emberStores)
        {
            sum += e.ember;
            e.desiredEmber = 0;
        }
        foreach(EmberStore c in constructors.Select(x=> x.store))
        {
            sum += c.ember;
            c.desiredEmber = 0;
        }
        while (sum > 0 && constructors.Any(c=>c.store.desiredEmber != c.store.maxEmber)) //add one evenly to each constructor until they are all full.
        {
            foreach(Constructor c in constructors)
            {
                if (c.store.desiredEmber >= c.store.maxEmber) continue;
                c.store.desiredEmber += 1;
                sum -= 1;
                if (sum <= 0) break;
            }
            
        }
        while (sum > 0) //add one evenly.
        {
            foreach (var t in emberStores)
            {
                if (t.desiredEmber >= t.maxEmber) continue;
                t.desiredEmber += 1;
                sum -= 1;
                if (sum <= 0) break;
            }
        }
        
        //DO CONNECTIONS
        
        foreach(EmberStore e in emberStores)
        {
            e.b?.Refresh();
        }
    }
    
    public void AddBuilding(Building b)
    {
        if(bs.Contains(b)) return;
        emberCount.Add(b,0);
        bs.Add(b);
        foreach(Constructor c in constructors)
        {
            if(Vector2.Distance(c.transform.position,b.transform.position) <= c.radius)
            {
                c.tasks.Add(b);
            }
        }
    }
    
    public void RemoveBuilding(Building b)
    {
        if(!bs.Contains(b)) return;
        emberCount.Remove(b);
        bs.Remove(b);
        foreach (Constructor c in constructors)
        {
            c.tasks.Remove(b);
        }
    }

    private void Update()
    {
        for (int x = 0; x < bs.Count; x++)
        {
            foreach (Constructor c in constructors)
            {
                if (c.constructing || c.store.ember <= 0) continue;

                // Pick the task with the fewest embers *that this constructor can reach*
                var task = c.tasks
                    .OrderBy(t => emberCount[t])
                    .FirstOrDefault();

                if (task == null) continue;           // nothing it can build right now

                c.Construct(task);
                emberCount[task]++;                   // track fairness

                task.numIconsTrue--;
                if (task.numIconsTrue == 0) RemoveBuilding(task);
            }
        }
    }

    #endregion
}