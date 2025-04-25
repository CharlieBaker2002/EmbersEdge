using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class Laser : Spell
{
    [SerializeField] DamageBoundary laser;
    float atr;
    [SerializeField] Sprite[] indicators;
    

    public override void Performed(InputAction.CallbackContext ctx)
    {
        
        GS.QA(this,() => Attack(),Mathf.Max(0f, 0.5f * (1 - ((float)ctx.duration))));
        this.QA(() =>base.Performed(ctx),1.5f);
    }

    private void Attack()
    {
        DamageBoundary db = Instantiate(laser,transform.position,transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
        db.damage = 1.5f * level * (1 + atr);
        db.damageOverT = 1.5f * level;
        db.dps = 1.5f * Mathf.Pow(2,level);
        db.GetComponent<Animator>().SetInteger("lvl", level - 1);
    }

    public override void LevelUp()
    {
        level++;
        GetComponentInChildren<AbilityRangeIndicator>().GetComponent<SpriteRenderer>().sprite = indicators[level - 2];
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(2 + level, 3);
    }

    public override void Intellect(float i)
    {
        atr = i;
    }
}
