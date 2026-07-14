using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A building that feeds on the souls of nearby dead enemies for energy.
/// A <see cref="Soul"/> spawns at a corpse (only if a non-full generator is in range), lingers
/// faintly, then syphons into a generator that claims it. The body turns to face the soul it's eating,
/// the claws rotate to grab it, and the claw sprite sheets animate as an energy pulse.
/// <list type="bullet">
/// <item><b>Small</b> (radius 4): serial — digests one soul over 3s, ramping in +1 energy; claws
/// slowly animate to the end of their sheet while eating, then slowly back afterwards.</item>
/// <item><b>Large</b> (radius 6): a continuous stream — each arrived soul eaten in 0.25s for +1; its
/// small claws bite (and pulse) much faster than its big claws. Energy pulse = quick 30 FPS to the
/// end of the sheet and back, once eaten.</item>
/// </list>
/// Energy is banked in an internal <see cref="EnergyStore"/> and served to the grid via
/// <see cref="IEnergyAccumulator"/> + <see cref="EnergyManager"/> (unchanged).
/// </summary>
public class SoulGenerator : Building, IOnDeath, IEnergyAccumulator
{
   [SerializeField] private GameObject FX;        // optional spawn-on-eat effect (assigned on the large prefab)
   public static List<SoulGenerator> gs;
   public float range = 2f;                        // collection radius (set per-variant in Start)

   const float DIGEST = 3f;                        // small: seconds to digest one soul (+1 energy ramped)
   const float BITE = 0.25f;                       // large: seconds to eat one soul
   const float CHOMP_DEG = 32f;                    // arm "eat" rotation amplitude
   const float AIM_SPEED = 8f;                     // how fast the body turns to face a soul
   const float SMALL_ARM_SPEED = 0.3f;             // big collector's small claws eat ~3x faster than big claws
   const float PULSE_FPS = 15f;                    // large: energy-pulse frame rate (half speed)
   const float SMALL_FWD_DUR = 1.5f;              // small: seconds to animate the sheet to the end while eating
   const float SMALL_BACK_DUR = 0.75f;            // small: seconds to animate the sheet back to rest after eating

   // Internal battery: capacity + draw rate + instabuffer (burst pool), sized by variant in Start.
   private readonly EnergyStore store = new EnergyStore(10f, 2f, 0f);

   private bool small;
   private Transform[] arms;
   private Quaternion[] armRestRot;
   private Vector3[] armRestEuler;
   private float[] armSide;
   private float[] armSpeed;                        // per-arm eat-speed multiplier (small claws bite faster)

   // Per-arm claw sprite-sheet animation (the "energy pulse"), driven manually so it never fights the
   // LeanTween rotation on the same object.
   private SpriteRenderer[] armSR;
   private Sprite[][] armFrames;
   private float[] armFrameT;                       // current frame position
   private int[] armFrameDir;                       // +1 forward, -1 back, 0 idle
   private float[] armFrameRate;                    // frames/sec
   private bool[] armFramePulse;                    // auto-reverse at the end (large pulse) vs hold (small)
   private static readonly Dictionary<string, Sprite[]> sheetCache = new();

   private Soul current;                            // small: soul currently inbound (null once digesting)
   private bool digesting;
   private float digestT;
   private Soul facingSoul;                         // soul currently being faced / collected

   public float Energy    => store.Energy;
   public float MaxEnergy => store.MaxEnergy;
   public float DrawRate  => store.DrawRate;
   public float MaxDrawThisFrame(float dt) => store.MaxDrawThisFrame(dt);
   public float PeekMaxDraw(float dt) => store.PeekMaxDraw(dt);
   public bool Full => store.Energy >= store.MaxEnergy - 0.001f;

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

