using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Generator : Building, IEnergyAccumulator
{
   public enum Taip
   {
      Ember,
      Solar,
      Pulse,
      White,
      Blue,
   }

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

   // Ember generators join the ember CABLE network as a sink (like an Ember Store): the network
   // routes ember into `connect` up to maxEmber, and we burn held ember into the energy battery.
   public EmberConnector connect;
   private int queue;
   private bool coroutined = false;

   // Internal battery: capacity + draw rate + instabuffer (burst pool), sized per type in Start.
   // The instabuffer is what lets a consumer pull a whole shot in one frame instead of a trickle.
   private readonly EnergyStore store = new EnergyStore(10f, 2f, 2f);

   [Header("Internal Battery")]
   [Tooltip("When OFF, the fields below are filled from the per-type defaults at Start so you can see them. " +
            "When ON, the values below are used instead.")]
   [SerializeField] private bool overrideBattery = false;
   [Tooltip("Energy storage capacity.")]
   [SerializeField] private float batteryCapacity;
   [Tooltip("Sustained draw rate (energy/second).")]
   [SerializeField] private float batteryDrawRate;
   [Tooltip("Burst pool — lets a consumer pull a whole shot in one frame.")]
   [SerializeField] private float batteryInstabuffer;

   public float Energy    => store.Energy;
   public float MaxEnergy => store.MaxEnergy;
   public float DrawRate  => store.DrawRate;
   public float MaxDrawThisFrame(float dt) => store.MaxDrawThisFrame(dt);

   public event Action<float> OnUpdate;
   public event Action OnUse;

   public bool Use(float cost)
   {
      if (!store.Use(cost)) return false;
      OnUpdate?.Invoke(store.Energy);
      OnUse?.Invoke();
      return true;
   }

   public void Add(float amount)
   {
      if (amount <= 0f) return;
      store.Add(amount);
      OnUpdate?.Invoke(store.Energy);
   }

   public void Animate()
   {
      if (typ is Taip.Pulse)
      {
         if (!coroutined)
         {
            coroutined = true;
            StartCoroutine(AnimateI());
         }
      }
      else if (typ is Taip.Ember)
      {
         sr.LeanAnimateFPS(sprs, 6, true).setOnComplete(Generate);
      }
      else
      {
         sr.LeanAnimateFPS(sprs, 12, true);
         this.QA(Generate,sprs.Length*0.5f / 12f);
      }
   }

   IEnumerator AnimateI()
   {
      while (queue > 0)
      {
         queue--;
         sr.LeanAnimateFPS(sprs, 24, true).setOnComplete(Generate);
         yield return new WaitForSeconds(sprs.Length / 24f);
         yield return new WaitForSeconds(0.1f);
      }
      coroutined = false;
   }

   private void Generate()
   {
      Add(genQuantity);
      if (FX != null) Instantiate(FX, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx));
      genQuantity = 0f;
   }

   private void Update()
   {
      store.Tick(Time.deltaTime);   // reconcile the instabuffer every frame, even when idle
      BurnEmber();
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
      // Build the ember-network connector BEFORE base.Start() — a pre-built generator's
      // base.Start() calls BEnable (which registers `connect`), so it must already exist.
      if (typ == Taip.Ember)
      {
         connect = gameObject.AddComponent<EmberConnector>();
         connect.taip    = EmberConnector.typ.Store;            // a sink: sources route to it, it links to stores
         connect.maxEmber = name.StartsWith("Small") ? 1 : 3;   // embers held (Small 1, Large 3)
         connect.cables                  = new List<EmberCable>();
         connect.connections             = new List<EmberConnector>();
         connect.cableConnectionDirections = new List<bool>();
      }

      base.Start();

      // Internal battery sized per generator type (code-authoritative — the prefabs don't
      // serialise these). (capacity, rate, instabuffer): capacity ≈ 5s of full-rate draw;
      // rate scales with output tier (≈ the pylon's 4 e/s per-cable cap); instabuffer ≈ rate
      // (capped ~4) so a consumer can pull a full shot's cost in one frame.
      (float cap, float rate, float insta) = typ switch
      {
         Taip.Ember => name.StartsWith("Small") ? (15, 3, 1f) : (45f, 10f, 1f),
         Taip.Solar => (20f, 1f, 0f),
         Taip.Pulse => (20f, 20f, 20f),
         Taip.White => (30f, 2f, 2f),
         Taip.Blue  => (80f, 8f, 4f),
         _          => (10f, 2f, 2f),
      };
      if (overrideBattery)
      {
         cap = batteryCapacity; rate = batteryDrawRate; insta = batteryInstabuffer;
      }
      else
      {
         // Mirror the per-type defaults into the serialized fields so they're visible in the inspector.
         batteryCapacity = cap; batteryDrawRate = rate; batteryInstabuffer = insta;
      }
      store.Configure(cap, rate, insta);

      switch (typ)
      {
         case Taip.Pulse:
           act = () =>
           {
              queue++;
              SetTimer(1f, 1f);
           };
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
         case Taip.White:
         case Taip.Blue:
            // `current` is the maintain LEVEL (White 1..3, Blue 1..4). It sets both the energy
            // floor (White current*10, Blue current*20) and the resources burnt to reach it
            // (White current×5, Blue current×1). Defaults: White 2/3, Blue 3/4.
            limit   = typ == Taip.White ? 3 : 4;
            current = typ == Taip.White ? 2 : 3;
            AddSlot(new int[4], "Lower Level",  decreaseSprite, false, Reduce);
            AddSlot(new int[4], "Raise Level",  increaseSprite, false, Increase);
            act = MaintainBurn;
            break;
         case Taip.Ember:
            // Refilled by the ember cable network (connector set up above); burns in BurnEmber().
            break;
      }
   }

   private const float emberEnergy = 15f;   // energy per ember burned (Small holds 1, Large holds 3 = cap)

   /// <summary>
   /// Ember generators burn a held ember (from the cable network) into the battery when there's
   /// room. Small burns its one ember only when the battery is empty (requests its next when it
   /// runs out); Large keeps the battery topped, burning whenever a full ember fits — refilling
   /// its reserve of up to 3 as it goes. The post-burn UpdateEmber re-routes ember to refill.
   /// </summary>
   private void BurnEmber()
   {
      if (typ != Taip.Ember || connect == null || connect.ember <= 0) return;
      bool room = name.StartsWith("Small") ? Energy <= 0.01f : Energy <= MaxEnergy - emberEnergy + 0.01f;
      if (!room) return;
      connect.ember--;
      Add(emberEnergy);
      connect.onRefresh?.Invoke();
      EnergyManager.i?.UpdateEmber();   // re-route the network to refill the slot we just burned
   }

   /// <summary>
   /// White/Blue end-of-round top-up: while below the maintain floor, burn resource units to
   /// refill — White spends 5 white per unit (→10 energy), Blue 1 blue per unit (→20 energy) —
   /// up to `current` units (the level) and only what's affordable. Floor = level×unitEnergy,
   /// which equals capacity at max level. CanAfford(…, true) spends on success; short-circuit
   /// means nothing is burnt once the battery already sits at/above the floor.
   /// </summary>
   private void MaintainBurn()
   {
      bool white       = typ == Taip.White;
      int[] unitCost   = white ? new[] { 5, 0, 0, 0 } : new[] { 0, 0, 1, 0 };
      float unitEnergy = white ? 10f : 20f;
      float floor      = current * unitEnergy;

      int units = 0;
      while (Energy < floor && units < current
             && ResourceManager.instance.CanAfford(unitCost, false, true))
      {
         Add(unitEnergy);
         units++;
      }
   }

   protected override void BEnable()
   {
      switch (typ)
      {
         case Taip.Pulse:
            EmbersEdge.EEExplodeEvent += act;
            break;
         case Taip.Ember:
            // Join the ember cable network as a sink; cables (re)build so it gets a supply link.
            if (EnergyManager.i != null && !EnergyManager.i.emberGens.Contains(this))
               EnergyManager.i.emberGens.Add(this);
            EnergyManager.i?.CreateCableConnections();
            break;
         default:
            SpawnManager.instance.onWaveComplete += act;
            break;
      }
      EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
   }

   protected override void BDisable()
   {
      switch (typ)
      {
         case Taip.Pulse:
            EmbersEdge.EEExplodeEvent -= act;
            break;
         case Taip.Ember:
            EnergyManager.i?.emberGens.Remove(this);
            EnergyManager.i?.CreateCableConnections();
            break;
         default:
            SpawnManager.instance.onWaveComplete -= act;
            break;
      };
      EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
   }

   // Level steps (min 1 so it always maintains at least one unit; max `limit` = 3/3 white, 4/4 blue).
   private void Reduce()   => current = Mathf.Max(1, current - 1);
   private void Increase() => current = Mathf.Min(limit, current + 1);
   
   private void SetTimer(float time, float quantity)
   {
      actionTimer = time;
      genQuantity += quantity;
   }
}