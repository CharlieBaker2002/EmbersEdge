using System.Collections;
using UnityEngine;

/// <summary>
/// Central tunables + orchestration for the drone workforce. One per scene (the drone kit
/// builder wires it into World.unity; Ensure() covers scenes without one). Everything drones
/// do is paced by <see cref="Haste"/> so the feel can be tuned from a single dial.
/// </summary>
public class DroneManager : MonoBehaviour
{
    public static DroneManager i;

    [Header("Feel")]
    [Tooltip("Master speed multiplier for everything drones do: move force, drill rate, repair rate. Raise to make the workforce snappier.")]
    public float haste = 1.5f;
    [Tooltip("Movement force per physics tick — code-set onto every drone at Start (the prefab-serialized value is stale).")]
    public float droneMoveForce = 5f;
    [Tooltip("Cruise speed cap (ActionScript maxVelocity) — code-set onto every drone at Start.")]
    public float droneMaxVelocity = 4.5f;

    [Header("Threat response")]
    [Tooltip("An enemy within this range makes a drone flee.")]
    public float threatRadius = 4f;
    [Tooltip("A fleeing drone relaxes once no enemy is within this range (hysteresis; keep > threatRadius).")]
    public float threatClearRadius = 6f;
    [Tooltip("How far a rally-fighting drill drone may stray from its rally point.")]
    public float rallyLeash = 7f;
    [Tooltip("Rally-fight drill contact damage per second, per enemy in reach (scaled by haste).")]
    public float drillCombatDps = 0.7f;

    [Header("Loot")]
    [Tooltip("Where returning bag drones dump their haul at base.")]
    public Vector2 scrapPoint = new Vector2(0f, -6f);

    [Header("Energy tariffs")]
    [Tooltip("Energy per 1 hp healed on buildings/vehicles (0.025 => 40 hp per full charge).")]
    public float repairCostPerHp = 0.025f;
    public float drillCostRegular = 0.05f;
    public float drillCostHard = 0.15f;
    public float drillCostVeryHard = 0.3f;

    [Header("Ore chips")]
    [Tooltip("Debris pieces per broken wall (random in range).")]
    public int chipsMinPerBreak = 1;
    public int chipsMaxPerBreak = 3;
    [Tooltip("Hard cap on live chips; oldest are culled first.")]
    public int maxChips = 300;

    public static float Haste => i != null ? i.haste : 1.5f;
    public static float DroneMoveForce => i != null ? i.droneMoveForce : 5f;
    public static float DroneMaxVelocity => i != null ? i.droneMaxVelocity : 4.5f;
    public static float DrillCombatDps => i != null ? i.drillCombatDps : 0.7f;
    public static float ThreatRadius => i != null ? i.threatRadius : 4f;
    public static float ThreatClearRadius => i != null ? i.threatClearRadius : 6f;
    public static float RallyLeash => i != null ? i.rallyLeash : 7f;
    public static Vector2 ScrapPoint => i != null ? i.scrapPoint : new Vector2(0f, -6f);
    public static float RepairCostPerHp => i != null ? i.repairCostPerHp : 0.025f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => i = null;

    public static DroneManager Ensure()
    {
        if (i == null) i = new GameObject("DroneManager").AddComponent<DroneManager>();
        return i;
    }

    void Awake()
    {
        if (i == null) i = this;
    }

    System.Action<bool> teleport;
    System.Action<int> newEra;
    System.Action waveComplete;
    SpawnManager.DayState lastDayState = SpawnManager.DayState.Day;

    void Start()
    {
        StartCoroutine(SubscribeWhenReady());
        newEra = _ => TearDownDungeonPads();
        GS.OnNewEra += newEra;
    }

    IEnumerator SubscribeWhenReady()
    {
        while (PortalScript.i == null || SpawnManager.instance == null) yield return null;
        teleport = OnTeleport;
        PortalScript.i.onTeleport += teleport;
        waveComplete = OnWaveComplete;
        SpawnManager.instance.onWaveComplete += waveComplete;
    }

