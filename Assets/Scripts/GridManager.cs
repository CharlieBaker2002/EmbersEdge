using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Keeps track of the buildable grid and its visual overlay.</summary>
public class GridManager : MonoBehaviour
{
    public static GridManager i;

    #region Parameters ––––––––––––––––––––––––––––––––––––––––––––

    [Header("Grid geometry")]
    public int width  = 64;
    public int height = 64;
    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;
    [Tooltip("Extra world-unit margin, beyond the map's inner inset, that the build grid must stay inside.")]
    public float buildEdgeMargin = 1.5f;

    private Color clearColour     = new Color(0f, 0.4f, 0f, 0.5f); // green – inside constructor range
    private Color filledColour    = new Color(0.4f, 0f, 0f, 1f); // red – occupied
    private Color outColour    = new Color(0.05f, 0.05f, 0.05f, 0.5f); // black – out of range
    private Color brightClearColour   = new Color(0f, 1f, 0f, 1f); // super‑bright green
    private Color brightBlockedColour = new Color(0.8f, 0f, 0f, 1f);  // super‑bright red
    private Color energyFreeColour   = new Color(0f, 0.4f, 0f, 0.5f); // yellow – energy & buildable

    [SerializeField] Transform buildingGrid;
    [SerializeField] SpriteRenderer block;
    [SerializeField] SpriteRenderer energyBlock;

    #endregion

    #region Runtime state –––––––––––––––––––––––––––––––––––––––––

    bool[,] occupied;              // placed buildings
    bool[,] inRange;               // within range of a constructor this frame
    Color[,] baseColour;           // cache of the colour each tile should have when *not* highlighted
    SpriteRenderer[,] overlay;     // sprite for each cell
    SpriteRenderer[,] energyOverlay; // sprite for each energy cell

    Vector2Int lastAnchor = new(int.MinValue, int.MinValue);
    Vector2Int lastSize   = Vector2Int.one;

    private bool stopDeactivate = false;
    private bool deactivating   = false;

    #endregion

    #region Init ––––––––––––––––––––––––––––––––––––––––––––––––––

    void Awake()
    {
        i = this;
        // Grid arrays + overlay squares are built lazily, sized to the map (see EnsureGridFitsMap), the
        // first time build mode is entered — by then MapManager has built the (possibly scaled) boundary.
        buildingGrid.gameObject.SetActive(false);
    }

    /// <summary>
    /// Size the grid to cover the whole map. Builds it on first use and grows it (never shrinks, so placed
    /// buildings stay addressable) when the map has expanded. A no-op when the current grid already fits,
    /// so it's cheap to call on every build-mode entry. The map's bounding box drives the cell count
    /// instead of a fixed width/height that could cut off before the edge.
    /// </summary>
    void EnsureGridFitsMap()
    {
        Bounds b = MapManager.MapBounds();
        if (b.size.x <= 0f || b.size.y <= 0f)
        {
            if (overlay == null) RebuildGrid(origin, width, height); // no map yet — fall back to authored size
            return;
        }

        float margin = cellSize * 2f;
        float minX = b.min.x - margin, minY = b.min.y - margin;
        float maxX = b.max.x + margin, maxY = b.max.y + margin;

        // Union with the existing coverage so a grow never drops cells that already hold buildings.
        if (overlay != null)
        {
            minX = Mathf.Min(minX, origin.x);
            minY = Mathf.Min(minY, origin.y);
            maxX = Mathf.Max(maxX, origin.x + width * cellSize);
            maxY = Mathf.Max(maxY, origin.y + height * cellSize);
        }

        // Snap origin to the cell lattice so the origin shift between grows is a whole number of cells —
        // that keeps the occupied remap an exact integer index offset.
        Vector2 newOrigin = new Vector2(Mathf.Floor(minX / cellSize) * cellSize, Mathf.Floor(minY / cellSize) * cellSize);
        int newW = Mathf.CeilToInt((maxX - newOrigin.x) / cellSize);
        int newH = Mathf.CeilToInt((maxY - newOrigin.y) / cellSize);

        if (overlay != null && newOrigin == origin && newW == width && newH == height) return; // already fits

        RebuildGrid(newOrigin, newW, newH);
    }

