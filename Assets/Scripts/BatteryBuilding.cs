using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class BatteryBuilding : Building
{
   [SerializeField] Sprite[] sprs;
   public float maxLight;
   [SerializeField] Light2D l;
   [SerializeField] Battery b;

   public override void Start()
   {
      base.Start();
      b.act += UpdateSprite;
   }
   private void UpdateSprite(float energy)
   {
      sr.sprite = GS.PercentParameter(sprs, 1f - b.energy / b.maxEnergy);
      l.intensity = maxLight * (b.energy / b.maxEnergy);
   }
}
