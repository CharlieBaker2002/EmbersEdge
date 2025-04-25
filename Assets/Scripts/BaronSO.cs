using System.Collections;
using UnityEngine;

public class Baron : MonoBehaviour
{
   public B bar = B.Teacher; 
   public MechanismSO[] inits;
   public MechanismSO[] seconds;
   public MechanismSO[] thirds;
   
   public static Baron current; //set in BlueprintManager
   public static B choice; //for easy switch statmements
   
   public enum B
   {
      Teacher, //Tutorial
      Gluttony, //Tonnes of boosts (& heals), increased sight range.
      Greed, //Bare resources.
      Sloth, //super tanky, take your sweet time. Dematerialise Stat.
      Envy, //Leech Stat & Speed.
      Wrath, //Bare damage. Upgrade starting weapon immediately? Blue stat.
      Lust, //Charm Stat, & Units.
      Pride //basically super difficult mode - start with almost nothing. Get rewarded as you go.
   };
   
   //MechaSuit.AddParts();

   public IEnumerator Start()
   {
      yield return new WaitForSeconds(7.5f);
      One();
   }

   protected virtual void One()
   {
      
   }

   public virtual void Cleanup()
   {
      Destroy(gameObject,1f);
   }

   public virtual void Two()
   {
     // ConvertListToCore(ref seconds);
   }

   public virtual void Three()
   {
      //ConvertListToCore(ref thirds);
   }

   // private void ConvertListToCore(ref Blueprint[] bps)
   // {
   //    for (int i = 0; i < bps.Length; i++)
   //    {
   //       string nam = bps[i].name;
   //       bps[i] = Instantiate(bps[i]);
   //       bps[i].name = nam;
   //       if (bps[i].classifier == Blueprint.Classifier.Inner_Manifestor)
   //       {
   //          bps[i].classifier = Blueprint.Classifier.Core_Mechanism;
   //       }
   //       else if (bps[i].classifier == Blueprint.Classifier.Outer_Manifestor)
   //       {
   //          bps[i].classifier = Blueprint.Classifier.Outer_Mechanism;
   //       }
   //    }
   //}
}