    /// <summary>(Re)allocate the grid at a new origin/size, carrying placed-building occupancy across.</summary>
    void RebuildGrid(Vector2 newOrigin, int newW, int newH)
    {
        var newOccupied = new bool[newW, newH];
        if (occupied != null)
        {
            int ox = Mathf.RoundToInt((origin.x - newOrigin.x) / cellSize);
            int oy = Mathf.RoundToInt((origin.y - newOrigin.y) / cellSize);
            for (int x = 0; x < width; ++x)
                for (int y = 0; y < height; ++y)
                {
                    if (!occupied[x, y]) continue;
                    int nx = x + ox, ny = y + oy;
                    if (nx >= 0 && ny >= 0 && nx < newW && ny < newH) newOccupied[nx, ny] = true;
                }
        }

        origin = newOrigin; width = newW; height = newH;
        occupied      = newOccupied;
        inRange       = new bool[width, height];
        baseColour    = new Color[width, height];
        overlay       = new SpriteRenderer[width, height];
        energyOverlay = new SpriteRenderer[width, height];

        for (int c = buildingGrid.childCount - 1; c >= 0; --c) Destroy(buildingGrid.GetChild(c).gameObject);
        MakeOverlaySquares();
    }

    void MakeOverlaySquares()
    {
        var square = Instantiate(block);
        square.gameObject.SetActive(false);
        var energySquare = Instantiate(energyBlock);
        energySquare.gameObject.SetActive(false);

        for (int x = 0; x < width; ++x)
            for (int y = 0; y < height; ++y)
            {
                var inst = Instantiate(square, GridToWorld(new Vector2Int(x, y)), Quaternion.identity, buildingGrid);
                inst.transform.localScale = Vector3.one * cellSize * 0.99f;
                inst.gameObject.SetActive(true);
                overlay[x, y] = inst;
                var eInst = Instantiate(energySquare, GridToWorld(new Vector2Int(x, y)), Quaternion.identity, buildingGrid);
                eInst.transform.localScale = Vector3.one * cellSize * 0.99f;
                eInst.gameObject.SetActive(false);                               // hidden until there is energy
                //energyOverlay[x, y] = eInst;
            }

        Destroy(square);
        Destroy(energySquare);
    }

    #endregion

    #region Public API ––––––––––––––––––––––––––––––––––––––––––––

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var local = (Vector2)worldPos - origin;
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.y / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
        => new Vector3(
            origin.x + (gridPos.x + 0.5f) * cellSize,
            origin.y + (gridPos.y + 0.5f) * cellSize,
            0f);

    /// <summary>True if every cell in the given rectangle is both un‑occupied *and* inside constructor range.</summary>
    /// <summary>Build the grid on demand if something touches it before the first build-mode entry
    /// (e.g. a pre-placed building registering occupancy at game start).</summary>
    void EnsureBuilt() { if (overlay == null) EnsureGridFitsMap(); }

