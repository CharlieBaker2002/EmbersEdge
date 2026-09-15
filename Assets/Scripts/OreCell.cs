using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Cell — the base's ore producer (the orb-era "Cell"/OrbManifester reborn for the chip
/// economy). It manifests ORE CHIPS instead of orbs, ONLY at the new-day event:
///   • it chips every base ore tile in the 3×3 around it (one chip per unit released, in that
///     tile's blend), exactly as the old Cell harvested its neighbours, and
///   • adds a flat <see cref="bonusChips"/> of its own — but only while there IS base ore in
///     that 3×3. No ore in range, no chip at all (user rule 2026-09-14).
/// The day's chips don't burst out at once: they trickle out evenly over
/// <see cref="productionSeconds"/> (user call 2026-09-14), each from where it came from (a tile's
/// chips beside the tile, the bonus beside the Cell). Chips leave as ordinary base-side scrap:
/// the bag fleet hauls them to whoever is hungry (construction first), or the player Hoovers
/// them up. The bonus is skipped while <see cref="maxLooseNearby"/> unclaimed chips already lie
/// in its ring — a Cell nobody is collecting from doesn't carpet the base. The CellTrigger
/// animation fires on each batch and its end-event <see cref="Spawn"/> starts the trickle.
/// </summary>
public class OreCell : Building
{
    [Header("Ore Cell")]
    [Tooltip("Flat chips the Cell adds of its own at each new day — only while any base ore lies in the 3×3 around it.")]
    public int bonusChips = 5;
    [Tooltip("At each new day chip every base ore tile in the 3×3 around the Cell (one chip per unit released, in the tile's blend).")]
    public bool chipAdjacentOre = true;
    [Tooltip("The bonus is skipped while this many loose, unclaimed chips already lie within looseRadius.")]
    public int maxLooseNearby = 8;
    public float looseRadius = 1.6f;
    /// <summary>The hover ring shows the loose-chip ring production pauses on (HoverRing, 2026-09-14).</summary>
    public override float HoverRingRadius => looseRadius;
    [Tooltip("Seconds the day's chips take to come out — spread evenly, not all at once.")]
    public float productionSeconds = 15f;
    [Tooltip("Intensity of the bonus chips the Cell manifests itself (0 low / 1 mid / 2 high — sets the chip size blend).")]
    [Range(0, 2)] public int intensity = 1;

    /// <summary>One chip still to come out: from an ore tile (its tier's blend) or the Cell's bonus.</summary>
    struct Pending { public Vector3 pos; public int tier; public bool fromTile; }

    Animator anim;
    readonly List<Pending> pending = new List<Pending>();
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
        int before = pending.Count;
        bool oreInRange = chipAdjacentOre && HarvestNeighbours();
        if (oreInRange && bonusChips > 0 && LooseNearby() < maxLooseNearby)
            for (int k = 0; k < bonusChips; k++)
                pending.Add(new Pending { pos = transform.position, tier = intensity, fromTile = false });
        if (pending.Count == before) yield break;   // no ore in range: nothing today
        if (anim != null && anim.isActiveAndEnabled) anim.SetTrigger(TriggerId);   // CellTrigger → Spawn() at its end
        else Spawn();
    }

    /// <summary>The old Cell's harvest: every base ore tile in the 3×3 around us gives up its
    /// units now, queued to come out beside that tile over the trickle. True when any live ore
    /// tile lies in range (whether or not this bite released a whole unit) — the bonus's gate.</summary>
    bool HarvestNeighbours()
    {
        var maps = TilemapResource.Maps;
        if (maps == null) return false;
        bool any = false;
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
                    any = true;
                    int k = o.Chip();
                    for (int u = 0; u < k; u++)
                        pending.Add(new Pending { pos = o.transform.position, tier = o.tier, fromTile = true });
                }
        }
        return any;
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
        if (spawnCo == null && pending.Count > 0) spawnCo = StartCoroutine(SpawnCo());
    }

    /// <summary>The trickle: the queue comes out evenly across productionSeconds (a chip queued
    /// while it runs just follows at the same pace). If the Cell dies mid-trickle, ore already
    /// taken from the tiles still comes out at once — never lost — while the bonus is dropped.</summary>
    IEnumerator SpawnCo()
    {
        float interval = Mathf.Max(0f, productionSeconds) / Mathf.Max(1, pending.Count);
        while (pending.Count > 0)
        {
            if (!builtYet) { FlushTileChips(); break; }
            var job = pending[0];
            pending.RemoveAt(0);
            Emit(job);
            if (pending.Count > 0 && interval > 0f) yield return new WaitForSeconds(interval);
        }
        pending.Clear();
        spawnCo = null;
    }

    void Emit(Pending job)
    {
        if (job.fromTile) DroneManager.SpawnOreUnits(job.pos, job.tier, 1);   // the tile's own blend
        else DroneManager.SpawnScrap(job.pos + GS.RandCircle(0.12f, 0.3f), DroneManager.RollBaseChipSize(job.tier), 0, transform.position);   // base cap applies
        var fx = MineField.ChipFxPrefab();
        if (fx != null) Instantiate(fx, job.pos, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
    }

    void FlushTileChips()
    {
        for (int k = 0; k < pending.Count; k++)
            if (pending[k].fromTile) DroneManager.SpawnOreUnits(pending[k].pos, pending[k].tier, 1);
        pending.Clear();
    }

    protected override void BDisable()
    {
        base.BDisable();
        if (pending.Count > 0) FlushTileChips();   // a dying / disabled Cell doesn't eat harvested ore
    }
}
