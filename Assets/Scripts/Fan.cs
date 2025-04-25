using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fan : Part
{
   [Header("all positive values xox")]
   [SerializeField] private Sprite[] sprs;
   private float timer;
   public static List<Fan> fans = new();

   public override void StartPart(MechaSuit mecha)
   {
      fans.Add(this);
      ResetFans();
      
   }
   
   public override void StopPart(MechaSuit mecha)
   {
      base.StopPart(mecha);
      fans.Remove(this);
      ResetFans();
   }

   private void Update()
   {
      timer += 30f * engagement * Time.deltaTime;
      if (timer >= 11f) timer -= 11f;
      sr.sprite = GS.PercentParameter(sprs, timer / 11f);
   }

   public static void ResetFans()
   {
      CharacterScript.CS.turnSpeed = 270f;
      CharacterScript.CS.maxDashTimer = 9f;
      CharacterScript.CS.AS.turniness = 0.2f;
      for (int i = 0; i < fans.Count; i += 1)
      {
         CharacterScript.CS.maxDashTimer = Mathf.Lerp(CharacterScript.CS.maxDashTimer, 4f, 0.3f);
         CharacterScript.CS.AS.turniness = Mathf.Lerp(CharacterScript.CS.AS.turniness, 2f, 0.3f);
         CharacterScript.CS.turnSpeed += 180f;
      }
   }
}
