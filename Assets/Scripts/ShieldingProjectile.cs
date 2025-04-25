using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShieldingProjectile : ProjectileScript
{
   public ShieldUtility.ShieldType typ;

   [SerializeField] float amount = 1f;
   [SerializeField] bool weak = false;
   [SerializeField] float duration = 3f;
   
   [SerializeField] float decayInTime;
   [SerializeField] float decayOutTime;

   private bool charOnly;
   
   public override void OnCollide(Collision2D coli)
   {
      if (coli.rigidbody != null )
      {
         if (coli.rigidbody.transform == father)
         {
            return;
         }
      }
      if (coli.rigidbody.CompareTag(tag))
      {
         var u = coli.rigidbody.GetComponent<Unit>();
         base.OnCollide(coli);
         
         switch (typ)
         {
            case ShieldUtility.ShieldType.Shield:
               ShieldUtility.Shield(u, amount, duration, weak);
               break;
            case ShieldUtility.ShieldType.DecayingShield:
               ShieldUtility.DecayingShield(u, amount, duration, decayOutTime, weak);
               break;
            case ShieldUtility.ShieldType.DecayInDecayingShield:
               ShieldUtility.DecayInDecayingShield(u, amount, duration, decayInTime, decayOutTime, weak);
               break;
            case ShieldUtility.ShieldType.DecayInShield:
               ShieldUtility.DecayInShield(u, amount, duration, decayInTime, weak);
               break;
         }
      }
      else
      {
         base.OnCollide(coli);
      }
   }
}
