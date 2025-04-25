using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class D2_0Room : Room, IRoom
{
    public Animator[] anims;

    public override void ResetRoom()
    {
        base.ResetRoom();
        if (!defeated)
        {
            foreach (Animator a in anims)
            {
                a.Rebind();
                a.Update(0);
            }
        }
    }

    public override void OnEnter()
    {
        base.OnEnter();
        if (!defeated)
        {
            anims[0].SetBool("Start", true);
        }
    }
}
