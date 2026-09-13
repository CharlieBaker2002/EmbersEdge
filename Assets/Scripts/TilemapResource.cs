using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The base's ore field — the scene object <c>ResourceMaps/Ore_Base</c> with THREE tilemap children,
/// <c>Low</c> / <c>Mid</c> / <c>High</c>, one per ore INTENSITY tier. It scatters ore batches in a
/// band outside the base map at Start (procedural per run) using the OreTile rule tile, whose
/// instantiated <see cref="Ore"/> objects are the mineable nodes (drill drones deconstruct marked
/// tiles; the Cell harvests its 3×3). Intensity rises outward: inner batches land on Low, outer
/// on High, with some mixing — and a node's tier sets the chip blend its units release
/// (DroneManager.oreTiers). The base shows intensity by MATERIAL for now (the tiled art has no
/// vein variants yet): each map's authored material is the era-0 ore glow of its tier
/// (Purple / Purple 1 / SpecialPurple); <see cref="DroneManager.OreSourceMaterial"/> keeps them
/// on the current era.
/// </summary>
public class TilemapResource : MonoBehaviour
{
    public static TilemapResource i;
    /// <summary>The tier maps (index = intensity 0 low / 1 mid / 2 high); null until Ore_Base awakes.</summary>
    public static Tilemap[] Maps => i != null ? i.tierMaps : null;
    public static Tilemap MapOf(int tier)
    {
        var m = Maps;
        if (m == null || m.Length == 0) return null;
        return m[Mathf.Clamp(tier, 0, m.Length - 1)];
    }

    [Tooltip("One tilemap per intensity tier: Low, Mid, High (children of this object).")]
    [SerializeField] Tilemap[] tierMaps = new Tilemap[3];
    [SerializeField] RuleTile t;
    [Tooltip("The OreTile's sprite variants — index picks the node's ore units.")]
    [SerializeField] Sprite[] allSprites;
    float valueCoef;
    [Tooltip("Ore band: batches land between these distances from the base centre (scaled by the map).")]
    [SerializeField] Vector2 distances;
    [Tooltip("Tiles per batch: x at the inner edge of the band → y at the outer edge.")]
    [SerializeField] Vector2 batchSizes;
    [Tooltip("Batches scattered per run (the whole field).")]
    [SerializeField] int batchN;
    [SerializeField] float diagonality = 0.8f;
    [Tooltip("How much a batch's intensity may wander from its distance (0 = strictly inner low → outer high).")]
    [Range(0f, 1f)] [SerializeField] float intensityJitter = 0.45f;

    System.Action<int> eraAction;

    private void Awake()
    {
        i = this;
        int[] buffer = new int[4];
        buffer[0] = 1;
        valueCoef = 1f / GS.CostValue(buffer);
    }

    void OnDestroy()
    {
        if (i == this) i = null;
        if (eraAction != null) GS.OnNewEra -= eraAction;
    }

    /// <summary>Follow the era: the authored materials are era 0's; later eras swap in theirs.</summary>
    void ApplyEraMaterials(int era)
    {
        if (tierMaps == null) return;
        for (int tier = 0; tier < tierMaps.Length; tier++)
        {
            var map = tierMaps[tier];
            if (map == null) continue;
            var mat = DroneManager.OreSourceMaterial(tier);
            var r = map.GetComponent<TilemapRenderer>();
            if (mat != null && r != null && r.sharedMaterial != mat) r.material = mat;
        }
    }

    bool AnyMapHasTile(Vector3Int pos)
    {
        for (int tier = 0; tier < tierMaps.Length; tier++)
            if (tierMaps[tier] != null && tierMaps[tier].HasTile(pos)) return true;
        return false;
    }

    private IEnumerator Start()
    {
        ApplyEraMaterials(GS.era);
        eraAction = ApplyEraMaterials;
        GS.OnNewEra += eraAction;
        if (tierMaps == null || tierMaps.Length == 0 || tierMaps[0] == null)
        {
            Debug.LogError("[TilemapResource] Ore_Base has no tier maps assigned (Low/Mid/High children).");
            yield break;
        }

        Sprite s;
        Vector3Int pos;
        float dec;
        float ang;
        Vector3Int r;
        Vector2 r2;
        // Push the whole resource band out with the map so ore only starts at the (scaled) base map edge.
        Vector2 dist = distances * MapManager.Scale;
        for (int n = batchN; n > 0; n--)
        {
            dec = 1f - (float)n / (float)batchN;
            // intensity by distance (inner → low, outer → high), jittered so the bands blend
            int tier = Mathf.Clamp(Mathf.RoundToInt(dec * 2f + Random.Range(-intensityJitter, intensityJitter)), 0, tierMaps.Length - 1);
            var map = tierMaps[tier] != null ? tierMaps[tier] : tierMaps[0];
            ang = Random.Range(80f * dec, 360f - 80f * dec) * Mathf.Deg2Rad;
            pos = new Vector3Int(Mathf.RoundToInt(Mathf.Lerp(dist.x,dist.y,dec) * Mathf.Sin(ang)), Mathf.RoundToInt(Mathf.Lerp(dist.x, dist.y, dec) * Mathf.Cos(ang)));
            for(int k = 0; k < Mathf.RoundToInt(Mathf.Lerp(batchSizes.x,batchSizes.y,dec)); k++)
            {
                r2 = Random.insideUnitCircle.normalized * diagonality;
                r = new Vector3Int(Mathf.RoundToInt(r2.x), Mathf.RoundToInt(r2.y));
                if((pos + r).sqrMagnitude < Mathf.Pow(Mathf.Lerp(dist.x, dist.y, dec), 2))
                {
                    k--;
                    continue;
                }
                if (AnyMapHasTile(pos + r))
                {
                    if (Random.Range(0, 4) != 0)
                    {
                        k--;
                    }
                    continue;
                }
                pos += r;
                map.SetTile(pos, t);
                s = map.GetSprite(pos);
                SetupOre(map, s, pos, tier);
            }
            yield return null;
        }
    }

    private void SetupOre(Tilemap map, Sprite s, Vector3Int pos, int tier)
    {
        float units;
        switch(System.Array.IndexOf(allSprites,s))
        {
            case 0: units = 300; break;
            case 1: units = 240; break;
            case 2: units = 200; break;
            case 3: units = 220; break;
            case 4: units = 130; break;
            case 5: units = 210; break;
            case 6: units = 200; break;
            case 7: units = 250; break;
            case 8: units = 260; break;
            case 9: units = 250; break;
            case 10: units = 200; break;
            case 11: units = 130; break;
            default: return;
        }
        var g = map.GetInstantiatedObject(pos);
        if (g == null) return;
        g.GetComponent<Ore>().Setup(tier, units * valueCoef * 0.5f, 10 / valueCoef);
    }
}
