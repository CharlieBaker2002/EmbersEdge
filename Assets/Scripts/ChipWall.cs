using System.Collections;
using UnityEngine;

/// <summary>
/// The chip-built Wall. Orbs only BUY the stub (the standard multi-drag orb task); the wall
/// becomes real masonry once plain-rock chips are bricked into it — a bag-drone haul like any
/// other chip customer (IChipConsumer — the fleet needs no changes). ALL later structure work
/// is chip work too: live damage and destroyed-ghost rebuilds pull chips at the same juice→hp
/// rate, and medic drones skip walls outright (NeedsDroneRepair is permanently false here).
///
/// Standard juice metrics throughout (OreChip.JuiceFor: small 0.1 / medium 0.5 / large 1.25):
/// raising the wall costs <see cref="buildJuice"/>; repairs price hp at buildJuice/maxHealth,
/// so a full ghost rebuild costs exactly one build. Any SIZE of plain rock is masonry (ore
/// never is — it belongs to the refiner); an oversized chip's excess juice stays banked in the
/// wall and pre-pays future repairs, so the intake never stalls waiting for a perfectly-sized
/// chip. Walls bid rock at appeal 2 — above every grinder (station/factory run rock at 1) —
/// so a hole in the perimeter eats before batteries charge.
///
/// The intake runs on a persistent coroutine, not Update: the Building script is DISABLED for
/// both hungry phases (ghost-init while awaiting build chips, SwitchMonos(false) as a destroyed
/// ghost) and coroutines keep ticking through that.
/// </summary>
public class ChipWall : Building, IChipConsumer
{
    [Header("Chip Wall")]
    [Tooltip("Plain-rock juice to raise the wall (small chip 0.1 / medium 0.5 / large 1.25). Repairs price hp at buildJuice/maxHealth, so a full rebuild costs one build.")]
    public float buildJuice = 0.5f;
    [Tooltip("Loose rock chips inside this radius are drawn into the wall whenever it wants juice. Drone spills scatter up to ~0.45 from the drop point — keep it above that.")]
    public float suctionRadius = 0.75f;

    /// <summary>Rock banked toward the current want: build progress before completion, then a
    /// small carry-over buffer (an oversized chip's excess) that pre-pays repairs.</summary>
    float juice;
    int inboundChipSpace;
    bool awaitingBuildChips;   // orbs paid, masonry owed — the stub phase
    Coroutine intakeCo;

    float JuicePerHp => buildJuice / Mathf.Max(0.01f, maxHealth);

    // ------------------------------------------------------------------ build (orbs buy, chips build)

    /// <summary>The orb task filling BUYS the wall but no longer completes it — that starts the
    /// chip phase instead. Cheat and dungeon commits keep the instant path (no chip logistics
    /// there); completion proper happens in ApplyJuice via base.CompleteViaOrbs().</summary>
    public override void CompleteViaOrbs()
    {
        if (RefreshManager.i != null && RefreshManager.i.CHEATBUILD) { base.CompleteViaOrbs(); return; }
        if (builtYet || awaitingBuildChips) return;
        if (!startCalled)
        {
            // same race as the base path: a synchronous orb-complete can land before Start()
            this.QA(CompleteViaOrbs, 0f);
            return;
        }
        if (transform.InDungeon()) { base.CompleteViaOrbs(); return; }
        awaitingBuildChips = true;
        ChipConsumers.Register(this);
        EnsureIntake();
    }

    protected override void BEnable()
    {
        base.BEnable();
        ChipConsumers.Register(this);   // pre-placed/completed walls want repair chips too
        EnsureIntake();
    }