   public override void Start()
   {
      // Variant-authoritative config: the full Soul Generator reaches further, banks more, and eats faster.
      small = name.StartsWith("Small");
      store.Configure(small ? 5f : 30f, small ? 1f : 3f, 0f);
      range = small ? 4f : 6f;

      // Arms (claws) are found by child name — no prefab wiring. Cache rest pose + sprite-sheet frames.
      string[] names = small
         ? new[] { "L", "R" }
         : new[] { "BigClawL", "BigClawR", "SmallClawL", "SmallClawR" };
      var found = new List<Transform>();
      foreach (string n in names)
      {
         Transform tr = transform.Find(n);
         if (tr != null) found.Add(tr);
      }
      arms = found.ToArray();
      int len = arms.Length;
      armRestRot = new Quaternion[len];
      armRestEuler = new Vector3[len];
      armSide = new float[len];
      armSpeed = new float[len];
      armSR = new SpriteRenderer[len];
      armFrames = new Sprite[len][];
      armFrameT = new float[len];
      armFrameDir = new int[len];
      armFrameRate = new float[len];
      armFramePulse = new bool[len];
      for (int i = 0; i < len; i++)
      {
         armRestRot[i] = arms[i].localRotation;
         armRestEuler[i] = arms[i].localRotation.eulerAngles;
         float x = arms[i].localPosition.x;
         armSide[i] = x >= 0f ? 1f : -1f;            // right arms close one way, left arms the other
         bool smallClaw = arms[i].name.Contains("Small");
         armSpeed[i] = smallClaw ? SMALL_ARM_SPEED : 1f;   // small claws bite faster

         armSR[i] = arms[i].GetComponent<SpriteRenderer>();
         string sheet = small
            ? "Sprites/Soul/Claws/SmallSoulHarvesterClaw"
            : (arms[i].name.Contains("Big") ? "Sprites/Soul/Claws/LargeSoulHarvesterClaw"
                                            : "Sprites/Soul/Claws/LargeSoulHarvesterSmallClaw");
         armFrames[i] = LoadFrames(sheet);
         if (armSR[i] != null && armFrames[i].Length > 0) armSR[i].sprite = armFrames[i][0];
      }

      base.Start();
   }

   protected override void BEnable()
   {
      gs.Add(this);
      EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);

