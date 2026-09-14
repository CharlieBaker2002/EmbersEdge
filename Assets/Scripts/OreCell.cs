using System.Collections;
using UnityEngine;

/// <summary>
/// The Cell — the base's ore producer (the orb-era "Cell"/OrbManifester reborn for the chip
/// economy). It manifests ORE CHIPS instead of orbs:
///   • a steady trickle — one chip every <see cref="secondsPerOre"/> seconds (0 = off);
///   • a daily batch of <see cref="orePerDay"/> at each new day, plus — set it on an ore seam —
///     it chips every base ore tile in the 3×3 around it (one chip per unit released, in that
///     tile's element), exactly as the old Cell harvested its neighbours.
/// Chips burst out of the Cell as ordinary base-side scrap: the bag fleet hauls them to
/// whoever is hungry (construction first), or the player Hoovers them up. Production pauses
/// while <see cref="maxLooseNearby"/> unclaimed chips already lie in its ring — a Cell nobody
/// is collecting from doesn't carpet the base. The CellTrigger animation fires on each batch
/// and its end-event <see cref="Spawn"/> releases the chips.
/// </summary>
public class OreCell : Building
{
    [Header("Ore Cell")]
    [Tooltip("Seconds per ore chip while built (0 = daily batch only).")]
    public float secondsPerOre = 30f;
    [Tooltip("Extra ore chips manifested at each new day.")]
    public int orePerDay = 2;
    [Tooltip("At each new day also chip every base ore tile in the 3×3 around the Cell (one chip per unit released, in the tile's element).")]
    public bool chipAdjacentOre = true;
    [Tooltip("Production pauses while this many loose, unclaimed chips already lie within looseRadius.")]
    public int maxLooseNearby = 8;
    public float looseRadius = 1.6f;
    /// <summary>The hover ring shows the loose-chip ring production pauses on (HoverRing, 2026-09-14).</summary>
    public override float HoverRingRadius => looseRadius;
    [Tooltip("Seconds between chips of one batch leaving the Cell.")]
    public float burstInterval = 0.12f;
    [Tooltip("Intensity of the ore the Cell manifests itself (0 low / 1 mid / 2 high — sets the chip size blend).")]
    [Range(0, 2)] public int intensity = 1;

    Animator anim;
    float t;
    int pendingSpawn;
    System.Action newDay;
    Coroutine spawnCo;
    static readonly int TriggerId = Animator.StringToHash("Trigger");

    public override void Start()
    {
        base.Start();
        anim = GetComponent<Animator>();
        newDay = OnNewDay;
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay += newDay;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (SpawnManager.instance != null && newDay != null) SpawnManager.instance.OnNewDay -= newDay;
    }

    void Update()
    {
        if (!builtYet || secondsPerOre <= 0f) return;
        t += Time.deltaTime;
        if (t < secondsPerOre) return;
        t = 0f;
        Produce(1);
    }

    void OnNewDay()
    {
        if (!builtYet) return;
        StartCoroutine(NewDayBatch());
    }

    /// <summary>The day's batch lands strictly AFTER the wave-clear wipe (user rule 2026-09-14):
    /// the wipe fires the instant the wave clears and the new day 1.25 s later, so this normally
    /// waits for nothing — but a batch spawned inside the fade would be wiped with the old chip,
    /// so it holds until the wipe has settled however the timings shift.</summary>
    IEnumerator NewDayBatch()
    {
        while (Time.time < ChipClearCycle.WaveClearSettledAt) yield return null;
        if (!builtYet) yield break;
        int n = orePerDay;
        if (chipAdjacentOre) n += ChipNeighbours();
        Produce(n);
    }

    /// <summary>The old Cell's harvest: every base ore tile in the 3×3 around us gives up a
    /// unit, released as a chip beside it. Returns how many extra chips the Cell itself adds
    /// (none — neighbour chips spawn at their tiles).</summary>
    int ChipNeighbours()
    {
        var maps = TilemapResource.Maps;
        if (maps == null) return 0;
        foreach (var map in maps)
        {
            if (map == null) continue;
            Vector3Int c = map.WorldToCell(transform.position);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    var g = map.GetInstantiatedObject(c + new Vector3Int(x, y, 0));
                    if (g == null) continue;
                    var o = g.GetComponent<Ore>();
                    if (o == null || o.Depleted) continue;
                    int k = o.Chip();
                    if (k > 0) DroneManager.SpawnOreUnits(o.transform.position, o.tier, k);   // the tile's own blend
                }
        }
        return 0;
    }

    void Produce(int n)
    {
        if (n <= 0 || !builtYet) return;
        if (LooseNearby() >= maxLooseNearby) return;   // nobody's collecting — hold
        pendingSpawn += n;
        if (anim != null && anim.isActiveAndEnabled) anim.SetTrigger(TriggerId);   // CellTrigger → Spawn() at its end
        else Spawn();
    }

    int LooseNearby()
    {
        int n = 0;
        Vector2 p = transform.position;
        float r2 = looseRadius * looseRadius;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.claimedBy != null) continue;
            if (((Vector2)chip.transform.position - p).sqrMagnitude <= r2) n++;
        }
        return n;
    }

    /// <summary>Animation event (CellTrigger, t = 1) — and the fallback when there's no animator.</summary>
    public void Spawn()
    {
        if (anim != null) anim.ResetTrigger(TriggerId);
        if (spawnCo == null && pendingSpawn > 0) spawnCo = StartCoroutine(SpawnCo());
    }

    IEnumerator SpawnCo()
    {
        while (pendingSpawn > 0 && builtYet)
        {
            pendingSpawn--;
            Vector3 p = transform.position + GS.RandCircle(0.12f, 0.3f);
            var chip = DroneManager.SpawnScrap(p, DroneManager.RollBaseChipSize(intensity), 0, transform.position);   // base cap applies
            var fx = MineField.ChipFxPrefab();
            if (fx != null) Instantiate(fx, transform.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
            yield return new WaitForSeconds(burstInterval);
        }
        pendingSpawn = 0;
        spawnCo = null;
    }
}