    public bool AreaClear(Vector2Int anchor, Vector2Int size)
    {
        EnsureBuilt();
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy) || occupied[gx, gy] || !inRange[gx, gy])
                    return false;
            }
        return true;
    }

    public void SetArea(Vector2Int anchor, Vector2Int size, bool state)
    {
        EnsureBuilt();
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy)) continue;

                occupied[gx, gy] = state;
                baseColour[gx, gy] = overlay[gx, gy].color = state || !inRange[gx, gy]
                    ? filledColour
                    : clearColour;
            }
        
        // Refresh energy cells when buildings are placed/removed
        RefreshEnergyCells();
    }

    /// <summary>
    /// Paints / updates the preview for the current frame. Only the cells that changed since the last call are touched → cheap.
    /// • Un‑buildable cells (occupied or out of range) go bright red.
    /// • All cells in a fully‑valid footprint go bright green.
    /// </summary>
    public void PreviewArea(Vector2Int anchor, Vector2Int size, bool valid)
    {
        // 1) Restore colours where the cursor was previously
        if (lastAnchor.x != int.MinValue)
        {
            for (int y = 0; y < lastSize.y; ++y)
                for (int x = 0; x < lastSize.x; ++x)
                {
                    int gx = lastAnchor.x + x;
                    int gy = lastAnchor.y + y;
                    if (Inside(gx, gy))
                    {
                        overlay[gx, gy].color = baseColour[gx, gy];
                        overlay[gx, gy].sortingLayerID = SortingLayer.NameToID("Default");
                    }
                }
        }

        // 2) Highlight current footprint
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy)) continue;

                bool blocked = occupied[gx, gy] || !inRange[gx, gy];

                if (valid)
                {
                    overlay[gx, gy].color = brightClearColour;           // whole footprint valid
                }
                else if (blocked)
                {
                    overlay[gx, gy].color = brightBlockedColour;         // only the offending tiles
                }
                overlay[gx, gy].sortingLayerID = SortingLayer.NameToID("Buildings");
            }

        lastAnchor = anchor;
        lastSize   = size;
    }

    /// <summary>Called by <see cref="BM"/> when entering build mode.</summary>
    public void ActivateGrid()
    {
        if (deactivating) stopDeactivate = true;
        EnsureGridFitsMap();    // size to the (possibly grown) map before painting
        RebuildRangeCache();    // expensive work done once on entry
        buildingGrid.gameObject.SetActive(true);
        RefreshEnergyCells();   // Also refresh energy cells when grid is activated
    }

    public void DeactivateGrid()
    {
        if (deactivating) return;
        deactivating = true;
        StartCoroutine(DoDeactivate());

        IEnumerator DoDeactivate()
        {
            yield return null;  // allow one frame for BM to finish up
            yield return null;

            if (stopDeactivate)
            {
                deactivating   = false;
                stopDeactivate = false;
                yield break;
            }

            BM.i.ChangeBuildingColour(true);
            buildingGrid.gameObject.SetActive(false);
            lastAnchor = new Vector2Int(int.MinValue, int.MinValue); // forget cached highlight
            deactivating = false;
        }
    }

    /// <summary>Called when pylons are added or removed to update energy display.</summary>
    public void OnPylonChanged()
    {
        if (buildingGrid.gameObject.activeSelf)
        {
            RefreshEnergyCells();
        }
    }

    #endregion

    #region Internals –––––––––––––––––––––––––––––––––––––––––––––––

    bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

    /// <summary>
    /// Re‑computes which tiles are inside any constructor's radius and caches the base grid colours.
    /// This runs *once* on entering build mode, so the grid can be repainted very cheaply each frame.
    /// </summary>
    public void RebuildRangeCache()
    {
        if (overlay == null) return; // grid not built yet (constructor placed before first build-mode entry)
        var constructors = EnergyManager.constructors.Concat(EnergyManager.toBeBuilt).ToList(); // assumed to exist per brief

        // Clip the buildable grid to the map's inner inset (pulled in by buildEdgeMargin). Computed once
        // here, then a cheap point-in-polygon per cell — far cheaper than testing the footprint each frame.
        Vector2[] buildable = MapManager.GetBuildableBoundary(buildEdgeMargin);

        for (int gx = 0; gx < width; ++gx)
            for (int gy = 0; gy < height; ++gy)
            {
                Vector3 cellWorld = GridToWorld(new Vector2Int(gx, gy));
                bool range = false;
                if (constructors.Count > 0 && (buildable == null || MapManager.PointInPoly(cellWorld, buildable)))
                {
                    foreach (var c in constructors)
                    {
                        if (c == null) continue;
                        float r = c.radius;
                        if ((c.transform.position - cellWorld).sqrMagnitude <= r * r)
                        {
                            range = true;
                            break;
                        }
                    }
                }

                inRange[gx, gy] = range;
                bool blocked = occupied[gx, gy];
                baseColour[gx, gy] = overlay[gx, gy].color = blocked ? filledColour : !range ? outColour : clearColour;
            }
    }

    #endregion
    
    /// <summary>Show or hide the energy overlay on a single cell.</summary>
    public void SetEnergy(int gx, int gy, bool hasEnergy)
    {
        // if (!Inside(gx, gy)) return;
        // if (energyOverlay[gx, gy] != null)
        //     energyOverlay[gx, gy].gameObject.SetActive(hasEnergy);
    }
    
    /// <summary>Re‑computes which cells are inside any pylon's reach and toggles the energy overlay colours.</summary>
    public void RefreshEnergyCells()
    {
        if (overlay == null) return; // grid not built yet; ActivateGrid will refresh once it is
        for (int gx = 0; gx < width; ++gx)
            for (int gy = 0; gy < height; ++gy)
            {
                bool freeAccess = inRange[gx, gy] && !occupied[gx, gy];
                
                SetEnergy(gx, gy, true);

                // Decide the color
                Color targetColour;
                if (freeAccess)
                {
                    targetColour = energyFreeColour; // yellow - energy & buildable
                }
                else if (occupied[gx, gy])
                {
                    targetColour = filledColour; // red - occupied
                }
                else if (!inRange[gx, gy])
                {
                    targetColour = outColour; // black - out of range
                }
                else
                {
                    targetColour = clearColour; // green - in range but no energy
                }

                // Apply color to both blocks
                overlay[gx, gy].color = targetColour;
                if (energyOverlay[gx, gy] != null)
                {
                    energyOverlay[gx, gy].color = targetColour;
                }
                
                // Update base color cache
                baseColour[gx, gy] = targetColour;
            }
    }
}