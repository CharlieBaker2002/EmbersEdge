using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoulGenerator : Building, IOnDeath, IEnergyAccumulator
{
   [SerializeField] Sprite[] sprs;
   [SerializeField] private SpriteRenderer[] arms;
   [SerializeField] private Sprite offSpr;
   [SerializeField] private GameObject FX;
   public static List<SoulGenerator> gs;
   private Dictionary<Collider2D, SoulCollectOnDeath> map = new();
   public float range = 2f;
   [SerializeField] private SpriteRenderer blanksr;
   private System.Action act;
   public bool busy;

   // Internal battery: capacity + draw rate + instabuffer (burst pool), sized by variant in Start.
   private readonly EnergyStore store = new EnergyStore(10f, 2f, 2f);

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

   private void Update() => store.Tick(Time.deltaTime);   // reconcile the instabuffer every frame

   // Visuals (arms/sprs) are optional for now — guard every access so a headless,
   // un-wired generator still collects souls and feeds the grid.
   bool HasArms => arms != null && arms.Length >= 2;
   bool HasSprs => sprs != null && sprs.Length > 0;

   public void Animate()
   {
      if (HasArms && HasSprs)
      {
         arms[0].LeanAnimateFPS(sprs, 1, true);
         arms[1].LeanAnimateFPS(sprs, 1, true);
      }
      this.QA(Generate, (sprs != null ? sprs.Length : 0) * 0.5f / 12f);
   }

   public void Activate()
   {
      if (!HasArms || !HasSprs) return;
      arms[0].sprite = sprs[0];
      arms[1].sprite = sprs[0];
   }

   private void Generate()
   {
      if (FX != null) Instantiate(FX, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx));
      Add(1f);
   }

   public override void Start()
   {
      // Internal battery (capacity, rate, instabuffer) + collection radius sized by variant
      // (code-authoritative). The full Soul Generator reaches further (collects more souls/wave)
      // and banks more than the small one.
      bool small = name.StartsWith("Small");
      store.Configure(small ? 5f : 30f, small ? 1f : 3f, 0f);
      range = small ? 2f : 3.5f;

      act = () =>
      {
         this.QA(() =>
         {
            if (!HasArms) return;
            arms[0].sprite = offSpr;
            arms[1].sprite = offSpr;
         },1.5f);
      };
      base.Start();
   }

   protected override void BEnable()
   {
      gs.Add(this);
      SpawnManager.instance.onWaveComplete += act;
      EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
   }

   protected override void BDisable()
   {
      gs.Remove(this);
      if (HasArms)
      {
         arms[0].sprite = offSpr;
         arms[1].sprite = offSpr;
      }
      SpawnManager.instance.onWaveComplete -= act;
      EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
   }

   public void Collect(Transform tran)
   {
      List<SpriteRenderer> srs = new List<SpriteRenderer>();
      foreach (SpriteRenderer s in tran.GetComponentsInChildren<SpriteRenderer>())
      {
         srs.Add(Instantiate(blanksr, s.transform.position, s.transform.rotation, transform));
         srs[^1].transform.localScale = s.transform.localScale;
         srs[^1].material = s.material;
         srs[^1].color = Color.Lerp(s.color, Color.black, 0.5f);
      }

      foreach (SpriteRenderer s in srs)
      {
         s.transform.LeanMoveLocal(Vector3.zero, 1f).setEaseOutQuart();
      }

      StartCoroutine(CollectSoul(srs));
      Animate();

      IEnumerator CollectSoul(List<SpriteRenderer> ss)
      {
         for(float t = 1f; t > 0f; t -= Time.deltaTime)
         {
            for (int i = 0; i < ss.Count; i++)
            {
               ss[i].transform.localScale = t*Vector3.one;
               ss[i].color = new Color(ss[0].color.r, ss[0].color.g, ss[0].color.b, t);
            }
            yield return null;
         }
         for (int i = 0; i < ss.Count; i++)
         {
            Destroy(ss[i].gameObject);
         }
      }
   }
}