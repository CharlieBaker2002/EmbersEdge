using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthShrine : Shrine
{
    public override void Trigger(Transform t)
    {
        if(active == true)
        {
            active = false;
            base.Trigger(t);
            var uni = t.GetComponent<Unit>();
            GS.Stat(uni,"Heal", 2 * GS.era + (5 - GS.era) * Random.Range(uni.ls.maxHp / 2f, uni.ls.maxHp) * 0.2f, 3f);
            Instantiate(FX, t.position, t.rotation, transform.parent);
        }
    }

}
