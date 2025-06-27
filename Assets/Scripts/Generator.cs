using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Generator : Building
{
   public enum Taip
   {
      Ember,
      Solar,
      Pulse,
      White,
      Blue,
      Soul
   }

   [SerializeField] Battery b;
   [SerializeField] Sprite[] sprs;
   [SerializeField] Taip typ;
   private float genQuantity = 0;
   [SerializeField] float actionTimer = -1f;
   
   [SerializeField] Sprite increaseSprite;
   [SerializeField] Sprite decreaseSprite;

   public int limit;
   public int current;

   private Action act;
   [SerializeField] private GameObject FX;
   private bool finishedBurnFlag = true;
   
   public void Animate()
   {
      sr.LeanAnimateFPS(sprs, 3, true).setOnComplete(Generate);
   }

   private void Generate()
   {
      b.Add( genQuantity);
      Instantiate(FX, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx));
      genQuantity = 0f;
      finishedBurnFlag = true;
   }

   private void Update()
   {
      if(actionTimer < 0f) return;
      actionTimer -= Time.deltaTime;
      if (actionTimer <= 0f)
      {
         actionTimer = -1f;
         Animate();
      }
   }

   public override void Start()
   {
      base.Start();
      switch (typ)
      {
         case Taip.Pulse:
           act = () => SetTimer(1f, 1f);
           break;
         case Taip.Solar:
            act = () =>
            {
               float amount = GS.season switch
               {
                  0 => 4f,
                  1 => 5f,
                  2 => 1f,
                  _ => 2f
               };
               SetTimer(0.5f, amount);
            };
            break;
         case Taip.Ember:
            act = Demand;
            break;
         default: //white,blue
            AddSlot(new int[4], "Reduce Cap", decreaseSprite, false,Reduce);
            AddSlot(new int[4], "Increase Cap", increaseSprite, false,Increase);
            act = Demand;
            break;
      }
   }

   private void Demand()
   {
      if(!finishedBurnFlag) return; //if still waiting on previous burn
      finishedBurnFlag = false;
      switch (typ)
      {
         case Taip.Ember:
            Upgrade(() => SetTimer(0.1f,current*6f),current);
            break;
         case Taip.White:
            ResourceManager.instance.NewTask(gameObject, new int[] { current, 0, 0, 0 },
               () => SetTimer(0.1f, current/5f), false);
            break;
         case Taip.Blue:
            ResourceManager.instance.NewTask(gameObject, new int[] { 0, 0, current, 0},
               () => SetTimer(0.1f, current*10f), false);
            break;
      }
   }

   protected override void BEnable()
   {
      switch (typ)
      {
         case Taip.Pulse:
            EmbersEdge.EEExplodeEvent += act;
            break;
         default:
            SpawnManager.instance.onWaveComplete += act;
            break;
      }
      finishedBurnFlag = true;
   }
   
   protected override void BDisable()
   {
      switch (typ)
      {
         case Taip.Pulse:
            EmbersEdge.EEExplodeEvent -= act;
            break;
         default:
            SpawnManager.instance.onWaveComplete -= act;
            break;
      };
   }

   private void Reduce()
   {
      if (typ == Taip.White)
      {
         current -= 5;
      }
      else
      {
         current--;
      }
      if (current < 0)
      {
         current = 0;
         return;
      }
   }

   private void Increase()
   {
      if (typ == Taip.White)
      {
         current += 5;
      }
      else
      {
         current++;
      }
      if (current > limit)
      {
         current = limit;
         return;
      }
   }
   
   private void SetTimer(float time, float quantity)
   {
      actionTimer = time;
      genQuantity += quantity;
   }
}