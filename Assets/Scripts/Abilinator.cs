using System.Linq;
using UnityEngine;

public class Abilinator : Combinator
{
    // public int level = 0;
    // public override void OnClick()
    // {
    //     if(!enabled)
    //     {
    //         return;
    //     }
    //     ResetTiles();
    //     foreach (Blueprint b in BlueprintManager.researched.Where(x =>
    //                  x.classifier == Blueprint.Classifier.Inner_Manifestor))
    //     {
    //         if(b.Typ() == "Ability")
    //         {
    //             if (Blueprint.ns[b.name] > 1)
    //             {
    //                 Debug.Log('a');
    //                 AddSlot(new int[]{0,0,0,0},b.name,b.s,true,() =>
    //                 {
    //                     InstantAct(b);
    //                     upgradeBP = b.relevents[level];
    //                 });
    //             }
    //         }
    //     }
    //     base.OnClick();
    // }
}
