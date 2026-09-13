using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The "next day button" cheat: grants ember to the store network and drops a pile of
/// chip at the base for testing the chip economy (construction, Hoover, drones, Refiner).</summary>
public class ProgressEEEventButton : MonoBehaviour, IClickable
{
    [Tooltip("Ember banked per click (clamped to the store network's free room).")]
    public int emberGrant = 10;
    [Tooltip("Chips dropped per click beside the player (or at the scrap pile if the player is away from base).")]
    public int chipGrant = 10;
    [Tooltip("Ore intensity tier the dropped chips roll their size from (0 low / 1 mid / 2 high); base cap applies.")]
    [Range(0, 2)] public int chipTier = 1;

    public void OnClick()
    {
        // ember: clamped to what the store network can actually hold — Bank/DepositDungeonEmber
        // fills capacity-first but overfills the default store with any overflow ("dungeon ember
        // is never lost"), which a cheat shouldn't do.
        if (EnergyManager.i != null && emberGrant > 0)
        {
            int room = 0;
            foreach (EmberStoreBuilding store in EnergyManager.i.emberStores)
            {
                if (store == null || store.connect == null) continue;
                room += Mathf.Max(0, store.connect.maxEmber - store.connect.ember);
            }
            int grant = Mathf.Min(emberGrant, room);
            if (grant > 0) EmberStore.Bank(grant);
        }

        // chip: a pile beside the player at base (a little below, so it isn't under the suit),
        // else at the fleet's scrap point — sized by the tier blend with the base cap
        if (chipGrant > 0)
        {
            var cs = CharacterScript.CS;
            Vector3 at = cs != null && PathZone.AtBase(cs.transform.position)
                ? cs.transform.position + new Vector3(0f, -1.2f, 0f)
                : (Vector3)DroneManager.ScrapPoint;
            DroneManager.SpawnOreUnits(at, chipTier, chipGrant);
        }
    }
}