    // NOT unregistered in BDisable: a destroyed ghost is switched off (SwitchMonos(false)) but
    // must stay on the fleet's board to be fed its rebuild chips.
    public override void OnDestroy()
    {
        if (!GS.qutting) ChipConsumers.Unregister(this);
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ chip intake (IChipConsumer)

    public bool ChipIntakeActive => JuiceWant > 0f;
    public Vector2 ChipDropPoint => transform.position;
    public float ChipIntakeRadius => suctionRadius;
    /// <summary>Rock is the wall's REAL input — bid above the grinders' 1 so masonry lands
    /// before battery juice. Ore is refused in AcceptsChip, so its appeal never matters.</summary>
    public int ChipAppeal(int sizeClass, int element) => 2;
    public int InboundChipSpace { get => inboundChipSpace; set => inboundChipSpace = value; }
    /// <summary>Masonry gate: plain rock only (ore belongs to the refiner), any size, and only
    /// while juice is actually wanted — an idle wall's ring protection lapses with its appetite
    /// (the station rule), so leftover spill stays fair game for hungrier customers.</summary>
    public bool AcceptsChip(int sizeClass, int element) => element < 0 && JuiceWant > 0f;
    /// <summary>Want in bag-space units for the fleet's planning, net of ring spill. Flat
    /// medium-chip exchange rate — the ChipFactory's demand heuristic.</summary>
    public float ChipDemandSpace
        => Mathf.Max(0f, JuiceWant - RingStockJuice()) / OreChip.JuicePerSpace(1);

    /// <summary>Juice still owed on the wall's current job, net of the bank: the build while a
    /// stub, the rebuild while a destroyed ghost, the hp deficit while standing. A condemned
    /// wall wants nothing — wreckers work there, not masons.</summary>
    float JuiceWant
    {
        get
        {
            if (MarkedForDemolition) return 0f;
            float want;
            if (awaitingBuildChips) want = buildJuice;
            else if (IsGhostAwaitingRepair) want = (maxHealth - repairHp) * JuicePerHp;
            else if (builtYet && physic != null && !physic.hasDied) want = (physic.maxHp - physic.hp) * JuicePerHp;
            else return 0f;
            return Mathf.Max(0f, want - juice);
        }
    }

    float ringScanT = float.NegativeInfinity;
    float ringJuiceCached;

    /// <summary>Juice sitting in the ring as settled, unclaimed rock — the wall will eat these
    /// without fleet help. Cached on the intake cadence; mirrors BatteryStation.RingStockJuice.</summary>
    float RingStockJuice()
    {
        if (Time.time - ringScanT < 0.25f) return ringJuiceCached;
        ringScanT = Time.time;
        float stock = 0f;
        Vector2 pos = transform.position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;
            if (chip.element >= 0) continue;   // ore never feeds a wall
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            stock += chip.JuiceValue;
        }
        ringJuiceCached = stock;
        return stock;
    }

    // ------------------------------------------------------------------ suck + apply

    void EnsureIntake()
    {
        if (intakeCo == null && gameObject.activeInHierarchy) intakeCo = StartCoroutine(IntakeLoop());
    }

    IEnumerator IntakeLoop()
    {
        var tick = new WaitForSeconds(0.25f);
        while (true)
        {
            yield return tick;
            if (JuiceWant > 0f) SuckChips();
            ApplyJuice();
        }
    }

    /// <summary>Settled, unclaimed rock in the ring eases into the wall (the grinder's
    /// AbsorbInto shrink) and credits juice. MayGive keeps the same dibs every intake honours —
    /// a chip a keener customer is waiting on stays put.</summary>
    void SuckChips()
    {
        Vector2 pos = transform.position;
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;                  // a drone is flying for it
            if (chip.Age < 0.35f) continue;                        // let fresh drops pop in first
            if (!ChipConsumers.MayGive(this, chip.sizeClass, chip.element)) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            juice += chip.JuiceValue;
            chip.AbsorbInto(transform);
            if (JuiceWant <= 0f) break;
        }
    }

    /// <summary>Spend the bank on the current job: finish the build, or push banked juice
    /// through the standard RepairTick (ghost rebuilds bank there, live walls heal). Leftover
    /// juice stays banked for the next scratch.</summary>
    void ApplyJuice()
    {
        if (juice <= 0f || MarkedForDemolition) return;
        if (awaitingBuildChips)
        {
            // read the masonry landing: the era ghost tint brightens toward built-white
            if (sr != null)
                sr.color = Color.Lerp(GS.ColFromEra(), Color.white, juice / Mathf.Max(0.01f, buildJuice));
            if (juice >= buildJuice - 1e-4f)
            {
                juice = Mathf.Max(0f, juice - buildJuice);
                awaitingBuildChips = false;
                base.CompleteViaOrbs();
            }
            return;
        }
        float applied = RepairTick(juice / JuicePerHp);
        if (applied > 0f) juice = Mathf.Max(0f, juice - applied * JuicePerHp);
    }

    /// <summary>Walls are mason work, never medic work — repair drones skip them entirely
    /// (chips rebuild ghosts and heal damage through the intake instead).</summary>
    public override bool NeedsDroneRepair => false;
}
