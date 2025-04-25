using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Strafer : Part
{
   [SerializeField] private Accelerator[] accel; //left & right
   public override void StartPart(MechaSuit mecha)
   {
      base.StartPart(mecha);
      Accelerator.accels.Add(accel[0]);
      Accelerator.accels.Add(accel[1]);
   }
   public override void StopPart(MechaSuit mecha)
   {
      base.StopPart(mecha);
      Accelerator.accels.Remove(accel[0]);
      Accelerator.accels.Remove(accel[1]);
   }

   private void Update()
   {
      engagement = accel[0].on || accel[1].on ? 1f : 0f;
   }
}
