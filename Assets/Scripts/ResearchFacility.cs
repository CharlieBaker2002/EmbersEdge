using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ResearchFacility : Building
{
    // [SerializeField]
    // Slider slid;
    // private float t;
    // private Blueprint b1;
    // private Blueprint b2;
    //
    // public override void Start()
    // {
    //     UIParent = ResearchUI.i.gameObject;
    //     buildings.Add(this);
    //     UIParent.SetActive(false);
    //     buildingBehaviours.Add(this);
    //     buildingBehaviours.Add(GetComponentInChildren<Collider2D>());
    //     buildingBehaviours.Add(ls);
    //     OnOpen += delegate { UIManager.CloseAllUIs(); UpdateUI(); UIParent.SetActive(true); CharacterScript.CS.GR = CharacterScript.CS.defaultGR; ResearchUI.i.facility = this; ResearchUI.i.SwapMode(0); };
    //     OnClose += delegate { UIParent.SetActive(false);};
    // }
    //
    // public void Do(float _t, int[] cost, Blueprint b, int mode, Blueprint ob = null)
    // {
    //     t = _t;
    //     b1 = b;
    //     b2 = ob;
    //     canOpen = false;
    //     if(mode == 0)
    //     {
    //         BlueprintManager.stashed.Remove(b);
    //         ResourceManager.instance.NewTask(gameObject, cost, ResearchBlueprint);
    //         return;
    //     }
    //     else
    //     {
    //         BlueprintManager.researched.Remove(b);
    //         BlueprintManager.researched.Remove(b2);
    //     }
    //     if(mode == 1)
    //     {
    //         RemoveWeaponUpgrades(BlueprintManager.toDiscover);
    //         RemoveWeaponUpgrades(BlueprintManager.held);
    //         RemoveWeaponUpgrades(BlueprintManager.stashed);
    //         RemoveWeaponUpgrades(BlueprintManager.researched);
    //         ResourceManager.instance.NewTask(gameObject, cost, UpgradeWeapon);
    //     }
    //     else
    //     {
    //         ResourceManager.instance.NewTask(gameObject, cost, UpgradeAbility);
    //     }
    // }
    //
    // private void RemoveWeaponUpgrades(List<Blueprint> check)
    // {
    //     List<Blueprint> bp = new List<Blueprint>();
    //     {
    //         foreach (Blueprint a in check)
    //         {
    //             if (a.Typ().Contains("Weapon Upgrade"))
    //             {
    //                 if (((WeaponBP)a).g == b1.g)
    //                 {
    //                     bp.Add(a);
    //                 }
    //             }
    //         }
    //     }
    //     foreach(Blueprint a in bp)
    //     {
    //         check.Remove(a);
    //     }
    // }
    //
    // private void ResearchBlueprint()
    // {
    //     StartCoroutine(ResearchBlueprintI());
    // }
    //
    // IEnumerator ResearchBlueprintI()
    // {
    //     //slid.maxValue = t;
    //     for(float i = t; t > 0f; t -= Time.deltaTime)
    //     {
    //         //slid.value = i;
    //         i -= Time.deltaTime;
    //         yield return null;
    //     }
    //     BlueprintManager.researched.Add(b1);
    //     ResearchUI.i.SwapMode();
    //     canOpen = true;
    // }
    //
    // private void UpgradeWeapon()
    // {
    //     StartCoroutine(WeaponBlueprintI());
    // }
    //
    // IEnumerator WeaponBlueprintI()
    // {
    //     //slid.maxValue = t;
    //     for (float i = t; t > 0f; t -= Time.deltaTime)
    //     {
    //         //slid.value = i;
    //         i -= Time.deltaTime;
    //         yield return null;
    //     }
    //     var weapon = (WeaponBP)b1;
    //     weapon.lvl = ((WeaponBP)b2).lvl;
    //     weapon.upgraded = true;
    //     weapon.s = b2.s;
    //     GS.CopyArray(ref weapon.cost, b2.cost);
    //     weapon.name = b2.name;
    //     weapon.description = b2.description;
    //     Destroy(b2);
    //     BlueprintManager.researched.Add(b1);
    //     ResearchUI.i.SwapMode();
    //     canOpen = true;
    // }
    //
    // private void UpgradeAbility()
    // {
    //     StartCoroutine(AbilityBlueprintI());
    // }
    //
    // IEnumerator AbilityBlueprintI()
    // {
    //     //slid.maxValue = t;
    //     for (float i = t; t > 0f; t -= Time.deltaTime)
    //     {
    //         //slid.value = i;
    //         i -= Time.deltaTime;
    //         yield return null;
    //     }
    //     var ab = (AbilityBP)b1;
    //     ab.LevelUp(b2);
    //     BlueprintManager.researched.Add(b1);
    //     //dont destroy b2, but it has been removed from researched!
    //     ResearchUI.i.SwapMode();
    //     canOpen = true;
    // }
}