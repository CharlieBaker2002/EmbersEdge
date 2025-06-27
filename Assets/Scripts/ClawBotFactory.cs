using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClawBotFactory : UnitBuilding
{
    public Sprite[] upgradeSprite;
    public SpriteRenderer[] slots;
    public Sprite[] slotSprites;
    public Sprite[] extraPartSprites;
    [SerializeField] ClawBotClaw[] claws;
    float healTimer = 0f;
    float clawTime = -1000f;
    bool big = false;
    [SerializeField] ClawBotClaw claw;
    [SerializeField] Transform[] points;
    [SerializeField] Transform[] rotators;
    [SerializeField] SpriteRenderer[] arms;
    [SerializeField] private Battery b;

    public override void Start()
    {
        base.Start();
        arms[0].enabled = true;
        arms[1].enabled = true;
        MakeClaw(0);
        clawTime = -100f;
        MakeClaw(1);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Rotate Left Arm", upgradeSprite[4], false, delegate { Rotate(0); });
        AddSlot(new int[] { 0, 0, 0, 0 }, "Rotate Right Arm", upgradeSprite[3], false, delegate { Rotate(1); });

        AddUpgradeSlot(new int[] { 25, 5, 1, 0 }, "Claw Ejection", upgradeSprite[2], true, delegate
        {
            cost[0] += 10;
            unit.GetComponent<ClawBot>().canShoot = true;
            unit.GetComponent<ClawBot>().extraParts[2].sprite = extraPartSprites[3];
            slots[2].sprite = slotSprites[2];
            canOpen = true;
        }, 7,true,delegate { canOpen = false; Shut(); });

        AddUpgradeSlot(new int[] { 0, 0, 3, 0 }, "Bigger Claws", upgradeSprite[0], true, delegate
        {
            big = true;
            cost[2] += 1;
            unit.GetComponent<ClawBot>().big = true;
            unit.GetComponent<ClawBot>().extraParts[0].sprite = extraPartSprites[1];
            unit.GetComponent<SpriteRenderer>().sprite = extraPartSprites[0];
            slots[0].sprite = slotSprites[0];
            canOpen = true;
            unitSprite = extraPartSprites[0];
            if (claws[0] != null)
            {
                Destroy(claws[0].gameObject);
            }
            if (claws[1] != null)
            {
                Destroy(claws[1].gameObject);
            }
            claws = new ClawBotClaw[] { null, null };
            clawTime = -100f;
            MakeClaw(0);
            clawTime = -100f;
            MakeClaw(1);
        }, 6,true,delegate { canOpen = false; Shut(); });

        AddUpgradeSlot(new int[] { 0, 0, 3, 1 }, "Dash And Slash", upgradeSprite[1], true, delegate
        {
            cost[0] += 10;
            unit.GetComponent<ClawBot>().canDash = true;
            unit.GetComponent<ClawBot>().extraParts[1].sprite = extraPartSprites[2];
            canOpen = true;
            slots[1].sprite = slotSprites[1];
        }, 8,true,delegate { canOpen = false; Shut(); });

       
    }

    public void Rotate(int index)
    {
        rotators[index].transform.rotation = Quaternion.Euler(0f, 0f, rotators[index].transform.rotation.eulerAngles.z - 30f);
    }

    public void Update()
    {
        healTimer -= Time.deltaTime;
        if (healTimer <= 0f)
        {
            healTimer += big ? 1.5f : 3f;
            if (claws[0] != null)
            {
                claws[0].GetComponent<LifeScript>().Change(1, -1);
            }
            if (claws[1] != null)
            {
                claws[1].GetComponent<LifeScript>().Change(1, -1);
            }
            points[0].transform.localPosition = new Vector3(0f, 0.15f, 0f);
            points[1].transform.localPosition = new Vector3(0f, 0.15f, 0f);
            int rand = Random.Range(0, 2);
            MakeClaw(rand);
            MakeClaw(rand + 1 - 2 * rand);
        }
    }

    void MakeClaw(int index)
    {
        if(Time.time < clawTime + 10)
        {
            return;
        }
        if (claws[index] == null)
        {
            claws[index] = Instantiate(claw, points[index]);
            claws[index].transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(Vector3.zero));
            clawTime = Time.time;
            if (index == 0)
            {
                claws[index].GetComponent<SpriteRenderer>().flipX = true;
            }
            if (big)
            {
                claws[index].ls.maxHp = 14;
            }
            claws[index].ls.hp = 0.000001f;
            claws[index].ls.Change(claws[index].ls.maxHp / 2f, -1);
        }
    }
}
