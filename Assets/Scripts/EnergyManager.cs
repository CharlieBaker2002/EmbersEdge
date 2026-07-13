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
        UnregisterPadArea(pad, pad.anchorCell, pad.gridSize);
    }

    /// <summary>Unregister a pad's claim from an explicit footprint — used when the footprint the
    /// pad registered under no longer matches its current one (rotation already swapped
    /// anchorCell/gridSize by the time the claim moves).</summary>
    public void UnregisterPadArea(EnergyPad pad, Vector2Int anchor, Vector2Int size)
    {
        if (pad == null) return;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        bool changed = false;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                var cell = anchor + new Vector2Int(x, y);
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

    /// <summary>The grid re-anchored its origin (map growth): every registered cell moves by one
    /// uniform offset. Remap both cell maps and let consumers re-resolve their adjacency.</summary>
    public void ShiftFrame(Vector2Int d)
    {
        if (d == Vector2Int.zero) return;
        var shiftedSources = new List<KeyValuePair<Vector2Int, List<IEnergyAccumulator>>>(sourcesAt);
        sourcesAt.Clear();
        foreach (var kv in shiftedSources) sourcesAt[kv.Key + d] = kv.Value;
        var shiftedPads = new List<KeyValuePair<Vector2Int, EnergyPad>>(padFootprintAt);
        padFootprintAt.Clear();
        foreach (var kv in shiftedPads) padFootprintAt[kv.Key + d] = kv.Value;
        OnPadsChanged?.Invoke();
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

    #endregion

    #region Ember
    
    private List<Building> bs = new();
    private readonly Dictionary<Building,int> emberCount = new();
    public List<EmberStoreBuilding> emberStores;
    /// <summary>The first Ember Store to come online — the small store already built at base. Dungeon
    /// ember (<see cref="EmberStore.Bank"/>) lands here by default; see <see cref="DepositDungeonEmber"/>.</summary>
    public EmberStoreBuilding defaultEmberStore;
    public static List<Constructor> constructors;
    public static List<Constructor> toBeBuilt;
    /// <summary>Ember generators participating in the cable network as sinks (they burn delivered ember into energy).</summary>
    public List<Generator> emberGens = new();

    private bool extracting = false;

    public float constructCableTimer = -1f;

    private void Start()
    {
        SpawnManager.instance.onWaveComplete += () => StartCoroutine(DoExtractors());
        GS.OnNewEra += _ => RegenerateCables();
    }

    /// <summary>An Ember Store came online. The first one registered (the small store already
    /// built at base) becomes the default landing spot for dungeon ember.</summary>
    public void RegisterEmberStore(EmberStoreBuilding b)
    {
        if (defaultEmberStore == null) defaultEmberStore = b;
        emberStores.Add(b);
        CreateCableConnections();
    }

    public void UnregisterEmberStore(EmberStoreBuilding b)
    {
        emberStores.Remove(b);
        if (defaultEmberStore == b) defaultEmberStore = emberStores.FirstOrDefault();
        CreateCableConnections();
    }

    /// <summary>
    /// Ember earned in the dungeon (<see cref="EmberStore.Bank"/>) lands in the default base store
    /// first. If that store is full, the overflow spills into whichever other Ember Store buildings
    /// in the network still have room (smallest capacity first, same ordering the cable network
    /// already uses). If every store is full, the remainder is banked on the default store anyway
    /// so dungeon ember is never lost.
    /// </summary>
    public void DepositDungeonEmber(int n)
    {
        if (n <= 0) return;
        int remaining = n;

        if (defaultEmberStore != null && defaultEmberStore.connect != null)
            remaining = FillStore(defaultEmberStore, remaining);

        if (remaining > 0 && emberStores.Count > 1)
        {
            UpdateEmberStores();   // smallest-capacity stores first
            foreach (EmberStoreBuilding store in emberStores)
            {
                if (remaining <= 0) break;
                if (store == defaultEmberStore) continue;
                remaining = FillStore(store, remaining);
            }
        }

        if (remaining > 0)
        {
            EmberStoreBuilding fallback = defaultEmberStore != null ? defaultEmberStore : emberStores.FirstOrDefault();
            if (fallback != null && fallback.connect != null)
            {
                fallback.connect.ember += remaining;
                fallback.Refresh();
            }
        }
    }

    /// <summary>
    /// Plan where n incoming dungeon embers should land: one connector per ember, assigned to the
    /// buildings that want them in the same priority order the cable network fills (constructors →
    /// ember generators → stores), one each round-robin. Whatever nothing has room for piles onto
    /// the default store anyway — dungeon ember is never lost. The result is only short when there
    /// are no stores at all.
    /// </summary>
    public List<EmberConnector> ResolveEmberDemand(int n)
    {
        var result = new List<EmberConnector>(Mathf.Max(0, n));
        if (n <= 0) return result;
        var room = new Dictionary<EmberConnector, int>();

        void Fill(IEnumerable<EmberConnector> source)
        {
            var list = source.Where(c => c != null).Distinct().ToList();
            foreach (EmberConnector c in list)
                if (!room.ContainsKey(c)) room[c] = Mathf.Max(0, c.maxEmber - c.ember - c.emberTravel);
            bool assigned = true;
            while (result.Count < n && assigned)
            {
                assigned = false;
                foreach (EmberConnector c in list)
                {
                    if (result.Count >= n) break;
                    if (room[c] <= 0) continue;
                    room[c]--;
                    result.Add(c);
                    assigned = true;
                }
            }
        }

        if (constructors != null) Fill(constructors.Where(x => x != null).Select(x => x.connect));
        Fill(emberGens.Where(g => g != null).Select(g => g.connect));
        Fill(emberStores.Where(s => s != null).Select(s => s.connect));

        EmberConnector fallback = defaultEmberStore != null ? defaultEmberStore.connect
            : emberStores.Where(s => s != null && s.connect != null).Select(s => s.connect).FirstOrDefault();
        while (result.Count < n && fallback != null) result.Add(fallback);
        return result;
    }

    int FillStore(EmberStoreBuilding store, int n)
    {
        EmberConnector c = store.connect;
        int room = Mathf.Max(0, c.maxEmber - c.ember);
        int add = Mathf.Min(room, n);
        if (add <= 0) return n;
        c.ember += add;
        store.Refresh();
        return n - add;
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
        // All extractors pulse together — MapManager commits each expansion instantly and just
        // retargets the animated outline, so simultaneous ChangeMapAsync calls are safe. Snapshot
        // the list first: an extractor that hits max distance disables itself and self-removes.
        List<Coroutine> running = new List<Coroutine>();
        foreach (Extractor ex in Extractor.extractors.ToList())
        {
            running.Add(StartCoroutine(ex.Animate()));
        }
        foreach (Coroutine co in running)
        {
            yield return co;
        }
        extracting = false;
        // ONE settling replan once the whole pulse has landed (replaces the old wave-complete
        // QA(UpdateEmber, 3), which raced the pulse: it launched the store→constructor refill
        // first, so each collection's replan saw the constructor as covered and shipped fresh
        // ember out to the very stores refilling the base — crossing flows that read as embers
        // rebounding base→store→base).
        UpdateEmber();
    }

    /// <summary>Uncommitted room on a connector — capacity minus what it holds AND what's already
    /// promised to it (inbound flights / queued jobs, both tracked in emberTravel).</summary>
    static int EmberRoom(EmberConnector c) => c == null ? 0 : c.maxEmber - c.ember - c.emberTravel;

    /// <summary>
    /// Route ONE freshly-collected extractor ember to the connector that wants it — constructors,
    /// then ember generators, then stores (the same priority <see cref="UpdateEmber"/>'s full
    /// replan fills in) — along the shortest cable route. Deliberately NOT a full UpdateEmber:
    /// a global replan per collected ember re-decides the whole network once a second during a
    /// pulse and shuttles already-settled ember around (the same per-arrival-rebalance trap
    /// documented on <see cref="EmberStore.Deliver"/>). Routing just the new ember leaves every
    /// existing plan alone; <see cref="DoExtractors"/> runs one settling replan at pulse end.
    /// </summary>
    public void RouteExtractedEmber(EmberConnector from)
    {
        if (from == null || from.ember + from.emberTravel <= 0) return;   // nothing uncommitted to send
        UpdateEmberStores();   // constructors: neediest first; stores: smallest first

        EmberConnector dest = null;
        foreach (Constructor c in constructors)
        {
            if (c != null && EmberRoom(c.connect) > 0) { dest = c.connect; break; }
        }
        if (dest == null)
        {
            foreach (Generator g in emberGens)
            {
                if (g != null && EmberRoom(g.connect) > 0) { dest = g.connect; break; }
            }
        }
        if (dest == null)
        {
            int fewest = int.MaxValue;   // emptiest store first — converges on the even spread the replan targets
            foreach (EmberStoreBuilding s in emberStores)
            {
                if (s == null || s.connect == null || EmberRoom(s.connect) <= 0) continue;
                int committed = s.connect.ember + s.connect.emberTravel;
                if (committed < fewest) { fewest = committed; dest = s.connect; }
            }
        }
        if (dest == null) return;   // network full — the ember waits at the extractor

        List<List<EmberConnector>> paths = CalculateShortestRoutes(
            new List<EmberConnector> { from }, new List<EmberConnector> { dest });
        if (paths.Count == 0) return;   // no cable route — the next full replan retries
        List<EmberConnector> path = paths[0];
        from.emberTravel--;
        dest.emberTravel++;
        path.RemoveAt(0);
        from.jobs.Add(path);
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
            // Net of queued jobs: an ember routed but not yet dispatched still sits in c.ember
            // while the destination's emberTravel already counts it — c.emberTravel is -1 for
            // each such job, so ember + emberTravel counts every physical ember exactly once.
            sum += c.ember + c.emberTravel;
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
        List<List<EmberConnector>> paths = CalculateShortestRoutes(starts,ends); //for each start, find the shortest path to each end, returns a list ordered by shortest distance (evaluating inter-connector distance sums)
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
    
    //reused across CalculateShortestRoutes calls (main thread only, cleared before every use)
    readonly List<EmberConnector> routeConnectors = new();
    readonly HashSet<EmberConnector> routeConnectorSet = new();
    readonly Dictionary<EmberConnector, float> routeDistances = new();
    readonly Dictionary<EmberConnector, EmberConnector> routePrevious = new();
    readonly HashSet<EmberConnector> routeUnvisited = new();

    void AddRouteConnector(EmberConnector c)
    {
        if (routeConnectorSet.Add(c))
        {
            routeConnectors.Add(c);
        }
    }

    // Fixed CalculateShortestRoutes method
    List<List<EmberConnector>> CalculateShortestRoutes(List<EmberConnector> starts, List<EmberConnector> ends)
    {
        var allPaths = new List<(List<EmberConnector> path, float distance)>();

        // Get all connectors in the game (same order + first-occurrence dedupe as the old
        // Concat().Distinct() chain, without rebuilding LINQ enumerators per ember event)
        routeConnectors.Clear();
        routeConnectorSet.Clear();
        foreach (var x in emberStores) AddRouteConnector(x.connect);
        foreach (var x in constructors) AddRouteConnector(x.connect);
        foreach (var x in Extractor.extractors) AddRouteConnector(x.connect);
        foreach (var x in EmberCannon.ecs) AddRouteConnector(x.connect);
        foreach (var g in emberGens)
        {
            if (g != null && g.connect != null) AddRouteConnector(g.connect);
        }
        var allConnectors = routeConnectors;

        // For each start connector
        foreach (var start in starts)
        {
            // Run Dijkstra's algorithm from this start
            var distances = routeDistances;
            var previous = routePrevious;
            var unvisited = routeUnvisited;
            distances.Clear();
            previous.Clear();
            unvisited.Clear();

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

        // One pass is enough: Construct() flips c.constructing synchronously, so a constructor
        // can dispatch at most once per frame anyway (the old bs.Count outer loop was dead weight).
        foreach (Constructor c in constructors)
        {
            if (c.constructing || c.connect.ember <= 0 || c.connect.ember + c.connect.emberTravel <= 0) continue;

            // Build in the order placed: the earliest-added task still needing work,
            // finishing it before moving on (tasks are appended in placement order).
            Building task = null;
            for (int i = 0; i < c.tasks.Count; i++)
            {
                if (c.tasks[i].numIconsTrue > 0)
                {
                    task = c.tasks[i];
                    break;
                }
            }

            if (task == null) continue;           // nothing it can build right now

            c.Construct(task);
            emberCount[task]++;                   // track fairness
            task.numIconsTrue--;
            if (task.numIconsTrue == 0) RemoveBuilding(task);
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
    
