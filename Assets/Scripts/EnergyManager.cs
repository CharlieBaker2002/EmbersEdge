using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EnergyManager : MonoBehaviour
{
    public static EnergyManager i;

    #region Energy

    // Sources (pads aggregating batteries, generators with internal storage, pylons relaying
    // upstreams) register themselves at every cell they want to be visible from. Multiple
    // sources may share a cell — consumers see them all via SourcesAt(cell) and aggregate.
    private readonly Dictionary<Vector2Int, List<IEnergyAccumulator>> sourcesAt = new();
    private static readonly List<IEnergyAccumulator> emptySources = new();

    // Separate footprint map used by Battery.Drop (PadAt) so the hub's physical area is
    // still findable for battery insertion even though its consumer-adjacency claim is
    // only the forward strip.
    private readonly Dictionary<Vector2Int, EnergyPad> padFootprintAt = new();

    /// <summary>Fires whenever a source is registered or unregistered. Power consumers re-resolve adjacency on this signal.</summary>
    public event Action OnPadsChanged;

    /// <summary>
    /// Register an EnergyPad's source claim at its full footprint. For hubs (single-battery
    /// directional pads), the cell registration is the same as a regular pad — but
    /// BuildingPower applies a directional filter (see <see cref="HubAccessibleFrom"/>)
    /// so only consumers in the hub's forward column actually pick it up.
    /// </summary>
    public void RegisterPad(EnergyPad pad)
    {
        if (pad == null) return;
        var size = pad.gridSize;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                var cell = pad.anchorCell + new Vector2Int(x, y);
                padFootprintAt[cell] = pad;
                AddSourceAt(cell, pad);
            }
        }
        OnPadsChanged?.Invoke();
    }

    public void UnregisterPad(EnergyPad pad)
    {
        if (pad == null) return;
        var size = pad.gridSize;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        bool changed = false;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                var cell = pad.anchorCell + new Vector2Int(x, y);
                if (padFootprintAt.TryGetValue(cell, out var existing) && existing == pad)
                    padFootprintAt.Remove(cell);
                if (RemoveSourceAt(cell, pad)) changed = true;
            }
        }
        if (changed) OnPadsChanged?.Invoke();
    }

    /// <summary>
    /// Directional filter for hubs. The consumer's footprint must lie entirely on the
    /// forward side of the hub's front edge AND its perpendicular range must overlap the
    /// hub's perpendicular range. That gives exactly the cells directly in front of the
    /// hub, matching its width — no diagonal/side leakage from cardinal adjacency.
    /// </summary>
    public static bool HubAccessibleFrom(EnergyPad hub, Vector2Int conAnchor, Vector2Int conSize)
    {
        int dx = Mathf.RoundToInt(hub.transform.up.x);
        int dy = Mathf.RoundToInt(hub.transform.up.y);
        // Collapse near-diagonal facings to the dominant axis.
        if (dx != 0 && dy != 0)
        {
            if (Mathf.Abs(hub.transform.up.x) >= Mathf.Abs(hub.transform.up.y)) dy = 0; else dx = 0;
        }
        if (dx == 0 && dy == 0) dy = 1;

        int hX0 = hub.anchorCell.x, hX1 = hub.anchorCell.x + hub.gridSize.x;
        int hY0 = hub.anchorCell.y, hY1 = hub.anchorCell.y + hub.gridSize.y;
        int cX0 = conAnchor.x, cX1 = conAnchor.x + conSize.x;
        int cY0 = conAnchor.y, cY1 = conAnchor.y + conSize.y;

        if (dy > 0) return cY0 >= hY1 && cX0 < hX1 && cX1 > hX0;
        if (dy < 0) return cY1 <= hY0 && cX0 < hX1 && cX1 > hX0;
        if (dx > 0) return cX0 >= hX1 && cY0 < hY1 && cY1 > hY0;
        return cX1 <= hX0 && cY0 < hY1 && cY1 > hY0;
    }

    /// <summary>Register any IEnergyAccumulator (generator, pylon, …) across a building's footprint.</summary>
    public void RegisterSource(IEnergyAccumulator source, Vector2Int anchor, Vector2Int size)
    {
        if (source == null) return;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                AddSourceAt(anchor + new Vector2Int(x, y), source);
            }
        }
        OnPadsChanged?.Invoke();
    }

    public void UnregisterSource(IEnergyAccumulator source, Vector2Int anchor, Vector2Int size)
    {
        if (source == null) return;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        bool changed = false;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (RemoveSourceAt(anchor + new Vector2Int(x, y), source)) changed = true;
            }
        }
        if (changed) OnPadsChanged?.Invoke();
    }

    void AddSourceAt(Vector2Int cell, IEnergyAccumulator source)
    {
        if (!sourcesAt.TryGetValue(cell, out var list))
        {
            list = new List<IEnergyAccumulator>();
            sourcesAt[cell] = list;
        }
        if (!list.Contains(source)) list.Add(source);
    }

    bool RemoveSourceAt(Vector2Int cell, IEnergyAccumulator source)
    {
        if (!sourcesAt.TryGetValue(cell, out var list)) return false;
        if (!list.Remove(source)) return false;
        if (list.Count == 0) sourcesAt.Remove(cell);
        return true;
    }

    /// <summary>All sources claiming this cell (pads, generators, pylons, …). Read-only.</summary>
    public IReadOnlyList<IEnergyAccumulator> SourcesAt(Vector2Int cell)
    {
        return sourcesAt.TryGetValue(cell, out var list) ? list : emptySources;
    }

    /// <summary>Back-compat alias used by BuildingPower's neighbour walk.</summary>
    public IReadOnlyList<IEnergyAccumulator> PadsAt(Vector2Int cell) => SourcesAt(cell);

    /// <summary>Register a single-cell claim on an arbitrary source (pylons routing to remote consumers, …).</summary>
    public void RegisterSourceAt(IEnergyAccumulator source, Vector2Int cell)
    {
        if (source == null) return;
        AddSourceAt(cell, source);
        OnPadsChanged?.Invoke();
    }

    public void UnregisterSourceAt(IEnergyAccumulator source, Vector2Int cell)
    {
        if (source == null) return;
        if (RemoveSourceAt(cell, source)) OnPadsChanged?.Invoke();
    }

    /// <summary>Legacy aliases kept so EnergyPad-specific callers don't break during the source-generalisation.</summary>
    public void RegisterPadAt(EnergyPad pad, Vector2Int cell) => RegisterSourceAt(pad, cell);
    public void UnregisterPadAt(EnergyPad pad, Vector2Int cell) => UnregisterSourceAt(pad, cell);

    /// <summary>EnergyPad physically occupying this cell, or null. Used by Battery.Drop.</summary>
    public EnergyPad PadAt(Vector2Int cell)
    {
        padFootprintAt.TryGetValue(cell, out var pad);
        return pad;
    }

    private void Awake()
    {
        i = this;
        GridManager.i?.RefreshEnergyCells();   // initial energy overlay
        existingCables = new List<GameObject>();
    }

    // public bool NewBattery(Battery b)
    // {
    //     foreach (Pylon p in pylons)
    //     {
    //         if (Vector2.Distance(b.transform.position, p.transform.position) < p.reachDistance)
    //         {
    //             p.batteries.Add(b);
    //         }
    //     }
    //     CreateGrids();
    //     allBatteries.Add(b); ;
    //     return pylons.Any(x => x.batteries.Contains(b));
    // }
    //
    // public void RemoveBattery(Battery b)
    // {
    //     foreach (Pylon p in pylons)
    //     {
    //         p.batteries.Remove(b);
    //     }
    //     allBatteries.Remove(b);
    //     CreateGrids();
    // }
    //
    // public void NewPylon(Pylon p)
    // {
    //     foreach (Pylon x in pylons)
    //     {
    //         if (Vector2.Distance(x.transform.position, p.transform.position) < Mathf.Max(p.reachDistance,x.reachDistance))
    //         {
    //             x.connections.Add(p);
    //             p.connections.Add(x);
    //         }
    //     }
    //     pylons.Add(p);
    //     // foreach (Battery b in allBatteries)
    //     // {
    //     //     if(Vector2.Distance(b.transform.position, p.transform.position) < p.reachDistance)
    //     //     {
    //     //         p.batteries.Add(b);
    //     //     }
    //     // }
    //     CreateGrids();
    //     GridManager.i.OnPylonChanged();   // update energy overlay
    // }
    //
    // public void RemovePylon(Pylon p)
    // {
    //     pylons.Remove(p);
    //     foreach (Pylon x in pylons)
    //     {
    //         x.connections.Remove(p);
    //     }
    //     CreateGrids();
    //     Debug.Log("rem pylon");
    //     GridManager.i.OnPylonChanged();   // update energy overlay
    // }
    //
    // void CreateGrids()
    // {
    //     grids = new List<List<Battery>>();
    //     var visited = new HashSet<Pylon>();
    //
    //     foreach (var start in pylons)
    //     {
    //         if (visited.Contains(start))
    //             continue;
    //
    //         // 1) Flood‑fill connected pylons
    //         var stack = new Stack<Pylon>();
    //         var component = new List<Pylon>();
    //         stack.Push(start);
    //         visited.Add(start);
    //
    //         while (stack.Count > 0)
    //         {
    //             var current = stack.Pop();
    //             component.Add(current);
    //
    //             foreach (var neighbor in current.connections)
    //             {
    //                 if (pylons.Contains(neighbor) && !visited.Contains(neighbor))
    //                 {
    //                     visited.Add(neighbor);
    //                     stack.Push(neighbor);
    //                 }
    //             }
    //         }
    //
    //         // 2) Gather and sort batteries
    //         var batteriesInGrid = component
    //             .SelectMany(p => p.batteries)
    //             .Distinct()
    //             .OrderBy(b => b.maxEnergy)
    //             .ToList();
    //
    //         // 3) Assign gridID and add to grids
    //         var gridIndex = grids.Count;
    //         for (int i = 0; i < batteriesInGrid.Count; i++)
    //             batteriesInGrid[i].gridID = gridIndex;
    //
    //         grids.Add(batteriesInGrid);
    //     }
    // }
    //
    // public void UpdateGrid(int gridID)
    // {
    //     float sum = 0f;
    //     foreach (Battery b in grids[gridID])
    //     {
    //         sum += b.energy;
    //     }
    //     for (int i = 0; i < grids[gridID].Count; i++)
    //     {
    //         sum -= grids[gridID][i].Set(sum / (grids[gridID].Count - i)); // distribute energy
    //     }
    // }

    #endregion

    #region Ember
    
    private List<Building> bs = new();
    private readonly Dictionary<Building,int> emberCount = new();
    public List<EmberStoreBuilding> emberStores;
    public static List<Constructor> constructors;
    public static List<Constructor> toBeBuilt;
    /// <summary>Ember generators participating in the cable network as sinks (they burn delivered ember into energy).</summary>
    public List<Generator> emberGens = new();

    private bool extracting = false;

    public float constructCableTimer = -1f;

    private void Start()
    {
        SpawnManager.instance.onWaveComplete += () => StartCoroutine(DoExtractors());
        SpawnManager.instance.onWaveComplete += () => GS.QA(UpdateEmber, 3);
        GS.OnNewEra += _ => RegenerateCables();
    }
    
    public void UpdateEmberStores()
    {
        constructors = constructors.OrderByDescending(x => x.tasks.Sum(z => z.numIconsTrue)).ThenBy(y=>y.connect.ember).ToList();
        emberStores = emberStores.OrderBy(x => x.connect.maxEmber).ToList();
    }

    public IEnumerator DoExtractors()
    {
        if(extracting) yield break;
        extracting = true;
        yield return null;
        for(int i = 0; i < Extractor.extractors.Count; i++)
        {
            yield return StartCoroutine(Extractor.extractors[i].Animate());
        }
        extracting = false;
    }
    
    public void UpdateEmber()
    {
        UpdateEmberStores();
        float sum = 0f;
        foreach (EmberStoreBuilding e in emberStores)
        {
            sum += e.connect.ember + e.connect.emberTravel;
            e.connect.desiredEmber = 0;
        }
        foreach(Constructor c in constructors)
        {
            sum += c.connect.ember + c.connect.emberTravel;
            c.connect.desiredEmber = 0;
        }
        foreach (Generator g in emberGens)
        {
            if (g == null || g.connect == null) continue;
            sum += g.connect.ember + g.connect.emberTravel;
            g.connect.desiredEmber = 0;
        }

        foreach (EmberConnector c in Extractor.extractors.Select(x=>x.connect).Concat(EmberCannon.ecs.Select(x=>x.connect)))
        {
            sum += c.ember;
            c.desiredEmber = 0;
        }
        while (sum > 0 && constructors.Any(c=>c.connect.desiredEmber < c.connect.maxEmber)) //add one evenly to each constructor until they are all full.
        {
            foreach(Constructor c in constructors)
            {
                if (c.connect.desiredEmber >= c.connect.maxEmber) continue;
                c.connect.desiredEmber++;
                sum -= 1;
                if (sum <= 0) break;
            }
        }
        // Ember generators fill after constructors, before stores — they turn ember into energy
        // (more useful than banking it). Adjust this ordering if stores should win.
        while (sum > 0 && emberGens.Any(g => g != null && g.connect != null && g.connect.desiredEmber < g.connect.maxEmber))
        {
            foreach (Generator g in emberGens)
            {
                if (g == null || g.connect == null || g.connect.desiredEmber >= g.connect.maxEmber) continue;
                g.connect.desiredEmber++;
                sum -= 1;
                if (sum <= 0) break;
            }
        }
        while (sum > 0 && emberStores.Any(c=>c.connect.desiredEmber < c.connect.maxEmber)) //add one evenly.
        {
            foreach (var t in emberStores)
            {
                if (t.connect.desiredEmber >= t.connect.maxEmber) continue;
                t.connect.desiredEmber++;
                sum -= 1;
                if (sum <= 0) break;
            }
        }
  
        List<EmberConnector> starts = new List<EmberConnector>();
        List<EmberConnector> ends = new List<EmberConnector>();
        IEnumerable<EmberConnector> ecs = constructors.Select(x => x.connect).Concat(emberStores.Select(x => x.connect)).Concat(Extractor.extractors.Select(x => x.connect)).Concat(EmberCannon.ecs.Select(x => x.connect)).Concat(emberGens.Where(g => g != null && g.connect != null).Select(g => g.connect));
        foreach(EmberConnector e in ecs)
        {
            if (e.desiredEmber > e.ember + e.emberTravel)
            {
                ends.Add(e);
            }
            else if (e.desiredEmber < e.ember + e.emberTravel)
            {
                starts.Add(e);
            }
        }
        
        if(ends.Count == 0 || starts.Count == 0)
        {
            return; // no ember to transfer
        }
        Debug.Log(starts.Count);
        Debug.Log(ends.Count);
        List<List<EmberConnector>> paths = CalculateShortestRoutes(starts,ends); //for each start, find the shortest path to each end, returns a list ordered by shortest distance (evaluating inter-connector distance sums)
        // foreach(List<EmberConnector> path in paths)
        // {
        //     Debug.Log("i: " + i);
        //     for(int m = 0; m < path.Count; m++)
        //     {
        //         Debug.Log("M: " + m);
        //         Debug.Log(path[m].gameObject);
        //     }
        // }
        int protecc = 0;
        while (starts.Count > 0)
        {
            protecc++;
            if (protecc >= 200)
            {
                Debug.LogWarning("protecc update ember");
                break;
            }
            List<EmberConnector> path = paths[0];
            if (!ends.Contains(path[^1] ) || !starts.Contains(path[0]))
            {
                paths.RemoveAt(0);
                continue;
            }
            
            path[0].emberTravel--;
            path[^1].emberTravel++;
            EmberConnector start = path[0];
            List<EmberConnector> copy = new List<EmberConnector>();
            GS.CopyList(ref copy,path);
            copy.RemoveAt(0);
            start.jobs.Add(copy);
            bool remPath = false;
            if (start.emberTravel == start.desiredEmber - start.ember)
            {
                starts.Remove(start);
                remPath = true;
            }
            if(path[^1].emberTravel == path[^1].desiredEmber - path[^1].ember)
            {
                ends.Remove(path[^1]);
                remPath = true;
            }
            if (remPath)
            {
                paths.RemoveAt(0);
            }
        }

        StartCoroutine(WaitToDoLostJobs());

        IEnumerator WaitToDoLostJobs()
        {
            yield return new WaitForSeconds(0.1f); // wait a bit
            foreach (List<EmberConnector> path in EmberCable.lostjobs)
            {
                if (path.Count == 1)
                {
                    path[0].ember++;
                    path[0].emberTravel--;
                    path[0].onRefresh.Invoke();
                }
                else if(path.Count > 1)
                {
                    path[0].Chain(path);
                }
            }

            EmberCable.lostjobs = new List<List<EmberConnector>>();
        }
        
    }
    
    // Fixed CalculateShortestRoutes method
    List<List<EmberConnector>> CalculateShortestRoutes(List<EmberConnector> starts, List<EmberConnector> ends)
    {
        var allPaths = new List<(List<EmberConnector> path, float distance)>();
        
        // Get all connectors in the game
        var allConnectors = emberStores.Select(x => x.connect)
            .Concat(constructors.Select(x => x.connect))
            .Concat(Extractor.extractors.Select(x => x.connect))
            .Concat(EmberCannon.ecs.Select(x => x.connect))
            .Concat(emberGens.Where(g => g != null && g.connect != null).Select(g => g.connect))
            .Distinct()
            .ToList();
        
        // For each start connector
        foreach (var start in starts)
        {
            // Run Dijkstra's algorithm from this start
            var distances = new Dictionary<EmberConnector, float>();
            var previous = new Dictionary<EmberConnector, EmberConnector>();
            var unvisited = new HashSet<EmberConnector>();
            
            // Initialize all connectors
            foreach (var connector in allConnectors)
            {
                distances[connector] = float.MaxValue;
                unvisited.Add(connector);
            }
            
            distances[start] = 0f;
            
            // Dijkstra's main loop
            while (unvisited.Count > 0)
            {
                // Find unvisited node with minimum distance
                EmberConnector current = null;
                float minDist = float.MaxValue;
                foreach (var connector in unvisited)
                {
                    if (distances[connector] < minDist)
                    {
                        minDist = distances[connector];
                        current = connector;
                    }
                }
                
                if (current == null || minDist == float.MaxValue)
                    break; // No more reachable nodes
                    
                unvisited.Remove(current);
                
                // Update distances to neighbors
                foreach (var neighbor in current.connections)
                {
                    if (!unvisited.Contains(neighbor))
                        continue;
                        
                    float edgeDistance = Vector2.Distance(
                        current.transform.position, 
                        neighbor.transform.position
                    );
                    float altDistance = distances[current] + edgeDistance;
                    
                    if (altDistance < distances[neighbor])
                    {
                        distances[neighbor] = altDistance;
                        previous[neighbor] = current;
                    }
                }
            }
            
            // Build paths to each reachable end
            foreach (var end in ends)
            {
                if (!distances.ContainsKey(end) || distances[end] == float.MaxValue)
                    continue; // No path exists
                    
                // Reconstruct path
                var path = new List<EmberConnector>();
                var current = end;
                
                while (current != null)
                {
                    path.Add(current);
                    if (!previous.TryGetValue(current, out current))
                        break;
                }
                
                path.Reverse();
                
                // Verify the path is valid (starts with start, ends with end, has at least 2 nodes)
                if (path.Count >= 2 && path[0] == start && path[path.Count - 1] == end)
                {
                    allPaths.Add((path, distances[end]));
                }
            }
        }
        
        // Sort all paths by total distance and return
        return allPaths
            .OrderBy(p => p.distance)
            .Select(p => p.path)
            .ToList();
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
        if (constructCableTimer >= 0f)
        {
            constructCableTimer -= Time.deltaTime;
            if (constructCableTimer < 0f)
            {
                RegenerateCables();
            }
        }
        // Constructors hold ALL building work (including repairs) while a wave is live — they only
        // start once Ember's Edge goes quiet again (eeactive is cleared after the wave completes).
        if (SpawnManager.eeactive) return;

        for (int x = 0; x < bs.Count; x++)
        {
            foreach (Constructor c in constructors)
            {
                if (c.constructing || c.connect.ember <= 0 || c.connect.ember + c.connect.emberTravel <= 0) continue;

                // Build in the order placed: the earliest-added task still needing work,
                // finishing it before moving on (tasks are appended in placement order).
                var task = c.tasks
                    .FirstOrDefault(t => t.numIconsTrue > 0);

                if (task == null) continue;           // nothing it can build right now

                c.Construct(task);
                emberCount[task]++;                   // track fairness
                task.numIconsTrue--;
                if (task.numIconsTrue == 0) RemoveBuilding(task);
            }
        }
    }
    
     // Add this field at the top with other fields
     public EmberCable emberCablePrefab;
    private List<GameObject> existingCables;
    
    void RegenerateCables()
    {
        // Clear any existing cable connections first
        ClearExistingCables();
        
        // Get all ember connectors
        var allConnectors = emberStores.Select(x => x.connect)
            .Concat(constructors.Select(x => x.connect))
            .Concat(Extractor.extractors.Select(x => x.connect)).Concat(EmberCannon.ecs.Select(x=> x.connect))
            .Concat(emberGens.Where(g => g != null && g.connect != null).Select(g => g.connect))
            .ToList();
        
        // Create connections based on type rules
        foreach (var connector in allConnectors)
        {
            connector.connections.Clear();
            connector.cables.Clear();
            connector.cableConnectionDirections.Clear();
        }
        
        // Connect each connector to appropriate targets
        foreach (var connector in allConnectors)
        {
            switch (connector.taip)
            {
                case EmberConnector.typ.Generator:
                    ConnectGeneratorToNearestTarget(connector, allConnectors);
                    break;
                case EmberConnector.typ.Constructor:
                    ConnectConstructorToTargets(connector, allConnectors);
                    break;
                case EmberConnector.typ.Store:
                    ConnectStoreToStore(connector, allConnectors);
                    break;
            }
        }
        UpdateEmber();
    }
    
    // Add this method to create cable connections
    public void CreateCableConnections()
    {   
        constructCableTimer = 0.25f;
    }
    
    private void ConnectGeneratorToNearestTarget(EmberConnector generator, List<EmberConnector> allConnectors)
    {
        // Find nearest store or constructor
        var stores = allConnectors.Where(c => c.taip == EmberConnector.typ.Store).ToList();
        var constructors = allConnectors.Where(c => c.taip == EmberConnector.typ.Constructor).ToList();
        
        EmberConnector nearestTarget = null;
        float nearestDistance = float.MaxValue;
        
        // Check stores first
        foreach (var store in stores)
        {
            float dist = Vector2.Distance(generator.transform.position, store.transform.position);
            if (dist < nearestDistance)
            {
                nearestDistance = dist;
                nearestTarget = store;
            }
        }
        
        // If no stores, check constructors
        if (nearestTarget == null)
        {
            foreach (var constructor in constructors)
            {
                float dist = Vector2.Distance(generator.transform.position, constructor.transform.position);
                if (dist < nearestDistance)
                {
                    nearestDistance = dist;
                    nearestTarget = constructor;
                }
            }
        }
        
        if (nearestTarget != null && !generator.connections.Contains(nearestTarget))
        {
            CreateCableLine(generator, nearestTarget);
        }
    }
    
    private void ConnectConstructorToTargets(EmberConnector constructor, List<EmberConnector> allConnectors)
    {
        // Connect to nearest store
        var stores = allConnectors.Where(c => c.taip == EmberConnector.typ.Store).ToList();
        if (stores.Count > 0)
        {
            var nearestStore = stores.OrderBy(s => Vector2.Distance(constructor.transform.position, s.transform.position)).FirstOrDefault();
            if (nearestStore != null && !constructor.connections.Contains(nearestStore))
            {
                CreateCableLine(constructor, nearestStore);
            }
        }
        
        // Connect to nearest other constructor
        var otherConstructors = allConnectors.Where(c => c.taip == EmberConnector.typ.Constructor && c != constructor).ToList();
        if (otherConstructors.Count > 0)
        {
            var nearestConstructor = otherConstructors.OrderBy(c => Vector2.Distance(constructor.transform.position, c.transform.position)).FirstOrDefault();
            if (nearestConstructor != null && !constructor.connections.Contains(nearestConstructor))
            {
                CreateCableLine(constructor, nearestConstructor);
            }
        }
    }
    
    private void ConnectStoreToStore(EmberConnector store, List<EmberConnector> allConnectors)
    {
        // Connect to nearest other store
        var otherStores = allConnectors.Where(c => c.taip == EmberConnector.typ.Store && c != store).ToList();
        if (otherStores.Count > 0)
        {
            var nearestStore = otherStores.OrderBy(s => Vector2.Distance(store.transform.position, s.transform.position)).FirstOrDefault();
            if (nearestStore != null && !store.connections.Contains(nearestStore))
            {
                CreateCableLine(store, nearestStore);
            }
        }
    }
    
    private void CreateCableLine(EmberConnector start, EmberConnector end)
    {
        // Avoid duplicate connections
        if (start.connections.Contains(end) || end.connections.Contains(start))
            return;
        
        Vector2 startPos = start.transform.position;
        Vector2 endPos = end.transform.position;
        
        float cableSize = 0.09375f;
        Vector2 currentPos = startPos;
        
        // Vertical segment
        float verticalDistance = endPos.y - startPos.y;
        int verticalSteps = Mathf.RoundToInt(Mathf.Abs(verticalDistance) / cableSize);
        float verticalDirection = Mathf.Sign(verticalDistance);
        
        EmberCable previousCable = null;
        EmberCable firstCable = null;
        EmberCable lastCable = null;
        
        // Create vertical cables
        for (int i = 0; i < verticalSteps; i++)
        {
            currentPos.y = startPos.y + (i * cableSize * verticalDirection);
            EmberCable cable = Instantiate(emberCablePrefab, currentPos, Quaternion.identity);
            existingCables.Add(cable.gameObject);
            
            // Set rotation based on direction
            if (verticalDirection > 0)
                cable.transform.rotation = Quaternion.Euler(0, 0, 0); // Up
            else
                cable.transform.rotation = Quaternion.Euler(0, 0, 180); // Down
            
            // Store the first cable
            if (firstCable == null)
            {
                firstCable = cable;
                // Set the start connector as the end for the first cable
                cable.end = start;
                cable.endInFront = false; // This cable points back to start
            }
            
            // Link cables
            if (previousCable != null)
            {
                previousCable.nextCable = cable;
                cable.prevCable = previousCable;
            }
            
            previousCable = cable;
            lastCable = cable;
        }
        
        // Horizontal segment
        float horizontalDistance = endPos.x - startPos.x;
        int horizontalSteps = Mathf.RoundToInt(Mathf.Abs(horizontalDistance) / cableSize);
        float horizontalDirection = Mathf.Sign(horizontalDistance);
        
        currentPos.y = endPos.y;
        
        for (int i = 0; i <= horizontalSteps; i++)
        {
            currentPos.x = startPos.x + (i * cableSize * horizontalDirection);
            EmberCable cable = Instantiate(emberCablePrefab, currentPos, Quaternion.identity);
            existingCables.Add(cable.gameObject);
            
            // Set rotation based on direction
            if (horizontalDirection > 0)
                cable.transform.rotation = Quaternion.Euler(0, 0, 270); // Right
            else
                cable.transform.rotation = Quaternion.Euler(0, 0, 90); // Left
            
            // Store the first cable if we didn't create any vertical cables
            if (firstCable == null)
            {
                firstCable = cable;
                // Set the start connector as the end for the first cable
                cable.end = start;
                cable.endInFront = false; // This cable points back to start
            }
            
            // Link cables
            if (previousCable != null)
            {
                previousCable.nextCable = cable;
                cable.prevCable = previousCable;
            }
            
            previousCable = cable;
            lastCable = cable;
        }
        
        // Set up the last cable to connect to the end connector
        if (lastCable != null)
        {
            lastCable.end = end;
            lastCable.endInFront = true; // This cable points forward to end
        }
        
        // Set up connector references
        if (firstCable != null)
        {
            start.connections.Add(end);
            start.cables.Add(firstCable);
            start.cableConnectionDirections.Add(true); // Forward from start
            
            end.connections.Add(start);
            end.cables.Add(lastCable);
            end.cableConnectionDirections.Add(false); // Backward to start
        }
    }
    
    private void ClearExistingCables()
    {
        foreach (var cable in existingCables)
        {
            Destroy(cable.gameObject);
        }

        existingCables = new List<GameObject>();
    }
}

    #endregion
    