      // Retro-attach the death hook to enemies already alive, so a generator built mid-wave starts
      // collecting immediately (new enemies get it from EmbersEdge.SpawnEnemy once gs is non-empty).
      if (SpawnManager.instance != null)
      {
         foreach (GameObject g in SpawnManager.instance.alives)
         {
            if (g == null) continue;
            foreach (LifeScript l in g.GetComponentsInChildren<LifeScript>())
            {
               if (l.GetComponent<SoulCollectOnDeath>() == null)
               {
                  var sc = l.gameObject.AddComponent<SoulCollectOnDeath>();
                  l.onDeaths.Add(sc);
               }
            }
         }
      }
   }

   protected override void BDisable()
   {
      gs.Remove(this);
      EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
      ResetArms();
   }

   private void Update()
   {
      store.Tick(Time.deltaTime);                   // reconcile the instabuffer every frame

      if (small)
      {
         if (digesting)
         {
            digestT += Time.deltaTime;
            Add(Time.deltaTime / DIGEST);            // ramp +1 energy across the 3s digest
            if (digestT >= DIGEST)
            {
               digesting = false;
               ResetArms();
               for (int i = 0; i < arms.Length; i++) StartBackOver(i, SMALL_BACK_DUR);   // slowly animate back
            }
         }
         else if (current == null && !Full)
         {
            Soul s = Soul.NearestAvailable(transform.position, range);
            if (s != null && s.Claim(this)) { current = s; facingSoul = s; }
         }
      }
      else if (!Full)
      {
         if (facingSoul == null) facingSoul = Soul.NearestAvailable(transform.position, range);
         Soul.ClaimInRange(this, transform.position, range);   // continuous stream
      }

      FaceSoul();
      AdvanceFrames();
   }

   /// <summary>Called by a <see cref="Soul"/> as its swarm arrives (so the arms react on time).</summary>
   public void ReceiveSoul(Soul s)
   {
      if (small)
      {
         digesting = true;
         digestT = 0f;
         current = null;                            // the soul object destroys itself after this call
         Chew();                                    // arms grab and work for the digest duration
         for (int i = 0; i < arms.Length; i++) StartFwdOver(i, SMALL_FWD_DUR);   // energy build while eating
      }
      else
      {
         Add(1f);                                   // instant +1
         Chomp(BITE);
         for (int i = 0; i < arms.Length; i++) StartPulse(i);             // quick energy pulse once eaten
      }
      if (FX != null) Instantiate(FX, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx));
   }

   /// <summary>Called by <see cref="Finder"/> when a wave starts.</summary>
   public void Activate() => ResetArms();

   // Turn the body to face the soul being collected; hold the last facing when nothing is inbound.
   private void FaceSoul()
   {
      if (facingSoul == null) return;
      Vector2 dir = facingSoul.transform.position - transform.position;
      if (dir != Vector2.zero)
         transform.up = Vector2.Lerp(transform.up, dir.normalized, AIM_SPEED * Time.deltaTime);
   }

   // ---- Claw sprite-sheet "energy pulse" (driven manually; independent of the rotation tween) ----

   private void AdvanceFrames()
   {
      if (armSR == null) return;
      float dt = Time.deltaTime;
      for (int i = 0; i < armSR.Length; i++)
      {
         Sprite[] f = armFrames[i];
         if (armSR[i] == null || f == null || f.Length < 2 || armFrameDir[i] == 0) continue;
         int last = f.Length - 1;
         armFrameT[i] += armFrameDir[i] * armFrameRate[i] * dt;
         if (armFrameT[i] >= last) { armFrameT[i] = last; armFrameDir[i] = armFramePulse[i] ? -1 : 0; }
         else if (armFrameT[i] <= 0f) { armFrameT[i] = 0f; armFrameDir[i] = 0; }
         armSR[i].sprite = f[Mathf.Clamp(Mathf.RoundToInt(armFrameT[i]), 0, last)];
      }
   }

   private void StartPulse(int i)                    // large: quick to-end-and-back at 30 FPS
   {
      if (armFrames[i] == null || armFrames[i].Length < 2) return;
      if (armFrameDir[i] == 0) armFrameT[i] = 0f;    // fresh pulse; mid-pulse just (re)drives forward, no snap
      armFrameDir[i] = 1;
      armFrameRate[i] = PULSE_FPS;
      armFramePulse[i] = true;
   }

   private void StartFwdOver(int i, float dur)       // small: slow toward the end while eating (holds at end)
   {
      if (armFrames[i] == null || armFrames[i].Length < 2) return;
      armFrameT[i] = 0f;
      armFrameDir[i] = 1;
      armFrameRate[i] = (armFrames[i].Length - 1) / Mathf.Max(0.01f, dur);
      armFramePulse[i] = false;
   }

   private void StartBackOver(int i, float dur)      // small: slow back to the start afterwards
   {
      if (armFrames[i] == null || armFrames[i].Length < 2) return;
      armFrameDir[i] = -1;
      armFrameRate[i] = (armFrames[i].Length - 1) / Mathf.Max(0.01f, dur);
      armFramePulse[i] = false;
   }

   private static Sprite[] LoadFrames(string path)
   {
      if (sheetCache.TryGetValue(path, out Sprite[] cached)) return cached;
      Sprite[] all = Resources.LoadAll<Sprite>(path);
      var list = new List<Sprite>();
      foreach (Sprite s in all)                       // keep numbered frames, drop the "_Emission" sub-sprite
      {
         int u = s.name.LastIndexOf('_');
         if (u >= 0 && int.TryParse(s.name.Substring(u + 1), out _)) list.Add(s);
      }
      list.Sort((a, b) => FrameIndex(a) - FrameIndex(b));
      Sprite[] arr = list.ToArray();
      sheetCache[path] = arr;
      return arr;
   }

   private static int FrameIndex(Sprite s)
   {
      int u = s.name.LastIndexOf('_');
      return int.Parse(s.name.Substring(u + 1));
   }

   // ---- Arm "eat" rotation: rotate each claw inward toward the mouth, then back -----------------

   private void Chomp(float time)                    // one quick bite (large); small claws bite faster
   {
      if (arms == null) return;
      for (int i = 0; i < arms.Length; i++)
      {
         GameObject go = arms[i].gameObject;
         Vector3 rest = armRestEuler[i];
         Vector3 closed = rest + new Vector3(0f, 0f, armSide[i] * CHOMP_DEG);
         float t = time * armSpeed[i];
         LeanTween.cancel(go);
         LeanTween.rotateLocal(go, closed, t * 0.45f).setEaseOutQuad().setDelay(i * 0.02f)
            .setOnComplete(() => LeanTween.rotateLocal(go, rest, t * 0.55f).setEaseInQuad());
      }
   }

   private void Chew()                               // continuous ping-pong chew (small, until ResetArms)
   {
      if (arms == null) return;
      for (int i = 0; i < arms.Length; i++)
      {
         GameObject go = arms[i].gameObject;
         Vector3 closed = armRestEuler[i] + new Vector3(0f, 0f, armSide[i] * CHOMP_DEG);
         LeanTween.cancel(go);
         LeanTween.rotateLocal(go, closed, 0.45f * armSpeed[i]).setEaseInOutSine().setLoopPingPong();
      }
   }

   private void ResetArms()
   {
      if (arms == null) return;
      for (int i = 0; i < arms.Length; i++)
      {
         LeanTween.cancel(arms[i].gameObject);
         arms[i].localRotation = armRestRot[i];
      }
   }
}
