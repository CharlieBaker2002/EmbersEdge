using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colony's battery LAYOUT planner: decides which pad/hub deserves the next spare battery so
/// drones can stock the grid automatically. Rules, in order:
///
///   1. PLAYER PINS FIRST. A pad the player manually stocked (EnergyPad.pinnedMin) is topped back
///      up to its pin before anything else gets a battery — even if that starves a newer hub.
///   2. DOCK PADS NEXT. A pad/hub feeding a DroneDock is guaranteed a battery before ordinary
///      pads see one — an unpowered dock stops recharging drones and stalls the whole colony.
///   3. Then batteries spread PROPORTIONALLY TO CONSUMERS, not first-come-first-serve: each pad's
///      weight is how many buildings actually draw from it (its presence in their BuildingPower
///      sources — pylons count, since a pylon's own Power view lists its upstream pads). Seats are
///      dealt D'Hondt style: the next battery goes to the pad maximising consumers/(assigned+1),
///      so a pad powering 2 buildings gets 2 before a 1-building hub gets its 1, and the extra
///      lands back on the busier pad.
///   4. Pads powering NOTHING (and never pinned) get nothing.
///
/// Sources for a move: loose base-side spares, then FULL station stock, then unpinned surplus
/// sitting above another pad's target. Batteries riding home from a station (hasHome) are counted
/// as already belonging to their home pad, so the charge-swap loop never fights the distributor.
/// </summary>
public static class BatteryDistribution
{
    static float lastQueryAt = float.NegativeInfinity;   // full-scan throttle
    static float lastEmptyAt = float.NegativeInfinity;   // negative cache — nothing to do lately

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lastQueryAt = float.NegativeInfinity;
        lastEmptyAt = float.NegativeInfinity;
    }

    // scratch (main thread only, cleared per query)
    static readonly List<EnergyPad> pads = new List<EnergyPad>();
    static readonly List<int> consumers = new List<int>();
    static readonly List<bool> feedsDock = new List<bool>();
    static readonly List<int> current = new List<int>();
    static readonly List<int> target = new List<int>();
    static readonly List<Battery> spares = new List<Battery>();

    /// <summary>A battery worth moving and the pad it should land on, or null. Claims nothing —
    /// the calling drone claims (claimedBy + dest.inboundBatteries).</summary>
    public static Battery FindPlacement(Drone forDrone, out EnergyPad dest)
    {
        dest = null;
        if (Time.time - lastEmptyAt < 0.5f) return null;
        if (Time.time - lastQueryAt < 0.2f) return null;   // bound the fleet's scan rate
        lastQueryAt = Time.time;

        // ---- eligible pads: built, base-side, powering something (or player-pinned) ----
        pads.Clear(); consumers.Clear(); feedsDock.Clear(); current.Clear(); target.Clear();
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            // Capacitor nodes are player-stocked surge housings — never a distribution
            // destination, never robbed for surplus (same standing as stations).
            if (list[k] is not EnergyPad p || p is BatteryStation || p is CapacitorNode) continue;
            if (!p.builtYet || !p.enabled || !p.gameObject.activeInHierarchy) continue;
            if (!PathZone.AtBase(p.transform.position)) continue;
            int c = CountConsumers(p, out bool dock);
            if (c <= 0 && p.pinnedMin <= 0) continue;
            pads.Add(p);
            consumers.Add(c);
            feedsDock.Add(dock);
        }
        if (pads.Count == 0) { lastEmptyAt = Time.time; return null; }

        // ---- what each pad effectively holds already ----
        for (int i = 0; i < pads.Count; i++)
            current.Add(pads[i].SlottedCount + pads[i].inboundBatteries);

        // ---- the spare pool, and batteries riding home (those belong to their home pad) ----
        // Station stock is spare when it's full, when it's had its day's charge, or when the
        // grinder can't charge anyway (no chips) — "put the most juice on the buildings".
        bool chargingPossible = BatteryStation.ChargingPossible();
        spares.Clear();
        for (int k = 0; k < Battery.all.Count; k++)
        {
            var b = Battery.all[k];
            if (b == null || b.transform.InDungeon() || b.following || b == Battery.held) continue;
            if (b.IsPulse) continue;   // placing a pulse battery is the PLAYER'S call — never redistributed
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (b.hasHome)
            {
                // mid-logistics (charging at a station / being carried): counts at its home pad
                if (b.homePad is EnergyPad hp && hp != b.pad)
                {
                    int idx = pads.IndexOf(hp);
                    if (idx >= 0) current[idx]++;
                }
                continue;
            }
            if (b.pad == null) { spares.Add(b); continue; }              // loose on the ground
            if (b.pad is BatteryStation
                && (b.energy >= b.maxEnergy - 1e-3f || b.ChargedToday || !chargingPossible))
                spares.Add(b);                                            // stock with nothing left to gain
        }

        // CARRIED batteries are deactivated (gone from Battery.all) — count them at their home
        // pad too, or the fleet double-serves a hub whose battery is mid-swap on a drone.
        // Bag-batched batteries (several riding one bag drone) count the same way.
        for (int k = 0; k < AllyAI.allies.Count; k++)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            var cb = d.Carried;
            if (cb != null && cb.hasHome && cb.homePad != null)
            {
                int idx = pads.IndexOf(cb.homePad);
                if (idx >= 0) current[idx]++;
            }
            var batch = d.BatchCarried;
            for (int j = 0; j < batch.Count; j++)
            {
                var bb = batch[j];
                if (bb == null || !bb.hasHome || bb.homePad == null) continue;
                int idx = pads.IndexOf(bb.homePad);
                if (idx >= 0) current[idx]++;
            }
        }

        int pool = spares.Count;
        for (int i = 0; i < current.Count; i++) pool += current[i];

        // ---- targets: pins off the top, then a battery for every dock pad, then D'Hondt ----
        int remaining = pool;
        for (int i = 0; i < pads.Count; i++)
        {
            target.Add(Mathf.Min(pads[i].pinnedMin, pads[i].SlotCapacity));
            remaining -= target[i];
        }
        // the pad keeping the DOCK alive is guaranteed a battery before ordinary pads see one —
        // an unpowered dock stops recharging drones and stalls the colony
        for (int i = 0; i < pads.Count && remaining > 0; i++)
        {
            if (!feedsDock[i] || consumers[i] <= 0) continue;
            if (target[i] >= 1 || pads[i].SlotCapacity < 1) continue;
            target[i] = 1;
            remaining--;
        }
        while (remaining > 0)
        {
            int bestI = -1;
            float bestQ = 0f;
            for (int i = 0; i < pads.Count; i++)
            {
                if (consumers[i] <= 0 || target[i] >= pads[i].SlotCapacity) continue;
                float q = consumers[i] / (float)(target[i] + 1);
                if (q > bestQ || (q == bestQ && bestI >= 0 && consumers[i] > consumers[bestI]))
                { bestQ = q; bestI = i; }
            }
            if (bestI < 0) break;   // every powered pad is at capacity
            target[bestI]++;
            remaining--;
        }

        // ---- the move: pins first (the player's hand), then an empty dock pad (the colony's
        // lifeline), then the biggest ordinary deficit ----
        int destI = -1, bestDef = 0, bestTier = int.MaxValue;
        for (int i = 0; i < pads.Count; i++)
        {
            int def = target[i] - current[i];
            if (def <= 0) continue;
            int tier = current[i] < Mathf.Min(pads[i].pinnedMin, pads[i].SlotCapacity) ? 0
                : feedsDock[i] && current[i] < 1 ? 1
                : 2;
            if (tier < bestTier || (tier == bestTier && def > bestDef))
            { bestTier = tier; bestDef = def; destI = i; }
        }
        if (destI < 0) { lastEmptyAt = Time.time; return null; }
        dest = pads[destI];

        // ---- the battery: fullest spare, else unpinned surplus off an overstocked pad ----
        Battery pick = null;
        for (int k = 0; k < spares.Count; k++)
            if (pick == null || spares[k].energy > pick.energy) pick = spares[k];
        if (pick == null)
        {
            for (int j = 0; j < pads.Count && pick == null; j++)
            {
                if (current[j] <= target[j]) continue;   // target already covers the pin
                var srcPad = pads[j];
                for (int s = 0; s < srcPad.slots.Length; s++)
                {
                    var b = srcPad.slots[s];
                    if (b == null || b.following || b == Battery.held || b.IsPulse) continue;
                    if (b.claimedBy != null && b.claimedBy != forDrone) continue;
                    if (pick == null || b.energy > pick.energy) pick = b;
                }
            }
        }
        if (pick == null) { lastEmptyAt = Time.time; dest = null; return null; }
        return pick;
    }

    /// <summary>Meaningful gain gate for a wave-end pad upgrade: a spare must beat the slotted
    /// battery by this much before a drone trades them (churn guard — the station swap's
    /// tighter BatteryStation.SwapMargin is for trips that also charge). Like every swap
    /// guard it scales down via BatteryStation.MarginOver as the slotted battery dies:
    /// against a dead pad even dregs are a real upgrade.</summary>
    public const float UpgradeMargin = 1.5f;

    /// <summary>Wave-end pad UPGRADE, the stationless swap: the drained battery on a working
    /// pad most in need (returned), plus the fullest free-floating <paramref name="spare"/>
    /// that beats it by <see cref="UpgradeMargin"/>. Free-floating = loose with no home claim —
    /// the scene's game-start batteries and player drops, exactly the stock the station economy
    /// doesn't own. Works with no station built; when one stands the charge run usually gets to
    /// a drained pad battery first (and charges it) — this catches what stations can't serve
    /// (no qualifying stock, or the once-a-day charge already spent). One trade per pad battery
    /// per swap window, the same day-tick/homecoming cadence as station trips. Claims nothing —
    /// the drone claims BOTH batteries.</summary>
    public static Battery FindUpgradeSwap(Drone forDrone, out Battery spare)
    {
        spare = null;
        int window = BatteryStation.SwapWindow;
        // the fullest free spare — no spare, no trade (below the dreg floor it upgrades nothing)
        Battery best = null;
        for (int k = 0; k < Battery.all.Count; k++)
        {
            var b = Battery.all[k];
            if (b == null || b.transform.InDungeon() || b.following || b == Battery.held) continue;
            if (b.IsPulse || b.pad != null || b.hasHome) continue;
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (!PathZone.AtBase(b.transform.position)) continue;
            if (best == null || b.energy > best.energy) best = b;
        }
        if (best == null || best.energy <= BatteryStation.DregMargin) return null;
        // the neediest working-pad battery it meaningfully beats — full margin against a live
        // battery, the dreg floor against a dead one (2.4 onto a 0-energy pad is a real gain)
        Battery outB = null;
        for (int k = 0; k < Battery.all.Count; k++)
        {
            var b = Battery.all[k];
            if (b == null || b.transform.InDungeon() || b.following || b == Battery.held) continue;
            if (b.IsPulse || b.pad == null || b.pad is BatteryStation || b.pad is CapacitorNode) continue;
            if (!b.pad.builtYet || !b.pad.enabled || !b.pad.gameObject.activeInHierarchy) continue;
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (b.padSwapWindow == window) continue;          // already served this window
            if (best.energy < b.energy + BatteryStation.MarginOver(b, UpgradeMargin)) continue;
            if (outB == null || b.energy < outB.energy) outB = b;
        }
        if (outB == null) return null;
        spare = best;
        return outB;
    }

    /// <summary>How many buildings actually draw from this pad: its presence in their resolved
    /// BuildingPower sources. Read-only — PowerOrNull never materialises a power view.
    /// <paramref name="feedsDock"/> flags a DroneDock among them (the priority consumer).</summary>
    static int CountConsumers(EnergyPad p, out bool feedsDock)
    {
        int n = 0;
        feedsDock = false;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            Building b = list[k];
            if (b == null || ReferenceEquals(b, p) || !b.gameObject.activeInHierarchy) continue;
            // A pylon CABLED onto the pad draws from it exactly like an adjacent building —
            // stock it the same way (counted once even if the pylon is also touching).
            if (b is EnergyPylon pyl && pyl.DrawsFromViaCable(p)) { n++; continue; }
            var pw = b.PowerOrNull;
            if (pw == null) continue;
            var srcs = pw.Sources;
            for (int s = 0; s < srcs.Count; s++)
                if (ReferenceEquals(srcs[s], p)) { n++; if (b is DroneDock) feedsDock = true; break; }
        }
        return n;
    }
}
