using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ElementalDronesSpell : Spell
{
    private int n = 3;
    float atr = 0;
    public GameObject[] drones;
    private int[] probabilities = new int[] {0,3};

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(2 + 2 * level, 20);
    }

    public override void Intellect(float intellect)
    {
        atr = intellect;
        n = Mathf.CeilToInt(3 * Mathf.Pow(2, level - 1) + atr);
    }

    public override void LevelUp()
    {
        level++;
        n = Mathf.CeilToInt(3 * Mathf.Pow(2, level - 1) +atr);
        if(level == 2)
        {
            probabilities = new int[] { 1, 2, 2};
        }
        if(level == 3)
        {
            probabilities = new int[] { 1,1,1,1 };
        }
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        float plus = Mathf.RoundToInt(0.25f*n*ResourceManager.instance.UseCores(1,2));
        for(int i = 0; i < (n + plus); i++)
        {
            int j = Random.Range(0, level + 1);
            while (Random.Range(0,4) < probabilities[j])
            {
                j = Random.Range(0, level + 1);
            }
            var d = Instantiate(drones[j], transform.position + level*(Vector3)Random.insideUnitCircle, Quaternion.Euler(0, 0, Random.Range(0, 360)), GS.FindParent(GS.Parent.allies));
            d.tag = tag;
        }
        LevelUp();
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        engagement = 1f;
    }
}
