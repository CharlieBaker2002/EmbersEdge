using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The soul of a dead enemy: a faint swarm of wisps that lingers at the corpse waiting to be
/// collected. When a non-full <see cref="SoulGenerator"/> claims it, the swarm brightens and
/// syphons into that building over ~1.75s, then notifies the generator (which eats it for energy).
/// Each wisp glows in the current dungeon colour via the era glow material — the same mechanism
/// <see cref="EmberParticle"/> uses (the greyscale art is its own emission, tinted by era colour).
/// One Update drives every wisp (no per-wisp MonoBehaviour); wisps share frames + material.
/// </summary>
public class Soul : MonoBehaviour
{
   // Lingering, unclaimed souls — generators seek from here.
   public static readonly List<Soul> available = new();

   const int WISPS = 28;        // "25+" per soul
   const float LINGER = 5f;     // seconds a soul waits to be collected
   const float TRAVEL = 1.75f;  // seconds to syphon into the building
   const float CLUSTER = 0.22f; // initial spread of the wisp cloud (tight)
   const float FAINT = 0.4f;    // lingering brightness/alpha (visible, but dimmer than collected)
   const float BRIGHTEN = 0.4f; // seconds to brighten from faint -> full once claimed
   const float WISP_FPS = 10f;  // shimmer frame rate

   static Sprite[] frames;      // cached soul_0..n from Resources

   enum State { Lingering, Dying, Travelling }
   State state = State.Lingering;
   SoulGenerator target;
   Vector3 targetLocal;         // building centre relative to the soul origin (fixed at claim)
   float lingerT, travelT, vis;
   bool delivered;              // generator notified at nominal arrival (so arms react on time)

   // per-wisp data (parallel arrays)
   SpriteRenderer[] sr;
   Vector3[] home, ctrl;
   float[] orbitPh, orbitR, framePh, baseScale, delay, dur;

   static Sprite[] Frames()
   {
      if (frames == null || frames.Length == 0)
      {
         var list = new List<Sprite>();
         for (int i = 0; i < 32; i++)
         {
            var s = Resources.Load<Sprite>($"Sprites/Soul/soul_{i}");
            if (s == null) break;
            list.Add(s);
         }
         frames = list.ToArray();
      }
      return frames;
   }

   /// <summary>Drop a soul at a corpse. Caller guarantees a hungry generator is in range.</summary>
   public static void Spawn(Vector3 pos)
   {
      var f = Frames();
      if (f.Length == 0) return;                       // art missing — nothing to show
      var go = new GameObject("Soul");
      go.transform.position = pos;
      go.transform.SetParent(GS.FindParent(GS.Parent.fx), true);
      go.AddComponent<Soul>().Build(f);
   }

   /// <summary>Nearest lingering soul within <paramref name="r"/> (for the serial small generator).</summary>
   public static Soul NearestAvailable(Vector3 p, float r)
   {
      float best = r * r;
      Soul bestS = null;
      for (int i = available.Count - 1; i >= 0; i--)
      {
         Soul s = available[i];
         if (s == null) { available.RemoveAt(i); continue; }
         float d = (s.transform.position - p).sqrMagnitude;
         if (d <= best) { best = d; bestS = s; }
      }
      return bestS;
   }

   /// <summary>Claim every lingering soul within range (for the large generator's continuous stream).</summary>
   public static void ClaimInRange(SoulGenerator g, Vector3 p, float r)
   {
      float r2 = r * r;
      for (int i = available.Count - 1; i >= 0; i--)   // backward: Claim removes from the list
      {
         Soul s = available[i];
         if (s == null) { available.RemoveAt(i); continue; }
         if ((s.transform.position - p).sqrMagnitude <= r2) s.Claim(g);
      }
   }

   void Build(Sprite[] f)
   {
      Material mat = GS.MatByEra(GS.era, true, false, true);   // superbright era glow = dungeon colour
      sr = new SpriteRenderer[WISPS];
      home = new Vector3[WISPS];
      ctrl = new Vector3[WISPS];
      orbitPh = new float[WISPS];
      orbitR = new float[WISPS];
      framePh = new float[WISPS];
      baseScale = new float[WISPS];
      delay = new float[WISPS];
      dur = new float[WISPS];

      for (int i = 0; i < WISPS; i++)
      {
         var w = new GameObject("wisp");
         w.transform.SetParent(transform, false);
         Vector2 off = Random.insideUnitCircle * CLUSTER;
         home[i] = new Vector3(off.x, off.y, 0f);
         w.transform.localPosition = home[i];
         baseScale[i] = Random.Range(0.4f, 0.8f);
         w.transform.localScale = Vector3.one * baseScale[i];

         var r = w.AddComponent<SpriteRenderer>();
         r.sharedMaterial = mat;                    // shared: avoid per-wisp material instances
         r.sprite = f[Random.Range(0, f.Length)];
         r.sortingOrder = 100 + Random.Range(0, 6); // above buildings/enemies, like other FX
         sr[i] = r;

         orbitPh[i] = Random.Range(0f, Mathf.PI * 2f);
         orbitR[i] = Random.Range(0.015f, 0.05f);
         framePh[i] = Random.Range(0f, f.Length);
      }
      vis = FAINT;
      ApplyVis();
      available.Add(this);
   }

