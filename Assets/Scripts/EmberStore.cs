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

   public static void Bank(int n)
   {
      if (n <= 0) return;
      ember += n;
      OnEmberHome?.Invoke(n);
      if (EnergyManager.i != null) EnergyManager.i.DepositDungeonEmber(n);
   }
}
