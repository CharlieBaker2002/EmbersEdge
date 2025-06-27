using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class A0_1Building : UnitBuilding
{
    public Sprite[] upgradeSprite;
    public SpriteRenderer[] slots;
    public Sprite[] slotSprites;
    private bool hasUpgradedMunitions = false;
    [SerializeField] private Battery b;

    public override void Start()
    {
        base.Start();
        AddUpgradeSlot(new int[] {25,10,0,0}, "More Bullets", upgradeSprite[0], true, delegate
        {
            cost[0] += 2;
            unit.GetComponent<A0_1>().shootN = 3;
            slots[0].sprite = slotSprites[0];
            canOpen = true;
        }, 4,true,delegate { hasUpgradedMunitions = true; Shut(); });

        AddUpgradeSlot(new int[] { 25, 10, 0, 0 }, "Improved Thruster", upgradeSprite[1], true,delegate
        {
            cost[0] += 3;
            unit.GetComponent<A0_1>().detectionRange = 9f;
            unit.GetComponent<A0_1>().shootSqrDist += 3f;
            unit.GetComponent<ActionScript>().maxVelocity = 3f;
            unit.GetComponent<AllyAI>().exploreRadius = 4f;
            unit.GetComponent<AllyAI>().resetTimer = 1.5f;
            canOpen = true;
            slots[1].sprite = slotSprites[1];
        }, 4,true,delegate { Shut(); });

        AddUpgradeSlot(new int[] { 80,0,3,0 }, "Piercer Projectile", upgradeSprite[2], true, delegate
        {
            cost[0] += 10;
            unit.GetComponent<A0_1>().shootN = 4;
            unit.GetComponent<AllyAI>().ydelta = 2;
            unit.GetComponent<A0_1>().shootSqrDist += 3f;
            slots[0].sprite = slotSprites[2];
            canOpen = true;
        },6, true,delegate { Shut(); }, null, HasUpgradeMunitions);

    }
    private bool HasUpgradeMunitions()
    {
        return hasUpgradedMunitions;
    }
}