using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProgressEEEventButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        Debug.Log("yo");
        GS.CallSpawnOrbs(Vector2.zero, new float[] { 100, 30, 10, 3 }, null, false);

        // +10 ember to the base, clamped to what the store network can actually hold —
        // Bank/DepositDungeonEmber fills capacity-first but overfills the default store with
        // any overflow ("dungeon ember is never lost"), which a cheat shouldn't do.
        if (EnergyManager.i == null) return;
        int room = 0;
        foreach (EmberStoreBuilding store in EnergyManager.i.emberStores)
        {
            if (store == null || store.connect == null) continue;
            room += Mathf.Max(0, store.connect.maxEmber - store.connect.ember);
        }
        int grant = Mathf.Min(10, room);
        if (grant > 0) EmberStore.Bank(grant);
    }
}
