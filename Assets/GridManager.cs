using System.Collections;
using UnityEngine;

/// <summary>Keeps track of the buildable grid and its visual overlay.</summary>
public class GridManager : MonoBehaviour
{
    public static GridManager i { get; private set; }

    [Header("Grid geometry")]
    public int width  = 64;
    public int height = 64;
    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;

    [Header("Visuals")]
    public Sprite squareSprite;          // a 1×1 white sprite
    public Color gridLineColour  = new Color(1,1,1,0.08f); // faint white
    public Color clearColour     = new Color(0,1,0,0.20f); // green
    public Color filledColour    = new Color(0,0,0,0.35f); // black

    [Header("Preview highlight colours")]
    public Color brightClearColour   = new Color(0f, 1f, 0f, 0.60f); // super‑bright green
    public Color brightBlockedColour = new Color(1f, 0f, 0f, 0.60f); // super‑bright red
    [SerializeField] Transform buildingGrid;
    
    bool[,] occupied;
    SpriteRenderer[,] overlay;           // one sprite per cell
    private bool stopDeactivate = false;
    private bool deactivating = false;

    void Awake()
    {
        if (i != null) { Destroy(gameObject); return; }
        i = this;
        DontDestroyOnLoad(gameObject);

        occupied = new bool[width, height];
        overlay  = new SpriteRenderer[width, height];

        //MakeGridLines();
        MakeOverlaySquares();
        buildingGrid.gameObject.SetActive(false);
    }

    #region Public API ––––––––––––––––––––––––––––––––––––––––––––––
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

    public bool AreaClear(Vector2Int anchor, Vector2Int size)
    {
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy) || occupied[gx, gy]) return false;
            }
        return true;
    }

    public void SetArea(Vector2Int anchor, Vector2Int size, bool state)
    {
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (Inside(gx, gy))
                {
                    occupied[gx, gy] = state;
                    overlay[gx, gy].color = state ? filledColour : clearColour;
                }
            }
    }

    /// <summary>
    /// Paints the preview overlay every frame.
    /// • Occupied cells   → bright red
    /// • Empty cells      → faint green
    /// • Footprint cells:
    ///     – if buildable → super‑bright green
    ///     – if blocked   → super‑bright red (only the cells causing the conflict)
    /// </summary>
    public void PreviewArea(Vector2Int anchor, Vector2Int size, bool valid)
    {
        // 1) Reset whole grid
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                overlay[x, y].color = occupied[x, y]
                    ? filledColour        // not buildable anywhere there’s something already
                    : clearColour;               // default faint green

        // 2) Highlight the footprint
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;

                // Outside grid? We can’t paint anything there.
                if (!Inside(gx, gy)) continue;

                bool blocked = occupied[gx, gy];

                if (valid)
                {
                    // Whole footprint is valid – show super‑bright green
                    overlay[gx, gy].color = brightClearColour;
                }
                else
                {
                    // Mixed footprint – only blocked cells go super‑bright red,
                    // the rest fall back to the faint green applied above.
                    if (blocked)
                        overlay[gx, gy].color = brightBlockedColour;
                }
            }
    }

    public void ActivateGrid()
    {
        if(deactivating) stopDeactivate = true;
        buildingGrid.gameObject.SetActive(true);
    }

    public void DeactivateGrid()
    {
        if (deactivating) return;
        deactivating = true;
        StartCoroutine(DoDeactivate());
        IEnumerator DoDeactivate()
        {
            yield return null;
            yield return null;
            if (stopDeactivate)
            {
                deactivating = false;
                stopDeactivate = false;
                yield break;
            }
            BM.i.ChangeBuildingColour(true);
            buildingGrid.gameObject.SetActive(false);
            deactivating = false;
        }
    }
    #endregion

    #region Internals –––––––––––––––––––––––––––––––––––––––––––––––
    bool Inside(int x, int y) =>
        x >= 0 && y >= 0 && x < width && y < height;

    void MakeGridLines()
    {
        var go = new GameObject("GridLines");
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.widthMultiplier = 0.02f;
        lr.positionCount  = (width + height + 2) * 2;
        lr.startColor = lr.endColor = gridLineColour;

        int p = 0;
        for (int x = 0; x <= width; x+=2)
        {
            lr.SetPosition(p++, new Vector3(origin.x + x * cellSize, origin.y));
            lr.SetPosition(p++, new Vector3(origin.x + x * cellSize, origin.y + height * cellSize));
        }
        for (int y = 0; y <= height; y+=2)
        {
            lr.SetPosition(p++, new Vector3(origin.x               , origin.y + y * cellSize));
            lr.SetPosition(p++, new Vector3(origin.x + width*cellSize, origin.y + y * cellSize));
        }
    }

    void MakeOverlaySquares()
    {
        var square = new GameObject("cell");
        var sr     = square.AddComponent<SpriteRenderer>();
        sr.sprite  = squareSprite;
        sr.sortingOrder = 100; // above everything else
        square.SetActive(false);

        for (int x = 0; x < width; ++x)
            for (int y = 0; y < height; ++y)
            {
                var inst = Instantiate(square, GridToWorld(new Vector2Int(x, y)), Quaternion.identity, buildingGrid);
                inst.transform.localScale = Vector3.one * cellSize * 0.99f;
                inst.SetActive(true);
                overlay[x, y] = inst.GetComponent<SpriteRenderer>();
                overlay[x, y].color = clearColour;
            }
    }
    #endregion
}