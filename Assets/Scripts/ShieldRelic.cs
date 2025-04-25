using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ShieldRelic : Part
{
    Action<float> damageDel;
    public GameObject shieldPrefab1;
    public GameObject shieldPrefab2;
    public GameObject shieldPrefab3;
    private float timer = 0f;
    [SerializeField] float energyCost;

    private void Awake()
    {
        damageDel = (float dmgP) =>
        {
            if (timer <= 0f && dmgP < 0)
            {
                GameObject shield;
                int r = Random.Range(0, 10);
                if (r == 0)
                {
                    if (!ResourceManager.instance.ChangeFuels(-energyCost))
                    {
                        return;
                    }
                    shield = shieldPrefab3;
                }
                else if (r < 4)
                {
                    if (!ResourceManager.instance.ChangeFuels(-energyCost))
                    {
                        return;
                    }
                    shield = shieldPrefab2;
                }
                else
                {
                    if (!ResourceManager.instance.ChangeFuels(-energyCost))
                    {
                        return;
                    }
                    shield = shieldPrefab1;
                }
                Instantiate(shield, transform.position, Quaternion.identity, transform.parent);
                timer = 15f;
                engagement = 0f;
            }
        };
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            engagement = 1f;
        }
    }

    public override void StartPart(MechaSuit m)
    {
        CharacterScript.CS.GetComponent<LifeScript>().onDamageDelegate += damageDel;
    }

    public override void StopPart(MechaSuit m)
    {
        CharacterScript.CS.GetComponent<LifeScript>().onDamageDelegate -= damageDel;
    }
}