    void OnDestroy()
    {
        if (PortalScript.i != null && teleport != null) PortalScript.i.onTeleport -= teleport;
        if (SpawnManager.instance != null && waveComplete != null) SpawnManager.instance.onWaveComplete -= waveComplete;
        if (newEra != null) GS.OnNewEra -= newEra;
        if (i == this) i = null;
    }

    void Update()
    {
        // No PreAttack event exists — watch the state machine: wave incoming ⇒ pilots board.
        var sm = SpawnManager.instance;
        if (sm == null) return;
        if (sm.dayState != lastDayState)
        {
            if (sm.dayState == SpawnManager.DayState.PreAttack)
                for (int k = PilotedVehicle.all.Count - 1; k >= 0; k--)
                    PilotedVehicle.all[k]?.WaveStarting();
            lastDayState = sm.dayState;
        }
    }

    /// <summary>Wave cleared: vehicles power down, pilots pop out to heal their hulls and recharge.</summary>
    void OnWaveComplete()
    {
        for (int k = PilotedVehicle.all.Count - 1; k >= 0; k--)
            PilotedVehicle.all[k]?.WaveEnded();
    }

    // ------------------------------------------------------------------ deploy / recall

    /// <summary>Drones ride the player's teleport: dive deploys every assigned charged drone
    /// through its pad link; returning home force-recalls EVERY drone still in the dungeon
    /// (mid-flight ones included — the frozen dimension must never strand a drone) and clears
    /// the chip debris ("chips last until you return to base").</summary>
    void OnTeleport(bool nowInDungeon)
    {
        if (nowInDungeon)
        {
            DeployAssigned();
        }
        else
        {
            RecallAllFromDungeon();
            DespawnAllChips();
        }
    }

    // ------------------------------------------------------------------ ore chips

    public static float DrillCost(CellType tier)
    {
        var m = i;
        if (tier == CellType.VeryHard) return m != null ? m.drillCostVeryHard : 0.3f;
        if (tier == CellType.Hard) return m != null ? m.drillCostHard : 0.15f;
        return m != null ? m.drillCostRegular : 0.05f;
    }

    Sprite[] chipSprites;
    GameObject chipPrefab;
    readonly Material[] chipOreMats = new Material[4];
    static readonly string[] OreMatNames = { "LitWhite", "LitGreen", "LitBlue", "LitRed" };

    /// <summary>Called from MineField.BreakCell for EVERY broken wall (player or drone). Null-safe
    /// static: quietly no-ops when no manager exists.</summary>
    public static void SpawnChips(Vector3 pos, CellType tier, int oreElement)
    {
        if (i == null) return;
        int size = tier == CellType.VeryHard ? 2 : tier == CellType.Hard ? 1 : 0;
        int n = Random.Range(i.chipsMinPerBreak, i.chipsMaxPerBreak + 1);
        var mf = MineField.i;
        for (int k = 0; k < n; k++)
        {
            // stay inside the cavity: a scatter offset that lands in rock snaps back to the
            // freshly-broken cell's centre (open by definition)
            Vector3 p = pos + GS.RandCircle(0.05f, 0.4f);
            if (mf != null && mf.IsSolid(mf.WorldToCell(p))) p = pos;
            i.SpawnChip(p, size, oreElement, pos);
        }
    }

