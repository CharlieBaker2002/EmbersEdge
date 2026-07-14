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

   [SerializeField] private GameObject FX;

   // Ember generators join the ember CABLE network as a sink (like an Ember Store): the network
   // routes ember into `connect` up to maxEmber, and we burn held ember into the energy battery.
   public EmberConnector connect;
   private int queue;
   private bool coroutined = false;

   // Player on/off switch (Enable/Disable click slot on every generator). OFF stops burning fuel
   // and ordering more; in-flight orbs still bank on arrival and the battery still serves what
   // it already holds.
   private bool running = true;

   // Internal battery: capacity + draw rate, sized per type in Start. Generators carry NO
   // instabuffer — they are pure sustained-rate sources; burst capacity comes from the grid
   // (pylon surge pools + capacitor-node batteries).
   private readonly EnergyStore store = new EnergyStore(10f, 2f, 0f);

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
   public float PeekMaxDraw(float dt) => store.PeekMaxDraw(dt);

   public event Action<float> OnUpdate;
   public event Action OnUse;

   // Energy paid for (orbs ordered/in flight) but not yet converted — counted against the floor
   // so the continuous top-up never double-orders the same energy. Each arriving unit moves its
   // energy from here into genQuantity (then Generate() banks it).
   private float orderedEnergy = 0f;
   // Pulse/Solar fractional generation accumulator — banks (and animates) per whole energy.
   private float accrual = 0f;
   // White/Blue continuous top-up cadence.
   private float maintainTimer = 0f;

   // Overdraw tripwire: every draw on this generator lands in Use(), so sustained outflow
   // above the rated draw means some consumer path is bypassing MaxDrawThisFrame budgeting
   // (e.g. a cable overdraw regression). 1.5x headroom absorbs legitimate instabuffer bursts.
   private float outflowUsed, outflowWindow;

   public bool Use(float cost)
   {
      if (!store.Use(cost)) return false;
      outflowUsed += cost;
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

   // Solar trickle (energy/second) — the old per-round seasonal lump (4/5/1/2) spread over ~10s.
   private float SolarRate => GS.season switch
   {
      0 => 0.4f,
      1 => 0.5f,
      2 => 0.1f,
      _ => 0.2f
   };

   private void Update()
   {
      store.Tick(Time.deltaTime);   // reconcile the instabuffer every frame, even when idle

      outflowWindow += Time.deltaTime;
      if (outflowWindow >= 5f)
      {
         float outRate = outflowUsed / outflowWindow;
         // Headroom widened for the surge rework: grid surge pools (pylon 1 e/s refill +
         // capacitor batteries) legitimately pull banked energy above the rated draw in
         // window averages, so the tripwire now only catches multiplicative bypass
         // (N-cables-off-one-generator style bugs). Tune after playtest if too loose.
         if (outRate > store.drawRate * 1.5f + 2.05f)
            Debug.LogWarning($"{name}: sustained output {outRate:F2} e/s exceeds rated draw {store.drawRate:F1} e/s — a consumer path is bypassing draw budgeting");
         outflowUsed = 0f; outflowWindow = 0f;
      }

      // Generation is CONTINUOUS (no per-round caps): output is bounded by the battery's draw
      // rate and the fuel supply, not by a wave-complete allowance.
      if (running)
      {
         switch (typ)
         {
            case Taip.Ember:
               BurnEmber();
               break;
            case Taip.Pulse:
               // Constant 1 energy/s, banked (and animated) in whole-energy steps, paused at cap.
               if (Energy + genQuantity < store.capacity - 0.01f)
               {
                  accrual += Time.deltaTime;
                  if (accrual >= 1f) { accrual -= 1f; queue++; SetTimer(0.1f, 1f); }
               }
               break;
            case Taip.Solar:
               accrual += SolarRate * Time.deltaTime;
               if (accrual >= 1f) { accrual -= 1f; SetTimer(0.5f, 1f); }
               break;
            case Taip.White:
            case Taip.Blue:
               maintainTimer -= Time.deltaTime;
               if (maintainTimer <= 0f) { maintainTimer = 0.5f; MaintainBurn(); }
               break;
         }
      }

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
      // serialise these). Capacity is UNCAPPED (except Pulse): generators can bank all round
      // long; the LIMIT is the draw rate (≈ per-cable pylon caps) and the fuel supply, which
      // is the design — batteries hold bursts, generators sustain but need setup.
      // Insta is 0 across the board: generators sustain, the GRID bursts (pylon surge pools +
      // capacitor-node batteries). A lone generator can only ever trickle at its rate.
      (float cap, float rate, float insta) = typ switch
      {
         Taip.Ember => name.StartsWith("Small") ? (float.PositiveInfinity, 1f, 0f)
                                                : (float.PositiveInfinity, 3f, 0f),
         Taip.Solar => (float.PositiveInfinity, 1f, 0f),
         Taip.Pulse => (20f, 1f, 0f),
         Taip.White => (float.PositiveInfinity, 1f, 0f),
         Taip.Blue  => (float.PositiveInfinity, 2f, 0f),
         _          => (10f, 2f, 0f),
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

      AddSlot(new int[4], "Enable / Disable", UIManager.i.noEnergyIcon, false, () => running = !running);
   }

   /// <summary>Energy per ember burned — Large refines 40% more out of each ember than Small.</summary>
   private float EmberEnergy => name.StartsWith("Small") ? 15f : 21f;

   /// <summary>
   /// Ember generators burn a held ember (from the cable network) into the battery when there's
   /// room. Small burns its one ember only when the battery is empty (requests its next when it
   /// runs out); Large keeps ~3 embers' worth banked, burning whenever a full ember fits below
   /// that target — refilling its reserve of up to 3 as it goes. (The battery capacity itself is
   /// uncapped, so the banked target is what stops a Large from draining the whole network.)
   /// The post-burn UpdateEmber re-routes ember to refill.
   /// </summary>
   private void BurnEmber()
   {
      if (typ != Taip.Ember || connect == null || connect.ember <= 0) return;
      float target = 3f * EmberEnergy;
      bool room = name.StartsWith("Small") ? Energy <= 0.01f : Energy <= target - EmberEnergy + 0.01f;
      if (!room) return;
      connect.ember--;
      Add(EmberEnergy);
      connect.onRefresh?.Invoke();
      EnergyManager.i?.UpdateEmber();   // re-route the network to refill the slot we just burned
   }

   /// <summary>
   /// White/Blue continuous top-up: whenever a whole fuel unit fits under the banked floor, order
   /// it — the floor is two units, so both refuel at HALF energy (no dead time waiting for empty).
   /// White buys 5 white orbs at a time (→10 energy, floor 20), Blue buys 1 blue orb at a time
   /// (→20 energy, floor 40: keeps two orbs' worth in). No per-round
   /// cap — a generator kept fed burns all round long. The spend goes through orb TASKS (not a
   /// silent CanAfford deduction) so the orbs visibly fly into the generator, and orders are
   /// placed even when orbs aren't in stock yet (onlyImmediate: false): orbs granted later flow
   /// into the open order instead of the generator getting a cold no. Exact accounting against
   /// orderedEnergy/genQuantity makes this idempotent — the 0.5s cadence only ever orders the
   /// outstanding deficit.
   /// </summary>
   private void MaintainBurn()
   {
      bool white       = typ == Taip.White;
      int  orbIndex    = white ? 0 : 2;
      int  unitOrbs    = white ? 5 : 1;
      float unitEnergy = white ? 10f : 20f;
      float floor      = white ? 20f : 40f;

      // Whole units that fit below the floor, net of everything already ordered (orderedEnergy)
      // or arrived-but-unbanked (genQuantity). Floor (not ceil): only refuel when a FULL unit
      // fits, so the bank never overshoots the floor.
      int units = Mathf.FloorToInt((floor - Energy - orderedEnergy - genQuantity) / unitEnergy + 0.001f);
      if (units <= 0) return;

      for (int i = 0; i < units; i++)
      {
         int[] unitCost = new int[4];
         unitCost[orbIndex] = unitOrbs;
         orderedEnergy += unitEnergy;
         ResourceManager.instance.NewTask(gameObject, unitCost, () =>
         {
            orderedEnergy = Mathf.Max(0f, orderedEnergy - unitEnergy);
            SetTimer(0.1f, unitEnergy);
         }, false);
      }
   }

   protected override void BEnable()
   {
      if (typ == Taip.Ember)
      {
         // Join the ember cable network as a sink; cables (re)build so it gets a supply link.
         if (EnergyManager.i != null && !EnergyManager.i.emberGens.Contains(this))
            EnergyManager.i.emberGens.Add(this);
         EnergyManager.i?.CreateCableConnections();
      }
      EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
   }

   protected override void BDisable()
   {
      switch (typ)
      {
         case Taip.Ember:
            EnergyManager.i?.emberGens.Remove(this);
            EnergyManager.i?.CreateCableConnections();
            break;
         case Taip.White:
         case Taip.Blue:
            orderedEnergy = 0f;   // open orders die with the building (their magnets refund on destroy)
            break;
      };
      EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
   }

   private void SetTimer(float time, float quantity)
   {
      actionTimer = time;
      genQuantity += quantity;
   }
}
