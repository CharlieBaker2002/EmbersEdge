using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
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

   // Energy paid for (orbs ordered/in flight) but not yet converted — counted against the floor
   // so wave refills, spawn charges and raise-level orders never double-order the same energy.
   // Each arriving unit moves its energy from here into genQuantity (then Generate() banks it).
   private float orderedEnergy = 0f;
   // Floating readout (level + per-round cost → energy), shown while the building UI is open.
   private TextMeshPro readout;

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

      // Assign `act` BEFORE base.Start(): a pre-built generator's base.Start() calls BEnable,
      // which subscribes `act` to its trigger event — assigning it afterwards left pre-placed
      // generators subscribed to null (they never generated). UI slots stay below (need UIParent).
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
            act = MaintainBurn;
            break;
         case Taip.Ember:
            // Refilled by the ember cable network (connector set up above); burns in BurnEmber().
            break;
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

      if (typ is Taip.White or Taip.Blue)
      {
         AddSlot(new int[4], "Lower Level",  decreaseSprite, false, Reduce);
         AddSlot(new int[4], "Raise Level",  increaseSprite, false, Increase);

         // Floating readout above the generator while its UI is open: level, the most orbs a
         // round can cost, and the energy floor that buys.
         readout = Instantiate(UIManager.i.numText, transform.position + new Vector3(0f, 1f, 0f),
            Quaternion.identity, transform);
         readout.alignment = TextAlignmentOptions.Center;
         readout.fontSize = Mathf.Max(2f, readout.fontSize * 0.55f);
         readout.color = Color.white;
         var rr = readout.GetComponent<Renderer>();
         if (rr != null) { rr.sortingLayerName = "Power Ups"; rr.sortingOrder = 8; }
         readout.gameObject.SetActive(false);
         OnOpen  += () => { UpdateReadout(); readout.gameObject.SetActive(true); };
         OnClose += () => readout.gameObject.SetActive(false);
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
   /// White/Blue top-up: while below the maintain floor, burn resource units to refill —
   /// White spends 5 white per unit (→10 energy), Blue 1 blue per unit (→20 energy) — up to
   /// `current` units (the level). Floor = level×unitEnergy, which equals capacity at max level.
   /// The spend goes through orb TASKS (not a silent CanAfford deduction) so the orbs visibly
   /// fly into the generator. Orders are placed even when orbs aren't in stock yet (onlyImmediate:
   /// false — the counter goes negative, standard build-task behaviour): orbs granted later flow
   /// into the open order instead of the generator getting a cold no for the round. Each UNIT is
   /// its own task, so the generator converts in correct multiples (5 white / 1 blue → one unit of
   /// energy) as they fill, rather than waiting for the whole order.
   /// </summary>
   private void MaintainBurn()
   {
      bool white       = typ == Taip.White;
      int  orbIndex    = white ? 0 : 2;
      int  unitOrbs    = white ? 5 : 1;
      float unitEnergy = white ? 10f : 20f;
      float floor      = current * unitEnergy;

      // Units needed to get back to the floor, net of everything already ordered (orderedEnergy)
      // or arrived-but-unbanked (genQuantity), capped by the level. Exact accounting makes this
      // idempotent — call it any time; it only ever orders the outstanding deficit.
      int needed = Mathf.CeilToInt((floor - Energy - orderedEnergy - genQuantity) / unitEnergy);
      int units  = Mathf.Min(needed, current);
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

   /// <summary>Refresh the floating readout: level, worst-case orb cost per round, and the
   /// energy floor that maintains. Rich-text colours the orb line in its element's colour.</summary>
   private void UpdateReadout()
   {
      if (readout == null) return;
      bool white   = typ == Taip.White;
      int maxOrbs  = current * (white ? 5 : 1);
      int maxEnergy = Mathf.RoundToInt(current * (white ? 10f : 20f));
      Color orbCol = white ? UIManager.i.colSO.StandardWhite : UIManager.i.colSO.StandardBlue;
      string hex   = ColorUtility.ToHtmlStringRGB(orbCol);
      readout.text =
         $"Level {current}/{limit}\n" +
         $"<color=#{hex}>up to {maxOrbs} {(white ? "white" : "blue")} orbs / round</color>\n" +
         $"keeps {maxEnergy} energy stored";
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
            // Charge on spawn too: a freshly built (or repaired) generator does its round action
            // immediately instead of sitting empty until the next wave completes.
            act?.Invoke();
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
            orderedEnergy = 0f;   // open orders die with the building (their magnets refund on destroy)
            break;
      };
      EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
   }

   // Level steps (min 1 so it always maintains at least one unit; max `limit` = 3/3 white, 4/4 blue).
   // Raising orders the extra orbs IMMEDIATELY (MaintainBurn tops up to the new floor right away);
   // lowering is an order for next round — no refund, the lower floor just applies from then on.
   private void Reduce()   { current = Mathf.Max(1, current - 1);     UpdateReadout(); }
   private void Increase() { current = Mathf.Min(limit, current + 1); UpdateReadout(); MaintainBurn(); }
   
   private void SetTimer(float time, float quantity)
   {
      actionTimer = time;
      genQuantity += quantity;
   }
}