    void SpawnChip(Vector3 pos, int sizeClass, int element, Vector2 burstFrom)
    {
        // never cache a failed load — an empty result (asset pipeline mid-refresh) would
        // otherwise poison the whole session
        if (chipSprites == null || chipSprites.Length < 12)
            chipSprites = LoadStripNumeric("OreChips");
        if (chipPrefab == null)
            chipPrefab = Resources.Load<GameObject>("OreChip");
        if (chipSprites.Length < 12) return;

        // cap: cull the oldest chip
        if (OreChip.all.Count >= maxChips && OreChip.all.Count > 0)
        {
            var oldest = OreChip.all[0];
            if (oldest != null) Destroy(oldest.gameObject);
            else OreChip.all.RemoveAt(0);
        }

        OreChip chip;
        if (chipPrefab != null)
        {
            chip = Instantiate(chipPrefab, pos, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                GS.FindParent(GS.Parent.loot)).GetComponent<OreChip>();
        }
        else
        {
            // no prefab yet (kit builder not run) — build one from scratch
            var go = new GameObject("OreChip");
            go.transform.SetParent(GS.FindParent(GS.Parent.loot));
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            chip = go.AddComponent<OreChip>();
            chip.sr = go.AddComponent<SpriteRenderer>();
        }
        if (chip == null) return;
        chip.sizeClass = sizeClass;
        chip.element = element;
        if (chip.sr == null) chip.sr = chip.GetComponent<SpriteRenderer>();
        if (chip.sr != null)
        {
            chip.sr.sprite = chipSprites[sizeClass * 4 + Random.Range(0, 4)];
            if (element >= 0 && element < 4)
            {
                chipOreMats[element] ??= Resources.Load<Material>("OreMats/" + OreMatNames[element]);
                if (chipOreMats[element] != null) chip.sr.material = chipOreMats[element];
            }
        }
        chip.Tumble(burstFrom);
    }

    /// <summary>Dumped scrap at the base (bag-drone haul) — same chip visuals, base-side, so it
    /// survives the return-home despawn.</summary>
    public static void SpawnScrap(Vector3 pos, int sizeClass, int element)
    {
        if (i == null) Ensure();
        // burstFrom == pos → degenerate direction, so each piece tumbles a random way (dump puff)
        i.SpawnChip(pos, sizeClass, element, pos);
    }

    static void DespawnAllChips()
    {
        // dungeon debris only — the scrap pile hauled home stays
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip != null && chip.transform.InDungeon()) Destroy(chip.gameObject);
        }
    }

    /// <summary>Sliced sheet from Resources in NUMERIC slice order (a plain name sort puts _10
    /// before _2). Mirrors MineField.LoadStripNumeric; shared by the drone visual components.</summary>
    public static Sprite[] LoadStripNumeric(string res)
    {
        var loaded = Resources.LoadAll<Sprite>(res);
        var list = new System.Collections.Generic.List<(int n, Sprite s)>();
        string prefix = res + "_";
        foreach (var s in loaded)
            if (s.name.StartsWith(prefix) && int.TryParse(s.name.Substring(prefix.Length), out int n))
                list.Add((n, s));
        list.Sort((a, b) => a.n.CompareTo(b.n));
        var arr = new Sprite[list.Count];
        for (int k = 0; k < list.Count; k++) arr[k] = list[k].s;
        return arr;
    }

    /// <summary>Deploy hook for pads that come online MID-DIVE. Dungeon pads are built from the
    /// in-dungeon menu, so a fresh pad's first chance to serve is while the player is already
    /// down there — without this, assigned drones would only arrive after a full surface-and-
    /// redive. No-op unless the player is in the dungeon.</summary>
    public static void TryDeployNow()
    {
        if (PortalScript.i == null || !PortalScript.i.inDungeon) return;
        DeployAssigned();
    }

    static void DeployAssigned()
    {
        for (int k = AllyAI.allies.Count - 1; k >= 0; k--)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.transform.InDungeon()) continue;
            if (!d.Charged || d.assignedPad == null) continue;
            if (d.state == Drone.State.BoardingVehicle) continue;
            Telepad link = d.assignedPad.IsDungeonSide ? d.assignedPad : d.assignedPad.Linked;
            if (link == null || !link.IsOperational) continue;
            d.Deploy(link);
        }
    }

    static void RecallAllFromDungeon()
    {
        for (int k = AllyAI.allies.Count - 1; k >= 0; k--)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.transform.InDungeon()) d.RecallHome();
        }
    }

    /// <summary>Era change regenerates the whole dungeon — its pads go with it (their tombstoned
    /// slots free up for the next era's pads).</summary>
    void TearDownDungeonPads()
    {
        foreach (Telepad t in TelepadNetwork.DungeonPadsSnapshot())
            if (t != null) Destroy(t.gameObject);
    }
}
