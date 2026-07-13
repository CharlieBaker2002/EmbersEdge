using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EmberStore : MonoBehaviour
{
   /// <summary>
   /// EMBER — the dungeon-harvest currency (NOT orbs). Collected from enemies by the EmberTether:
   /// fully clearing a tethered pocket sends its ember home. Total-earned tally; the actual ember
   /// lands in the base's Ember Store network (see EnergyManager.DepositDungeonEmber).
   /// </summary>
   public static int ember;

   /// <summary>Fired on each delivery (the amount just banked), for UI/FX to hook.</summary>
   public static Action<int> OnEmberHome;

   /// <summary>
   /// Ember riding home with the player: pocket completions HOLD their ember here instead of banking
   /// instantly, and the return teleport pays it out — each unit flies visibly from the arrival point
   /// into the building that wants it (PortalScript.PayOutPendingEmber → <see cref="Deliver"/>).
   /// </summary>
   public static int pending;

   [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
   static void ResetStatics()   // no-domain-reload: statics survive play-stop unless reset here
   {
      ember = 0;
      pending = 0;
   }

   public static void Hold(int n)
   {
      if (n > 0) pending += n;
   }

   /// <summary>Claim everything held for the ride home (called once by the payout).</summary>
   public static int TakePending()
   {
      int n = pending;
      pending = 0;
      return n;
   }

   public static void Bank(int n)
   {
      if (n <= 0) return;
      ember += n;
      OnEmberHome?.Invoke(n);
      if (EnergyManager.i != null) EnergyManager.i.DepositDungeonEmber(n);
   }

   /// <summary>
   /// One animated payout ember landed on a building: tally it and deposit it straight into that
   /// connector. Stores flash their impact statics from the arrival direction, the same way an
   /// expander hit does. A target destroyed mid-flight falls back to the plain bank so the ember
   /// is never lost.
   /// Deliberately NO UpdateEmber here: the payout flights already land each ember where demand
   /// wants it, and the network can't see the embers still in flight — a per-arrival rebalance
   /// reads every landing as an imbalance and sets stores shuttling ember back and forth through
   /// the cables. The next naturally-triggered UpdateEmber (constructor spend, generator burn,
   /// expander collection) settles any genuine leftover drift.
   /// </summary>
   public static void Deliver(EmberConnector c, Vector3 from)
   {
      ember += 1;
      OnEmberHome?.Invoke(1);
      if (c == null)
      {
         if (EnergyManager.i != null) EnergyManager.i.DepositDungeonEmber(1);
         return;
      }
      c.ember++;
      c.onRefresh?.Invoke();
      EmberStoreBuilding store = c.GetComponentInParent<EmberStoreBuilding>();
      if (store != null) store.Hit(from - store.transform.position);
   }
}
