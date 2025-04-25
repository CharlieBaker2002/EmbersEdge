using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Phasor : Part
{
   public static List<Phasor> phasors;
   public static List<Phasor> mitigators;
   [SerializeField] ParticleSystem fx;
   [SerializeField] bool mitigator = false;
   [SerializeField] Sprite offSprite;
   [SerializeField] Sprite onSprite;
   [SerializeField] Sprite[] spriteAnim;
   private bool awake = true;
   
   public override void StartPart(MechaSuit mecha)
   {
      base.StartPart(mecha);
      if (mitigator)
      {
         mitigators.Add(this);
      }
      else
      {
         phasors.Add(this);
      }
      
   }
   
   public override void StopPart(MechaSuit mecha)
   {
      base.StopPart(mecha);
      if (mitigator)
      {
         mitigators.Remove(this);
      }
      else
      {
         phasors.Remove(this);
      }
   }

   public static void ActivatePhasors(int ind)
   {
      if (phasors.Count > ind)
      {
         phasors[ind].Activate();
      }
      if(mitigators.Count > ind)
      {
         mitigators[ind].Activate();
      }
   }

   public void Reawaken()
   {
      awake = true;
      engagement = 1f;
      sr.sprite = onSprite;
   }

   void Activate()
   {
      if (!mitigator)
      {
         GS.Stat(CharacterScript.CS, "immaterial", 1.25f);
      }
      else
      {
         GS.Stat(CharacterScript.CS, "immaterial", 1.25f);
         GS.Stat(CharacterScript.CS, "reflect", 1.25f,1f);
         GS.Stat(CharacterScript.CS, "invulnerable", 1.25f);
      }
      awake = false;
      StartCoroutine(Animate());
      fx.Play();
   }

   private IEnumerator Animate()
   {
      for(float t= 0f; t < 1f; t += Time.deltaTime)
      {
         if (awake)
         {
            fx.Stop();
            yield break;
         }
         sr.sprite = GS.PercentParameter(spriteAnim, t);
         yield return null;
      }
      if (awake)
      {
         fx.Stop();
         yield break;
      }
      sr.sprite = offSprite;
      engagement = 0f;
      fx.Stop();
   }

   public override bool CanAddThisPart()
   {
      if (mitigator)
      {
         if(DashPump.pumps.Count < mitigators.Count)
         {
            return true;
         }
      }
      else
      {
         if(DashPump.pumps.Count < phasors.Count)
         {
            return true;
         }
      }
      return false;
   }
}
