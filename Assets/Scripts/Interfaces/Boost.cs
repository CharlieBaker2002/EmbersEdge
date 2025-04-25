using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Boost : Part
{
    public int ind;
    public Sprite s;
    private bool consumed = false;
    
    private void BaseConsume()
    {
        if (!consumed)
        {
            MechaSuit.boostsLeft++;
            UnityEngine.UI.Image img = CharacterScript.CS.potionImgs[ind];
            img.gameObject.SetActive(false);
            img.sprite = null;
            img.color = Color.clear;
            img.GetComponentInParent<UnityEngine.UI.Slider>().value = 0;
            CharacterScript.CS.pds[ind] = delegate { };
            CharacterScript.CS.boostBools[ind] = false;
            consumed = true;
        }
    }

    public virtual void Consume(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        BaseConsume();
    }
    
    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        if (sr == null)
        {
            sr = GetComponent<SpriteRenderer>();
        }
        ind = CharacterScript.CS.NewBoost(this);
      
    }

    public override void StopPart(MechaSuit m)
    {
        BaseConsume();
    }

    public override bool CanAddThisPart()
    {
        return CharacterScript.CS.boostBools.Any(x => x == false);
    }
}