using System.Collections;
using System.Collections.Generic;
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

    private Color clearColour     = new Color(0f, 0.4f, 0f, 1f); // green – inside constructor range
    private Color filledColour    = new Color(0.4f, 0f, 0f, 1f); // red – occupied
    private Color outColour    = new Color(0.05f, 0.05f, 0.05f, 1f); // black – out of range
    private Color brightClearColour   = new Color(0f, 0.8f, 0f, 1f); // super‑bright green
    private Color brightBlockedColour = new Color(0.8f, 0f, 0f, 1f);  // super‑bright red

    [SerializeField] Transform buildingGrid;
    [SerializeField] GameObject block;

    #endregion

    #region Runtime state –––––––––––––––––––––––––––––––––––––––––

    bool[,] occupied;              // placed buildings
    bool[,] inRange;               // within range of a constructor this frame
    Color[,] baseColour;           // cache of the colour each tile should have when *not* highlighted
    SpriteRenderer[,] overlay;     // sprite for each cell

    Vector2Int lastAnchor = new(int.MinValue, int.MinValue);
    Vector2Int lastSize   = Vector2Int.one;

    private bool stopDeactivate = false;
    private bool deactivating   = false;

    #endregion

    #region Init ––––––––––––––––––––––––––––––––––––––––––––––––––

    void Awake()
    {
        i = this;

        occupied   = new bool[width, height];
        inRange    = new bool[width, height];
        baseColour = new Color[width, height];
        overlay    = new SpriteRenderer[width, height];

        MakeOverlaySquares();
        buildingGrid.gameObject.SetActive(false);
    }

    void MakeOverlaySquares()
    {
        var square = Instantiate(block);
        square.SetActive(false);

        for (int x = 0; x < width; ++x)
            for (int y = 0; y < height; ++y)
            {
                var inst = Instantiate(square, GridToWorld(new Vector2Int(x, y)), Quaternion.identity, buildingGrid);
                inst.transform.localScale = Vector3.one * cellSize * 0.99f;
                inst.SetActive(true);
                overlay[x, y] = inst.GetComponent<SpriteRenderer>();
            }

        Destroy(square);
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
    public bool AreaClear(Vector2Int anchor, Vector2Int size)
    {
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
        RebuildRangeCache();    // expensive work done once on entry
        buildingGrid.gameObject.SetActive(true);
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

    #endregion

    #region Internals –––––––––––––––––––––––––––––––––––––––––––––––

    bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

    /// <summary>
    /// Re‑computes which tiles are inside any constructor’s radius and caches the base grid colours.
    /// This runs *once* on entering build mode, so the grid can be repainted very cheaply each frame.
    /// </summary>
    void RebuildRangeCache()
    {
        var constructors = EnergyManager.i?.constructors; // assumed to exist per brief

        for (int gx = 0; gx < width; ++gx)
            for (int gy = 0; gy < height; ++gy)
            {
                bool range = false;
                if (constructors != null && constructors.Count > 0)
                {
                    Vector3 cellWorld = GridToWorld(new Vector2Int(gx, gy));
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
}
