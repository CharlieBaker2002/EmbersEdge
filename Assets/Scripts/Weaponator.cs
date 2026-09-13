using System.Linq;
using UnityEngine;
public class Weaponator : Combinator
{
    public int level = 0;
    public override void OnClick()
    {
        if(!enabled)
        {
            return;
        }
        ResetTiles();
        foreach (Blueprint b in BlueprintManager.researched.Where(x => x is MechanismSO))
        {
            MechanismSO z = (MechanismSO)b;
            if (z.p.taip != Part.PartType.Weapon) continue;
            if (z.relevents == null || z.relevents.Count <= level) continue;   // no upgrade line (Hoover) — nothing to combine into
            AddSlot(new int[]{0,0,0,0},z.name,z.s,true,() =>
            {
                InstantAct(b);
                upgradeBP = (MechanismSO)z.relevents[level];
            });
        }
        base.OnClick();
    }
}