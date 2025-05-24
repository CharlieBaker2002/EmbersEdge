using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EnergyManager : MonoBehaviour
{
    public static EnergyManager i;

    #region Energy

    public List<Pylon> pylons;
    private List<List<Battery>> grids = new(); // batteries sorted in ascending max energy so update grid works

    private void Awake()
    {
        i = this;
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

    public List<EmberStore> emberStores;
    public List<Extractor> extractors;

    public void UpdateEmberStores()
    {
        emberStores = emberStores.OrderBy(x => x.maxEmber).ToList();
    }

    public void UpdateEmber()
    {
        float sum = 0f;
        foreach (EmberStore e in emberStores)
        {
            sum += e.ember;
        }
        for (int i = 0; i < emberStores.Count; i++)
        {
            sum -= emberStores[i].Set(sum / (emberStores.Count - i));
        }
    }

    #endregion
}