   /// <summary>A generator grabs this soul; it brightens and heads in. Returns false if already taken.</summary>
   public bool Claim(SoulGenerator g)
   {
      if (state != State.Lingering) return false;
      available.Remove(this);
      target = g;
      targetLocal = g.transform.position - transform.position;
      targetLocal.z = 0f;

      for (int i = 0; i < WISPS; i++)               // bowed/spiralling control point per wisp
      {
         Vector3 a = home[i], b = targetLocal, mid = (a + b) * 0.5f;
         Vector3 dir = b - a;
         float dist = Mathf.Max(0.001f, dir.magnitude);
         dir /= dist;
         Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
         float bow = Random.Range(-1f, 1f) * dist * Random.Range(0.1f, 0.28f);
         ctrl[i] = mid + perp * bow + (Vector3)(Random.insideUnitCircle * 0.07f);
         delay[i] = Random.Range(0f, 0.12f);
         dur[i] = TRAVEL + Random.Range(-0.1f, 0.1f);
      }
      state = State.Travelling;
      travelT = 0f;
      delivered = false;
      return true;
   }

   void Update()
   {
      float dt = Time.deltaTime, t = Time.time;

      switch (state)
      {
         case State.Lingering:
            lingerT += dt;
            for (int i = 0; i < WISPS; i++)
            {
               Vector3 drift = new Vector3(Mathf.Cos(t * 1.3f + orbitPh[i]),
                                           Mathf.Sin(t * 1.1f + orbitPh[i]), 0f) * orbitR[i];
               sr[i].transform.localPosition = home[i] + drift;
            }
            if (lingerT >= LINGER) state = State.Dying;
            break;

         case State.Dying:                            // uncollected: fade the faint cloud out
            vis = Mathf.MoveTowards(vis, 0f, (FAINT / 0.4f) * dt);
            if (vis <= 0.001f) { Destroy(gameObject); return; }
            break;

         case State.Travelling:
            travelT += dt;
            vis = Mathf.Lerp(FAINT, 1f, Mathf.Clamp01(travelT / BRIGHTEN));
            if (!delivered && travelT >= TRAVEL)      // notify the generator as the swarm arrives, not after stragglers
            {
               delivered = true;
               if (target != null) target.ReceiveSoul(this);
            }
            bool allDone = true;
            for (int i = 0; i < WISPS; i++)
            {
               float u = Mathf.Clamp01((travelT - delay[i]) / dur[i]);
               if (u < 1f) allDone = false;
               float e = u * u;                       // ease-in: accelerate (syphon) into the building
               Vector3 p = Bezier(home[i], ctrl[i], targetLocal, e);
               float swirl = 1f - u;                  // spiral that tightens on approach
               p += new Vector3(Mathf.Cos(orbitPh[i] + u * 12f),
                                Mathf.Sin(orbitPh[i] + u * 12f), 0f) * orbitR[i] * 3f * swirl;
               sr[i].transform.localPosition = p;
               float shrink = 1f - Smooth01((u - 0.85f) / 0.15f);   // suck-in: shrink in last 15%
               sr[i].transform.localScale = Vector3.one * baseScale[i] * shrink;
               if (u >= 1f) sr[i].enabled = false;
            }
            if (allDone || travelT >= TRAVEL + 0.6f) { Destroy(gameObject); return; }
            break;
      }

      ApplyVis();
      for (int i = 0; i < WISPS; i++)                 // shimmer
      {
         framePh[i] += dt * WISP_FPS;
         if (sr[i].enabled) sr[i].sprite = frames[(int)framePh[i] % frames.Length];
      }
   }

   void ApplyVis()
   {
      Color c = new Color(vis, vis, vis, vis);
      for (int i = 0; i < WISPS; i++) sr[i].color = c;
   }

   void OnDestroy() => available.Remove(this);

   static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
   {
      float it = 1f - t;
      return it * it * a + 2f * it * t * c + t * t * b;
   }

   static float Smooth01(float u)
   {
      u = Mathf.Clamp01(u);
      return u * u * (3f - 2f * u);
   }
}
