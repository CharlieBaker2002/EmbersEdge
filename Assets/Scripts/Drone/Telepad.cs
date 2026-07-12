using TMPro;
using UnityEngine;

/// <summary>
/// Drone gateway between dimensions, buildable at base AND in the dungeon (dungeonBuildable +
/// builtBlasts 0: dungeon placement completes on purchase — there are no orb pylons down there
/// to fly construction orbs in). Base pad #N links to dungeon pad #N (TelepadNetwork, by build
/// order; the number is shown on the pad). The dungeon pad doubles as the RALLY POINT for the
/// units it serves.
///
/// COLONY MODEL: the player never assigns individual drones. Clicking a BASE pad opens its slot
/// UI (up to <see cref="capacity"/> request slots), each filled with a unit TYPE — drill drone,
/// bag drone or attack unit (a crewed vehicle). When the player dives, DroneManager fills the
/// requests from whatever matching units exist; the requests persist as standing orders.
/// </summary>
public class Telepad : Building
{
    [Header("Telepad")]
    [Tooltip("Total request slots on this pad (drill + bag + attack).")]
    public int capacity = 4;

    [HideInInspector] public int index = -1;
    [Tooltip("Standing deployment requests, filled at every dive.")]
    public int reqDrill, reqBag, reqAttack;

    TextMeshPro padNumber;
    bool dungeonSideCached;
    bool sideCached;
    readonly BaseTile[] reqTiles = new BaseTile[4];

    public bool IsDungeonSide
    {
        get
        {
            if (!sideCached)
            {
                sideCached = true;
                dungeonSideCached = !PathZone.AtBase(transform.position);
            }
            return dungeonSideCached;
        }
    }

    public Telepad Linked => TelepadNetwork.LinkOf(this);

    /// <summary>Built, alive and enabled — a ghost awaiting drone repair is not operational.</summary>
    public bool IsOperational => builtYet && enabled && !IsGhostAwaitingRepair;

    public Vector2 RallyPoint => transform.position;

    public int TotalRequests => reqDrill + reqBag + reqAttack;

    public override void Start()
    {
        base.Start();
        // Request UI lives on BASE pads only — the dungeon pad is just the exit.
        if (IsDungeonSide) return;
        int[] free = { 0, 0, 0, 0 };
        Sprite drill = FirstFrame("DroneDrill");
        Sprite bag = FirstFrame("DroneBag");
        Sprite drone = FirstFrame("Drone");
        AddSlot(free, "", drill, false, () => AddRequest(ref reqDrill), false, null, () => TotalRequests < capacity);
        reqTiles[0] = tiles[tiles.Count - 1];
        AddSlot(free, "", bag, false, () => AddRequest(ref reqBag), false, null, () => TotalRequests < capacity);
        reqTiles[1] = tiles[tiles.Count - 1];
        AddSlot(free, "", drone, false, () => AddRequest(ref reqAttack), false, null, () => TotalRequests < capacity);
        reqTiles[2] = tiles[tiles.Count - 1];
        AddSlot(free, "", null, false, ClearRequests, false, null, () => TotalRequests > 0);
        reqTiles[3] = tiles[tiles.Count - 1];
        RefreshRequestLabels();
    }

    static Sprite FirstFrame(string strip)
    {
        var frames = DroneManager.LoadStripNumeric(strip);
        return frames.Length > 0 ? frames[0] : null;
    }

    void AddRequest(ref int req)
    {
        if (TotalRequests >= capacity) return;
        req++;
        RefreshRequestLabels();
    }

    void ClearRequests()
    {
        reqDrill = reqBag = reqAttack = 0;
        RefreshRequestLabels();
    }

    void RefreshRequestLabels()
    {
        if (reqTiles[0] != null) reqTiles[0].txt.text = $"Drill Drones ×{reqDrill}";
        if (reqTiles[1] != null) reqTiles[1].txt.text = $"Bag Drones ×{reqBag}";
        if (reqTiles[2] != null) reqTiles[2].txt.text = $"Attack Units ×{reqAttack}";
        if (reqTiles[3] != null) reqTiles[3].txt.text = $"Clear — {capacity - TotalRequests}/{capacity} free";
    }

    protected override void BEnable()
    {
        DroneManager.Ensure();
        if (index < 0) index = TelepadNetwork.Register(this);
        if (padNumber == null && UIManager.i != null)
        {
            padNumber = Instantiate(UIManager.i.numText, transform.position + new Vector3(0f, 0.55f, 0f),
                Quaternion.identity, transform);
            padNumber.gameObject.SetActive(true);
        }
        if (padNumber != null)
        {
            padNumber.text = (index + 1).ToString();
            padNumber.color = GS.ColFromEra() * 1.25f;
        }
        // A dungeon pad coming online mid-dive serves its base pad's requests right away —
        // the player shouldn't have to surface and re-dive to fetch the units.
        if (IsDungeonSide) DroneManager.TryDeployNow();
    }

    public override void OnDestroy()
    {
        TelepadNetwork.Unregister(this);
        // free the mine-cell occupancy dungeon placement recorded
        if (IsDungeonSide && MineField.i != null)
            BM.DungeonOccupancy.Remove(MineField.i.WorldToCell(transform.position));
        // deployed units pointing at this pad fall back to default recall landings
        for (int k = 0; k < AllyAI.allies.Count; k++)
            if (AllyAI.allies[k] is Drone d && d != null && d.assignedPad == this) d.assignedPad = null;
        for (int k = 0; k < PilotedVehicle.all.Count; k++)
        {
            var v = PilotedVehicle.all[k];
            if (v != null && v.deployedVia == this) v.deployedVia = null;
        }
        base.OnDestroy();
    }
}
