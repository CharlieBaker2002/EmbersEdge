using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bonus : MonoBehaviour
{

    //set bp cost[0] to zero to reuse infinitely

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        switch (name)
        {
            //Resource
            case "Small White Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 15*GS.Era1(), 0, 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Small Green Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 5 * GS.Era1(), 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Small Blue Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 0, 1 * GS.Era1(), 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;

            case "White-Green Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 15 * GS.Era1(), 10, 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "White-Blue Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 15 * GS.Era1(), 0, 2, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Red Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 0, 0, 1 * GS.Era1() }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;

            case "Large White Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 100 * GS.Era1(), 0, 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Large Mixed Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 10 * GS.Era1(), 2, 1 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;

            case "Huge White Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 225 * 1 + GS.Era1(), 0, 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Huge Green Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 75 * GS.Era1(), 0, 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Huge Blue Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 0, 15 * GS.Era1(), 0 }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;
            case "Huge Red Orb Stash":
                GS.CallSpawnOrbs(transform.position, new int[] { 0, 0, 0, 5 * GS.Era1() }, DM.i.activeRoom.transform);
                RefreshManager.i.QA(() => GS.GatherResources(DM.i.activeRoom.transform),1.5f);
                break;

            //Temp Stats
            case "Engine Restock":
                ResourceManager.instance.ChangeFuels(1000);
                break;
            case "Energy Core Restock":
                ResourceManager.instance.AddCores(1);
                break;
            case "Small Heal":
                CharacterScript.CS.ls.Change(3,0);
                break;
            case "Weapon Restock":
                WeaponScript w = CharacterScript.CS.Weapon();
                w.totalAmmo += Mathf.RoundToInt(w.maximumAmmo / 2);
                w.totalAmmo = Mathf.Min(w.totalAmmo, w.maximumAmmo);
                w.ammoInClip = w.ammoPerClip;
                AmmoSlider.i.WeaponSliderResetClips(w.totalAmmo);
                AmmoSlider.i.UpdateSlider(w.ammoInClip);
                break;

            case "Medium Heal":
                CharacterScript.CS.ls.Change(9, 0);
                break;
            case "Weapons Half Restock":
                foreach(WeaponScript weapon in CharacterScript.CS.weapons)
                {
                    weapon.totalAmmo += Mathf.RoundToInt(weapon.maximumAmmo / 2);
                    weapon.totalAmmo = Mathf.Min(weapon.totalAmmo, weapon.maximumAmmo);
                }
                WeaponScript current = CharacterScript.CS.Weapon();
                current.ammoInClip = current.ammoPerClip;
                AmmoSlider.i.WeaponSliderResetClips(current.totalAmmo);
                AmmoSlider.i.UpdateSlider(current.ammoInClip);
                break;
            case "Heal And Engine Restock":
                ResourceManager.instance.ChangeFuels(1000);
                CharacterScript.CS.ls.Change(3, 0);
                ResourceManager.instance.AddCores(1);
                break;

            case "Large Heal":
                CharacterScript.CS.ls.Change(18, 0);
                break;
            case "Full Restock And Heal":
                foreach (WeaponScript weapon in CharacterScript.CS.weapons)
                {
                    weapon.totalAmmo = weapon.maximumAmmo;
                    weapon.ammoInClip = weapon.ammoPerClip;
                }
                WeaponScript current2 = CharacterScript.CS.Weapon();
                AmmoSlider.i.WeaponSliderResetClips(current2.totalAmmo);
                AmmoSlider.i.UpdateSlider(current2.ammoInClip);
                ResourceManager.instance.ChangeFuels(1000);
                ResourceManager.instance.AddCores(10);
                CharacterScript.CS.ls.Change(6,0);
                break;

            //Perma Stats, starting with uncommon
            case "Max Health +":
                CharacterScript.CS.ls.maxHp += 2;
                CharacterScript.CS.healthSlider.UpdateMax(CharacterScript.CS.ls.maxHp);
                CharacterScript.CS.ls.Change(0.0001f, -1);
                break;
            case "Max Fuel +":
                ResourceManager.instance.maxFuel += 2;
                ResourceManager.instance.fuelSlider.UpdateMax(ResourceManager.instance.maxFuel);
                break;

            case "Speed +":
                break;
            case "Max Energy Core +":
                //IMPLEMENT 
                break;
            case "Max Shield +":
                CharacterScript.CS.latentShield.UpdateMax(1.5f);
                break;
            case "Max Energy +":
                ResourceManager.instance.maxEnergy += 2;
                ResourceManager.instance.energySlider.UpdateMax(ResourceManager.instance.maxEnergy);
                break;

            case "Mass ++":
                foreach(Transform t in GS.FindParent(GS.Parent.buildings))
                {
                    if(t.TryGetComponent<MechaManifestor>(out var m))
                    {
                        //m.mech.suit.mass += 2;
                        Debug.LogWarning("NOT IMPLEMENTED");
                    }
                }
                CharacterScript.CS.AS.mass += 2;
                break;
            case "Max Shield ++":
                CharacterScript.CS.latentShield.UpdateMax(5f);
                break;
            case "Max Health ++":
                CharacterScript.CS.ls.maxHp += 12;
                CharacterScript.CS.healthSlider.UpdateMax(CharacterScript.CS.ls.maxHp);
                break;
            case "Max Fuel And Energy ++":
                ResourceManager.instance.maxEnergy += 4;
                ResourceManager.instance.energySlider.UpdateMax(ResourceManager.instance.maxEnergy);
                ResourceManager.instance.maxFuel += 4;
                ResourceManager.instance.fuelSlider.UpdateMax(ResourceManager.instance.maxFuel);
                break;

            //Utility

            case "White Orb Recall":
                ResourceManager.instance.DropResources(0);
                break;
            case "Green Orb Recall":
                ResourceManager.instance.DropResources(1);
                break;
            case "Blue Orb Recall":
                ResourceManager.instance.DropResources(2);
                break;
            case "Red Orb Recall":
                ResourceManager.instance.DropResources(3);
                break;

            case "Blueprint Recall":
                foreach(Blueprint bp in BlueprintManager.held)
                {
                    BlueprintManager.stashed.Add(bp);
                }
                BlueprintManager.held.Clear();
                break;
            case "All Orbs Recall":
                ResourceManager.instance.DropResources();
                break;

            case "Full Recall And Orb Duplicate":
                int[] orbHeldCurrent = new int[4];
                GS.CopyArray(ref orbHeldCurrent, ResourceManager.instance.held);
                ResourceManager.instance.DropResources();
                GS.CallSpawnOrbs(transform.position, orbHeldCurrent);
                break;
            default:
                throw new System.Exception("Incorrectly named bonus");
        }
        Destroy(gameObject);
    }
}
