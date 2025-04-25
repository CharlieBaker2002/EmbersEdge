using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public class DefensePart : Part
{
  public string typp = "hp";
   public static List<SpriteRenderer> hps;
   public static List<SpriteRenderer> shields;
   public static List<SpriteRenderer> shieldRegens;
   private float val = 0f;
   [SerializeField] private bool cheatStart = false;
   public override void StartPart(MechaSuit mecha)
   {
      if (typp == "hp")
      {
         if (cheatStart)
         {
            val = 9f;
            CharacterScript.CS.ls.maxHp += 9f;
            return;
         }
         val = 2.5f;
         CharacterScript.CS.ls.maxHp += 2.5f;
         GS.Stat(CharacterScript.CS,"heal",2.5f,2.5f);
         hps.Add(sr);
      }
      else if (typp == "shield")
      {
         CharacterScript.CS.latentShield.UpdateMax(1f);
         shields.Add(sr);
      }
      else if (typp == "shieldRegen")
      {
         shieldRegens.Add(sr);
         val = 0.4f;
         CharacterScript.CS.latentShield.rate += val;
      }
   }

   public override void StopPart(MechaSuit m)
   {
      if (typp == "hp")
      {
         CharacterScript.CS.ls.maxHp -= val;
         hps.Remove(sr);
      }
      else if (typp == "shield")
      {
         CharacterScript.CS.latentShield.UpdateMax(-1f);
         shields.Remove(sr);
      }
      else if (typp == "shieldRegen")
      {
         shieldRegens.Remove(sr);
         CharacterScript.CS.latentShield.rate -= val;
      }
   }
}
