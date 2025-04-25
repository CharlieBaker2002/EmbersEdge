using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class D0_5 : Room, IRoom
{
    public Spin spin;
    public GameObject spikes;

    public override void OnDefeat()
    {
        base.OnDefeat();
        spin.StartCoroutine(spin.StopSpinning());
        Destroy(spikes);
    }
}
