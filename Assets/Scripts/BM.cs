using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class BM : MonoBehaviour //Building Manager
{
    public static BM i;
    public GameObject UI;
    public GameObject redBuilding;
    private Building rbb;
    public List<Building> buildings; //active
    public GameObject UIPrefab;
    public Transform[] UIspots;
    [HideInInspector] public int[] cost = new int[4];
    private GameObject redbuildingPrefab;
    private Action<InputAction.CallbackContext> clickAction;
    private Action escape;
    private Action closeUIDel;
    [SerializeField] Transform mainDaddyT;
    [SerializeField] GameObject backButton;
    [SerializeField] DaddyBuildingTile[] daddies;
    [SerializeField] Transform[] daddyTs;
    private BuildingTile recent;
    // Stores the original colour for each building sprite so it can be restored later
    private readonly Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();
    //private bool sampled = false;
    [HideInInspector] public bool planting = false;
    public bool added = false;
    public Action goToDaddy;
    [SerializeField] GameObject map;
    [SerializeField] Vector2Int gridSize = new Vector2Int(1,1); // size in cells
    Vector2Int anchorCell;                                      // where we’re hovering
    int rotationStep;                                           // 0..3, each step = 90° clockwise
    // multi-drag placement (Building.multiDrag): sweep with the button held to stamp copies
    Vector2Int lastStampCell;
    bool dragArmed;      // a deliberate click starts the sweep (guards against the menu click's held button)
    bool upfrontSpent;   // the menu click pre-charged ONE copy; later stamps charge per placement

    /// <summary>A refundable up-front build charge is outstanding: a building was picked from
    /// the menu (bank already debited) but nothing has been placed yet — Escape would refund
    /// it in full. While this is true, held orbs must NOT bank into the pylons
    /// (ResourceManager.DropResources gates on it): banking against the debited pool and then
    /// cancelling would refund on top of the freshly-banked orbs and push the pylons past
    /// their caps. The allocation itself stays — only the physical orb movement waits for the
    /// placement click. Zero once a copy is committed (cost is consumed, nothing refunds).</summary>
    public static bool PlacementRefundPending
    {
        get
        {
            if (i == null || !i.planting || i.redBuilding == null) return false;
            return i.cost[0] != 0 || i.cost[1] != 0 || i.cost[2] != 0 || i.cost[3] != 0;
        }
    }

    // ---- dungeon placement (Building.dungeonBuildable, e.g. the Telepad) ----
    // The base grid doesn't exist down there: snap to mine cells, validate on excavated floor,
    // and keep occupancy in this set (mirrors GridManager.SetArea). Static + reload-off ⇒ reset.
    bool dungeonMode;
    Vector3Int dungeonAnchor;
    public static readonly HashSet<Vector3Int> DungeonOccupancy = new HashSet<Vector3Int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDungeonOccupancy() => DungeonOccupancy.Clear();
    
    private void Awake()
    {
        i = this;
    }

    public void AddDaddyDel()
    {
        if (added) return;
        EscapeRouter.i?.Push(goToDaddy);
        added = true;
    }

    public void RemoveDaddyDel()
    {
        EscapeRouter.i?.Remove(goToDaddy);
        added = false;
    }

    public void ChangeBuildingColour(bool on)
    {
    }

    private void Start()
    {
        IM.i.pi.Player.Build.started += _ => AltUI();
        IM.i.pi.Player.Escape.Enable();
        IM.i.pi.Player.Build.Enable();
        clickAction = delegate { TryPlace(); };
        escape = () => Escape();
        closeUIDel = CloseUIs;
        goToDaddy = () =>
        {
            // No-op if router already popped us (Esc path); pops us when called
            // programmatically (e.g. BuildingUI.OnDisable) so the stack stays in sync.
            EscapeRouter.i?.Remove(goToDaddy);
            added = false;
            BackItUpOffDaddy(false);
        };
    }

    private void Update()
    {
        // Placed-building hotkeys. The placement ghost handles its own R inside the follow
        // coroutine, so a live session owns the key exclusively.
        if (planting || redBuilding != null) return;
        if (Keyboard.current == null || IM.i == null || CharacterScript.dead) return;
        bool rotate = Keyboard.current.rKey.wasPressedThisFrame;
        // Mac keyboards label backspace "delete" — accept both for the demolition mark
        bool demolish = Keyboard.current.deleteKey.wasPressedThisFrame
                     || Keyboard.current.backspaceKey.wasPressedThisFrame;
        if (!rotate && !demolish) return;
        Building b = BuildingUnderCursor();
        if (b == null) return;
        if (rotate) b.TryRotate90();
        // Delete toggles the drone-demolition mark: once to condemn, again to reprieve.
        // Built base-side buildings only — construction flows and dungeon pads keep their own rules.
        if (demolish && b.builtYet && PathZone.AtBase(b.transform.position)) DemolitionMarks.Toggle(b);
    }

    /// <summary>The placed building under the cursor — FocusRouter's raycast recipe: the live
    /// physic carries an IClickableCarrier pointing back at its Building; ghosts (no physic yet)
    /// are found through any collider parented under the building itself.</summary>
    static Building BuildingUnderCursor()
    {
        Vector2 p = IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();
        var hits = Physics2D.RaycastAll(new Vector3(p.x, p.y, -100f), Vector3.forward, 1000f,
            LayerMask.GetMask("Ally Buildings"));
        foreach (var h in hits)
        {
            var rb = h.collider.attachedRigidbody;
            if (rb != null && rb.TryGetComponent<IClickableCarrier>(out var rcar) && rcar.clickable is Building carried) return carried;
            if (h.collider.TryGetComponent<IClickableCarrier>(out var car) && car.clickable is Building bb) return bb;
            var direct = h.collider.GetComponentInParent<Building>();
            if (direct != null) return direct;
        }
        return null;
    }

    public void AltUI() //inefficient but few lines so meh.
    {
        if (CharacterScript.dead)
        {
            return;
        }

        bool inDungeon = GS.CS().InDungeon();
        bool wasActive = UI.activeInHierarchy;
        UIManager.CloseAllUIs();
        if (!wasActive)
        {
            if (inDungeon && !AnyDungeonBuildable()) return;   // nothing placeable down here yet
            if (!IM.i.CActive())
            {
                IM.i.OpenCursor();
            }

            UI.SetActive(true);
            IM.i.pi.Player.Interact.Enable();
            if (inDungeon) SetupDungeonPalette();
            else DetermineFitDaddies();
            EscapeRouter.i?.Push(closeUIDel);
        }
        else
        {
            IM.i.CloseCursor();
        }
    }

    bool AnyDungeonBuildable()
    {
        foreach (GameObject g in GetAllBuildings())
        {
            var b = g != null ? g.GetComponentInChildren<Building>(true) : null;
            if (b != null && b.dungeonBuildable) return true;
        }
        return false;
    }

    /// <summary>The in-dungeon build menu: a flat palette of dungeonBuildable buildings only
    /// (no daddy groups). Tiles reuse the exact SetupDaddy init so cost/icon behave identically.</summary>
    void SetupDungeonPalette()
    {
        foreach (DaddyBuildingTile d in daddies)
        {
            d.gameObject.SetActive(false);
        }
        DaddyBuildingTile.current = null;
        backButton.SetActive(false);
        int pos = 0;
        foreach (GameObject g in GetAllBuildings())
        {
            var build = g != null ? g.GetComponentInChildren<Building>(true) : null;
            if (build == null || !build.dungeonBuildable) continue;
            var a = Instantiate(UIPrefab, UIspots[pos + 4].position, Quaternion.identity, UI.transform);
            BuildingTile tile = a.GetComponent<BuildingTile>();
            tile.img.sprite = build.icon == null ? build.sr.sprite : build.icon;
            int[] costB = new int[4] { 0, 0, 0, 0 };
            foreach (OrbMagnet om in g.GetComponents<OrbMagnet>())
            {
                if (om.typ == OrbMagnet.OrbType.Task)
                {
                    costB[om.orbType] += om.capacity;
                    om.init = true;
                }
            }
            tile.cost = costB;
            tile.txt.text = g.name;
            tile.UpdateCost();
            tile.ChangeBackground();
            tile.buildingPrefab = g;
            pos++;
        }
    }

    public void CloseUIs() //called from uimanager
    {
        UI.SetActive(false);
        Escape(false);
        EscapeRouter.i?.Remove(goToDaddy);
        added = false;
        EscapeRouter.i?.Remove(closeUIDel);
        DestroyChildren();
        foreach (DaddyBuildingTile d in daddies)
        {
            d.gameObject.SetActive(false);
        }
    }
    
    public void BuildingFollowMouse(GameObject g, BuildingTile r)
    {
        map.SetActive(false);
        dungeonMode = GS.CS().InDungeon();
        recent = r;
        redbuildingPrefab = g;
        redBuilding = Instantiate(g);
        rbb = redBuilding.GetComponentInChildren<Building>(true);
        redbuildingPrefab.transform.position = new Vector3(redbuildingPrefab.transform.position.x,
            redbuildingPrefab.transform.position.y, 0f);
        foreach(SpriteRenderer s in redBuilding.GetComponentsInChildren<SpriteRenderer>(true))
        {
            s.color = new Color(0.35f, 0.35f, 0.35f, 0.75f);
        }
        if (!dungeonMode) GridManager.i.ActivateGrid();
        ChangeBuildingColour(false);
        rotationStep = 0;
        lastStampCell = new Vector2Int(int.MinValue, int.MinValue);
        dragArmed = false;
        upfrontSpent = false;
        RecomputeGridSize();
        IM.i.pi.Player.Interact.performed += clickAction;
        // Daddy delegate is already on the router; layer the placement-cancel handler on top.
        // Pressing Esc now hits placement-cancel first, then the daddy UI back-out.
        EscapeRouter.i?.Remove(goToDaddy);
        added = false;
        EscapeRouter.i?.Push(escape);
        IM.i.pi.Player.Interact.Disable();
        planting = true;
        StartCoroutine(BuildingFollowMouse());
    }

    private IEnumerator BuildingFollowMouse()
    {
        yield return null;
        IM.i.pi.Player.Interact.Enable();
        while (redBuilding != null)
        {
            yield return GS.WFFU;
            if (redBuilding == null) yield break;

            if (dungeonMode)
            {
                // Mine-cell snap; validity is painted straight onto the ghost (no grid overlay).
                PositionDungeon();
                continue;
            }

            // R rotates the preview 90° clockwise. Non-square footprints swap their gridSize.
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                rotationStep = (rotationStep + 1) % 4;
                redBuilding.transform.rotation = Quaternion.Euler(0f, 0f, -90f * rotationStep);
                RecomputeGridSize();
            }

            // Snap to grid and preview footprint
            Position(redBuilding.transform);

            bool gridClear   = GridManager.i.AreaClear(anchorCell, gridSize);
            // Validity matches TryPlace() — both rely on AreaClear's inRange, which RebuildRangeCache now
            // clips to the map's inner inset (pulled in by buildEdgeMargin), so cells near the edge is
            // already out-of-range and unbuildable. No per-frame bounds test needed.
            // colour overlay & sprite tint
            GridManager.i.PreviewArea(anchorCell, gridSize, gridClear);

            // multi-drag sweep: with the place button held (after a deliberate first click), stamp a
            // copy on every NEW clear cell the cursor passes over
            if (dragArmed && rbb != null && rbb.multiDrag && gridClear &&
                anchorCell != lastStampCell && IM.i.pi.Player.Interact.IsPressed())
            {
                StampMultiCopy();
            }
        }
    }

    /// <summary>World-space bounding size after rotation. 90°/270° swap x and y.</summary>
    Vector2 EffectiveSize()
    {
        return rotationStep % 2 == 0 ? rbb.size : new Vector2(rbb.size.y, rbb.size.x);
    }

    void RecomputeGridSize()
    {
        Vector2 eff = EffectiveSize();
        gridSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(eff.x / GridManager.i.cellSize)),
            Mathf.Max(1, Mathf.RoundToInt(eff.y / GridManager.i.cellSize)));
    }

    public void Escape(bool activateGoToDaddy = true)
    {
        StopAllCoroutines();
        if (!dungeonMode) GridManager.i.DeactivateGrid();
        if (redBuilding != null)
        {
            ResourceManager.instance.CanAfford(cost, true);
            Destroy(redBuilding);
            redBuilding = null;
        }

        IM.i.pi.Player.Interact.performed -= clickAction;
        // No-op when called via the router (it already popped us); active when called
        // programmatically (CloseUIs, BuildingTile.OnClick re-pick).
        EscapeRouter.i?.Remove(escape);
        if (activateGoToDaddy)
        {
            AddDaddyDel();
        }

        map.SetActive(true);
        planting = false;
        dungeonMode = false;
    }

    private void TryPlace()
    {
        if (redBuilding == null)
        {
            return;
        }

        if (dungeonMode)
        {
            if (!DungeonAreaClear()) return;
            Commit(redBuilding, rbb, false);
            planting = false;
            redBuilding = null;
            IM.i.pi.Player.Interact.performed -= clickAction;
            EscapeRouter.i?.Remove(escape);
            map.SetActive(true);
            GS.QA(() =>
            {
                ResourceManager.instance.DropResources();
                if (ResourceManager.instance.CanAfford(recent.cost, false, false))
                {
                    recent.OnClick();
                }
            }, 2);
            return;
        }

        if (!GridManager.i.AreaClear(anchorCell, gridSize))
            return;

        // multi-drag buildings (walls): stamp a copy and KEEP placing — the ghost stays on the
        // cursor, the sweep poll in the follow coroutine stamps more, Escape ends the session.
        if (rbb != null && rbb.multiDrag)
        {
            dragArmed = true;
            StampMultiCopy();
            return;
        }

        Commit(redBuilding, rbb, false);
        GridManager.i.DeactivateGrid();

        planting = false;
        redBuilding = null;
        IM.i.pi.Player.Interact.performed -= clickAction;
        // Successful place: pop placement-cancel; daddy UI back-out (closeUIDel) stays on stack.
        EscapeRouter.i?.Remove(escape);
        AddDaddyDel();
        GS.QA(() =>
        {
            // Held orbs were frozen out of the bank while the placement was refundable — settle
            // them now so the re-pick check sees everything the player actually has.
            ResourceManager.instance.DropResources();
            if (ResourceManager.instance.CanAfford(recent.cost, false, false))
            {
                recent.OnClick();
            }
        }, 2);
    }

    /// <summary>One multi-drag stamp at the current (verified clear) anchor: charge, clone, commit.
    /// The first stamp consumes the menu click's up-front charge; later ones pay per placement.</summary>
    void StampMultiCopy()
    {
        if (upfrontSpent)
        {
            // cost is already zeroed here, so nothing is refundable — held orbs may settle
            // into the bank before the per-stamp charge instead of waiting for the auto-bank tick.
            ResourceManager.instance.DropResources();
            if (!ResourceManager.instance.CanAfford(recent.cost))
            {
                Escape();   // out of resources — close the placement session (cost already zeroed, so nothing refunds)
                return;
            }
        }
        else
        {
            upfrontSpent = true;
            GS.CopyArray(ref cost, new int[4]);   // up-front charge is now consumed — Escape must not refund it
        }

        var built = Instantiate(redbuildingPrefab, redBuilding.transform.position, redBuilding.transform.rotation);
        var bb = built.GetComponentInChildren<Building>(true);
        Commit(built, bb, true);
        lastStampCell = anchorCell;
    }

    /// <summary>Turn a placed instance into a live under-construction building at the current
    /// anchor: grid occupancy, era tint, orb-task magnets (whose completion also FINISHES 0-blast
    /// buildings — orb-only construction, no ember), decompressors, registry.</summary>
    void Commit(GameObject built, Building bb, bool freshInstance)
    {
        if (dungeonMode)
        {
            CommitDungeon(built, bb);
            return;
        }
        GridManager.i.SetArea(anchorCell, gridSize, true);
        bb.anchorCell = anchorCell;
        bb.gridSize = gridSize;

        if (bb.hasExtraParent)
        {
            foreach (SpriteRenderer s in bb.transform.parent.GetComponentsInChildren<SpriteRenderer>(true))
            {
                s.color = GS.ColFromEra();
            }
        }
        else
        {
            foreach (SpriteRenderer s in bb.GetComponentsInChildren<SpriteRenderer>(true))
            {
                s.color = GS.ColFromEra();
            }
        }

        var ground = bb.groundEdit;
        if (ground != null) ground.SetActive((true));
        foreach (FastSpriteDecompressor fsd in built.GetComponentsInChildren<FastSpriteDecompressor>(true))
        {
            fsd.enabled = true;
        }

        var bros = built.GetComponents<OrbMagnet>().Where(x => x.typ == OrbMagnet.OrbType.Task).ToArray();

        built.transform.parent = GS.FindParent(GS.Parent.buildings);
        buildings.Add(bb);
        var SD = built.GetComponentsInChildren<SpriteDecompressor>(true);
        // CHEATBUILD: skip the orb-task phase — no magnets to fill, no orbs to fly. Mirror
        // CommitDungeon: drop the task magnets and wake every behaviour so Start/BuildFirst run
        // (LoadWithEEs then skips the ember phase too). Fresh stamps keep the 2-frame defer past
        // their ghost-init, same as the normal enable path below.
        if (RefreshManager.i != null && RefreshManager.i.CHEATBUILD)
        {
            foreach (OrbMagnet om in bros) Destroy(om);
            GS.QA(() =>
            {
                if (built == null || bb == null) return;
                if (bb.TryGetComponent<Collider2D>(out var ghostCol)) Destroy(ghostCol);
                foreach (Behaviour beh in built.GetComponentsInChildren<Behaviour>(true))
                {
                    if (beh is OrbMagnet) continue;   // doomed (Destroy is deferred) — don't wake them
                    beh.enabled = true;
                }
                if (bb.builtBlasts <= 0) bb.CompleteViaOrbs();   // orb-only buildings have no ember phase to finish them
            }, freshInstance ? 2 : 0);
            return;
        }
        foreach (OrbMagnet om in bros)
        {
            if (om.typ == OrbMagnet.OrbType.Task)
            {
                foreach (var o in bros)
                {
                    if (o != om)
                    {
                        om.siblingTs.Add(o);
                    }
                }
                om.action = delegate
                {
                    if (bb == null) return;
                    if (bb.TryGetComponent<Collider2D>(out var col))
                    {
                        Destroy(col);
                    }
                    if (bb.builtBlasts <= 0)
                    {
                        bb.CompleteViaOrbs();   // orb-only construction: the task filling IS the build
                    }
                    // physic is created+activated by SwitchMonos(true) (the EE-icon build path),
                    // which is QA-deferred and races this orb-task callback. If the orbs land
                    // first, physic is still null here — skip; SwitchMonos will create AND
                    // activate it a moment later (this SetActive is redundant with that). Without
                    // the guard this NREs intermittently on build.
                    if (bb.physic != null) bb.physic.gameObject.SetActive(true);
                };
                foreach (var spriteDecompressor in SD)
                {
                    spriteDecompressor.oms.Add(om);
                }
            }
        }

        // A freshly-instantiated stamp hasn't run Building.Start yet — its ghost-init
        // (SwitchMonos(false, init)) lands NEXT frame and would flip anything it owns back off.
        // Defer the enables past it; the ghost-turned-building path enables immediately as before.
        if (freshInstance)
        {
            GS.QA(() =>
            {
                if (built == null) return;
                foreach (OrbMagnet om in bros) { if (om != null) om.enabled = true; }
                foreach (var sd in SD) { if (sd != null) sd.enabled = true; }
            }, 2);
        }
        else
        {
            foreach (OrbMagnet om in bros)
            {
                om.enabled = true;
            }

            foreach (var sd in SD)
            {
                sd.enabled = true;
            }
        }
    }


    // ---- dungeon placement helpers ----

    void PositionDungeon()
    {
        if (MineField.i == null || redBuilding == null) return;
        Vector2 worldMouse = IM.controller && IM.i.CActive() ? (Vector2)IM.i.controllerCursor.position : IM.i.MousePosition();
        dungeonAnchor = MineField.i.WorldToCell(worldMouse);
        redBuilding.transform.position = MineField.i.CellCenterWorld(dungeonAnchor);
        bool clear = DungeonAreaClear();
        Color tint = clear ? new Color(0.35f, 0.35f, 0.35f, 0.75f) : new Color(0.7f, 0.15f, 0.15f, 0.75f);
        foreach (SpriteRenderer s in redBuilding.GetComponentsInChildren<SpriteRenderer>(true))
        {
            s.color = tint;
        }
    }

    bool DungeonAreaClear()
    {
        if (MineField.i == null) return false;
        return MineField.i.IsExcavated(dungeonAnchor) && !DungeonOccupancy.Contains(dungeonAnchor);
    }

    /// <summary>Dungeon commit: mine-cell occupancy instead of the base grid, and construction
    /// completes IMMEDIATELY — the cost was charged from the bank on the menu click, and no orb
    /// pylons exist in the dungeon to fly the task orbs in.</summary>
    void CommitDungeon(GameObject built, Building bb)
    {
        DungeonOccupancy.Add(dungeonAnchor);
        foreach (SpriteRenderer s in built.GetComponentsInChildren<SpriteRenderer>(true))
        {
            s.color = GS.ColFromEra();
        }
        built.transform.parent = GS.FindParent(GS.Parent.buildings);
        buildings.Add(bb);
        foreach (OrbMagnet om in built.GetComponents<OrbMagnet>())
        {
            if (om.typ == OrbMagnet.OrbType.Task) Destroy(om);
        }
        // Building prefabs ship with the main script DISABLED — in the base flow the filled orb
        // task enables every child Behaviour before invoking CompleteViaOrbs (OrbMagnet.ReceiveOrb).
        // We just destroyed those magnets, so replicate that enable here or Start never runs and
        // CompleteViaOrbs retry-loops forever on startCalled == false.
        foreach (Behaviour beh in built.GetComponentsInChildren<Behaviour>(true))
        {
            if (beh is OrbMagnet) continue;   // doomed (Destroy is deferred) — don't wake them
            beh.enabled = true;
        }
        bb.CompleteViaOrbs();   // QA-retries internally until the instance's Start has run
    }

    private void Position(Transform t)
        {
            Vector2 eff = EffectiveSize();
            Vector2 vAdjust = new Vector2(-0.125f + 0.5f * eff.x, -0.125f + 0.5f * eff.y);
            Vector2 worldMouse = IM.controller && IM.i.CActive()
                ? (Vector2)IM.i.controllerCursor.position
                : IM.i.MousePosition();
            worldMouse -= vAdjust;
            anchorCell = GridManager.i.WorldToGrid(worldMouse);
            Vector3 snapped = GridManager.i.GridToWorld(anchorCell) + (Vector3)vAdjust; //0.375 for 1, 0.875 for 2
            redBuilding.transform.position = snapped;
            bool clear = GridManager.i.AreaClear(anchorCell, gridSize);
            GridManager.i.PreviewArea(anchorCell, gridSize, clear);
        }
        
        public void SetupDaddy(DaddyBuildingTile t)
        {
            t.transform.position = mainDaddyT.transform.position;
            backButton.SetActive(true);
            foreach (DaddyBuildingTile d in daddies)
            {
                if (d != t)
                {
                    d.gameObject.SetActive(false);
                }
            }
            // Palette layout: UIspots rows 2+ (index 4 onward), 4 tiles per row.
            // A null entry in the daddy's buildings list = start a new row.
            const int columns = 4;
            int col = 0, row = 0;
            for (int i = 0; i < t.buildings.Length; i++)
            {
                if (t.buildings[i] == null)
                {
                    if (col > 0) { row++; col = 0; }
                    continue;
                }
                bool has = false;
                foreach (GameObject g in GetAllBuildings())
                {
                    if (t.buildings[i] == g)
                    {
                        has = true;
                        break;
                    }
                }
                if (!has) continue;
                int spot = 4 + row * columns + col;
                if (spot >= UIspots.Length) break;
                col++;
                if (col == columns) { row++; col = 0; }
                var a = Instantiate(UIPrefab, UIspots[spot].position, Quaternion.identity, UI.transform);
                BuildingTile tile = a.GetComponent<BuildingTile>();
                Building build = t.buildings[i].GetComponentInChildren<Building>(true);
                tile.img.sprite = build.icon == null ? build.sr.sprite : build.icon;
                int[] costB = new int[4] { 0, 0, 0, 0 };
                foreach (OrbMagnet om in t.buildings[i].GetComponents<OrbMagnet>())
                {
                    if (om.typ == OrbMagnet.OrbType.Task)
                    {
                        costB[om.orbType] += om.capacity;
                        om.init = true;
                    }
                }
                tile.cost = costB;
                tile.txt.text = t.buildings[i].name;
                tile.UpdateCost();
                tile.ChangeBackground();
                tile.buildingPrefab = t.buildings[i];
            }
        }
        
        void DetermineFitDaddies()
        {
            int pos = 0;
            foreach (DaddyBuildingTile d in daddies)
            {
                if (d.buildings.Intersect(GetAllBuildings()).FirstOrDefault() != null)
                {
                    d.transform.position = daddyTs[pos].position;
                    d.gameObject.SetActive(true);
                    pos++;
                }
                else
                {
                    d.gameObject.SetActive(false);
                }
            }
            DaddyBuildingTile.current = null;
            backButton.SetActive(false);
        }
        
        void DestroyChildren()
        {
            foreach (BuildingTile t in UI.GetComponentsInChildren<BuildingTile>(true))
            {
                Destroy(t.gameObject);
            }
        }
        
        public void BackItUpOffDaddy(bool callEscape = true) // go back
        {
            DestroyChildren();
            DetermineFitDaddies();
            added = false;
            if (callEscape)
            {
                Escape();
            }
        }
        
        private List<GameObject> GetAllBuildings()
        {
            // Telepads are never player-buildable: base pads come from expansion events, dungeon
            // pads from dungeon generation. Filter them out of every palette (base AND dungeon).
            return BlueprintManager.GetBuildings(BlueprintManager.researched).Select((x => x.g)).Union(BlueprintManager.i.defaultBuildings)
                .Where(g => g == null || g.GetComponentInChildren<Telepad>(true) == null).ToList();
        }
}