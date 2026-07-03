using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tiles the base ground with the SAME art the dungeon uses — Resources/GroundTiles, at the dungeon's 0.5u
/// cell scale, a random variant per cell. It paints a plain square (centred on world origin, the map centre)
/// covering the whole map; the map's SpriteMask clips it to the actual map shape, so the square just has to
/// be big enough — "approximate as a square and let the masking do the rest".
///
/// Self-contained: it builds its own Grid + Tilemap child at world origin, so it doesn't matter which
/// GameObject you attach it to (put it on the old Ground object, or any empty). Renders unlit like the
/// dungeon floor, on the same sorting layer the old floor used, masked to the map.
/// </summary>
[DisallowMultipleComponent]
public class BaseGroundTiler : MonoBehaviour
{
    [Tooltip("Must match the dungeon cell (0.5) so the ground art is the same scale in both.")]
    public float cellSize = 0.5f;
    [Tooltip("Sorting layer for the ground (the old base floor used 'Buildings').")]
    public string sortingLayer = "Buildings";
    [Tooltip("Sorting order — keep well below buildings so the ground is behind everything.")]
    public int sortingOrder = -100;
    [Tooltip("Fallback square side (world units) if MapManager isn't available yet.")]
    public float fallbackSpan = 90f;

    Grid grid;
    Tilemap map;
    Tile[] floorPool;
    float paintedSpan = -1f;

    void Start()
    {
        Rebuild();
        MapManager.OnUpdateMap += Rebuild;   // repaint if the map (and so the span) grows
    }

    void OnDestroy() => MapManager.OnUpdateMap -= Rebuild;

    void EnsureSetup()
    {
        if (grid != null) return;

        var gridGo = new GameObject("BaseGroundGrid");
        gridGo.transform.SetParent(transform, false);
        gridGo.transform.position = Vector3.zero;                 // the map is centred on world origin
        grid = gridGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(cellSize, cellSize, 0f);

        var tmGo = new GameObject("GroundTilemap");
        tmGo.transform.SetParent(gridGo.transform, false);
        map = tmGo.AddComponent<Tilemap>();
        var r = tmGo.AddComponent<TilemapRenderer>();
        r.sortingLayerName = sortingLayer;
        r.sortingOrder = sortingOrder;
        r.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;   // clipped to the map shape by the map's SpriteMask

        // Unlit, exactly like the dungeon floor, so the ground always reads at full brightness.
        var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh);

        BuildFloorPool();
    }

    // Same construction as MineField's floor: the base GroundTiles variants, scaled to the dungeon cell.
    void BuildFloorPool()
    {
        var sprites = Resources.LoadAll<Sprite>("GroundTiles");
        if (sprites == null || sprites.Length == 0) { floorPool = null; return; }
        floorPool = new Tile[sprites.Length];
        for (int s = 0; s < sprites.Length; s++)
        {
            var sp = sprites[s];
            float natural = sp.pixelsPerUnit > 0f ? sp.rect.width / sp.pixelsPerUnit : cellSize;  // base tile world size (~1u)
            float scale = natural > 0.0001f ? cellSize / natural : 1f;                            // shrink to the dungeon cell
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sp;
            t.color = Color.white;
            t.colliderType = Tile.ColliderType.None;
            t.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            floorPool[s] = t;
        }
    }

    public void Rebuild()
    {
        EnsureSetup();
        if (floorPool == null || map == null) return;

        float span = (Application.isPlaying && MapManager.i != null ? MapManager.MaskSpan : fallbackSpan) * 1.1f;
        if (span <= 0f) span = fallbackSpan;
        if (span <= paintedSpan) return;         // already covered — the mask handles the shape, no repaint needed
        paintedSpan = span;

        int half = Mathf.CeilToInt(span * 0.5f / cellSize);
        var bounds = new BoundsInt(-half, -half, 0, half * 2, half * 2, 1);
        var tiles = new TileBase[bounds.size.x * bounds.size.y];
        for (int idx = 0; idx < tiles.Length; idx++)
            tiles[idx] = floorPool[floorPool.Length == 1 ? 0 : Random.Range(0, floorPool.Length)];
        map.SetTilesBlock(bounds, tiles);
    }
}
