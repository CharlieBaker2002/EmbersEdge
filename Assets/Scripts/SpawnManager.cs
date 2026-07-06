using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager instance;
    [Space(10)]
    public Transform orbParent;
    public List<MarauderSO> marauders;
    public int incrementer;
    public GameObject[] orbs;
    public ObjectPool<GameObject>[] orbPools = new ObjectPool<GameObject>[4];
    public GameObject[] chests;
    public Transform Allies;
    public Transform AllyBuildings;
    public Transform AllyProjectiles;
    public Transform Enemies;
    public Transform EnemyProjectiles;
    public Transform FX;
    public Transform EE;
    public Transform Loot;
    public Transform Misc;
    public Transform Followers;
    public TextMeshProUGUI timeText;
    public System.Action OnNewDay;
    private float sinceLastBigAttack = 0f;
    private float activityLevel;     // 0.45–1, drives wave VISUALS (spin / Acco wibble / slider)
    private float waveRand = 1f;     // budget multiplier rolled per cycle (EnsureActivityRolled)
    private int activityCategory;    // 0=Weakly,1=Active,2=Very,3=Extremely (difficulty-shifted vocabulary)
    public System.Action onWaveComplete;
    public bool waveCompleted = false;
    bool helpedWithWave = false; //determines whether next day should be called even from dungeon
    float maxTimer;
    public static bool eeactive = false;

    // Player-triggered waves: a wave is "armed" (pre-rolled + previewed) when the player returns
    // from a dungeon run, then summoned manually with V / the Tele-Phone. See ArmWave / TryStartWave.
    public bool waveArmed = false;
    public WavePlan currentPlan;
    [Tooltip("Authored waves (Tools > Wave Forge). If null, falls back to Resources/WaveAuthoring, then to the legacy procedural roll.")]
    public WaveAuthoringSO waveAuthoring;
    [HideInInspector] public bool forceStartOnReturn = false; // death punishment: auto-summon on the way home
    [HideInInspector] public bool inBossTransition = false;   // suppress arming during boss/era change
    private bool activityRolled = false; // activity is rolled once per cycle (forecast or arm); reset on defeat/era
    private bool previewActive = false;  // a wave preview (pre-dungeon forecast OR armed) is currently shown

    public Material[] eraMats;

    public enum DayState { Day, PreAttack, Attack }
    public DayState dayState = DayState.Day;

    private List<TS> timeScales = new();

    public float timer = 120f;

    public static int day = 0;
    public static int daySinceNewEra = 0;
    // Clean 0-based index of the upcoming wave within the current era -> authored Day index (Day 1 = 0).
    // Kept separate from daySinceNewEra (which has a start-up/era-change off-by-one) so the designer's
    // "Day 1" always lines up with the first wave of a dungeon.
    public static int eraWaveIndex = 0;
    private bool realWaveThisCycle = false; // true once a real wave is summoned; gates the eraWaveIndex bump
    public Slider activitySlider;
    public Slider eraCompletionSlider;

    public GameObject castFX;

    public GameObject[] vulnerables;

    public List<EmbersEdge> EEs = new List<EmbersEdge>();
    public List<GameObject> alives = new List<GameObject>();
    public Transform EESpawnPoint;
    public MarauderSO[] E2SOs;
    public MarauderSO[] E3SOs;

    public Color e1;
    public Color e2;
    public Color e3;

    public EEIcon EEIcon;


    private void Awake()
    {
        onWaveComplete = delegate
        {
            //UIManager.i.SetTelePhone(UIManager.TeleMode.Base, 1f);
            if (!GS.CS().InDungeon()) { PortalScript.i.YesPortal(); } 
        };
        maxTimer = timer;
        instance = this;
        if (waveAuthoring == null) waveAuthoring = Resources.Load<WaveAuthoringSO>("WaveAuthoring");
        EmbersEdge.warmUpTime = 20f;
        day = 0;
        daySinceNewEra = 0;
        OnNewDay += delegate {day++; daySinceNewEra++; UIManager.i.UpdateDayText(day);};
        Random.InitState((int)System.DateTime.Now.Ticks);
        orbPools[0] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[0], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            if (!Application.isPlaying || !orb) return;
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[1] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[1], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            if (!Application.isPlaying || !orb) return;
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[2] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[2], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            if (!Application.isPlaying || !orb) return;
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[3] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[3], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            if (!Application.isPlaying || !orb) return;
            Destroy(orb);
            OrbScript.tot--;
        });
    }
    
    /// <summary>
    /// Forecast the next wave while peaceful at base BEFORE any dungeon run, so the player can scout
    /// where/what it will be. Builds (display-only) the plan for the cores they currently have. The
    /// SAME plan object is reused + extended by ArmWave on return, so the main core's part of the
    /// forecast stays stable and the dungeon only ADDS cores. Driven from the Day-state Update.
    /// </summary>
    private void ShowPreDungeonPreview()
    {
        EnsureActivityRolled();
        currentPlan = BuildFullPlan(activityLevel);
        previewActive = true;
        if (EnemyTracker.i != null) EnemyTracker.i.ShowPreview(currentPlan);
        MapManager.SetSpin(activityLevel); // testingDefence: the EE goes active pre-dungeon for defence testing
        SetActivityText();
    }

    // Status text + colour for the current rolled activity tier (vocabulary shifts with difficulty).
    private void SetActivityText()
    {
        switch (activityCategory)
        {
            case 0: timeText.text = "Ember's Edge Weakly Active"; timeText.color = new Color(0.675f, 0.5f, 0.3f); break;
            case 1: timeText.text = "Ember's Edge Active"; timeText.color = new Color(0.775f, 0.4f, 0.2f); break;
            case 2: timeText.text = "Ember's Edge Very Active"; timeText.color = new Color(0.875f, 0.15f, 0.1f); break;
            default: timeText.text = "Ember's Edge Extremely Active"; timeText.color = Color.red; break;
        }
    }

    /// <summary>
    /// Arm the next wave on return from a dungeon run (PortalScript.PortalFR): reuse the rolled
    /// activity, fold in any cores absorbed during the run, lock the dungeon teleport, and keep the
    /// preview up. The player then summons it manually (V / Tele-Phone).
    /// </summary>
    public void ArmWave()
    {
        if (inBossTransition) return;          // boss-defeat / era change manages its own flow
        if (dayState != DayState.Day) return;  // a wave is already underway
        if (waveArmed || eeactive) return;     // already armed/active this cycle
        if (EEs.Count == 0) return;

        EnsureActivityRolled();
        currentPlan = BuildFullPlan(activityLevel); // recompute for the full core set (now incl. absorbed cores)

        waveArmed = true;
        previewActive = true;
        PortalScript.i.NoPortal();               // can't flee back to the dungeon until it's cleared
        if (EnemyTracker.i != null) EnemyTracker.i.ShowPreview(currentPlan);
        // The Ember's Edge becomes ACTIVE on return (visual spin + status) but doesn't spawn until summoned.
        MapManager.SetSpin(activityLevel);
        SetActivityText();
    }

    /// <summary>
    /// Tele-Phone "skip the dungeon" hatch: arm (if needed) with whatever cores you have RIGHT NOW —
    /// no dungeon-absorbed bonus — and summon immediately. The V-key cycle still requires a dungeon
    /// run; this is the explicit accelerate/skip option.
    /// </summary>
    public void ForceStartWave()
    {
        if (eeactive || dayState != DayState.Day || PortalScript.i.inDungeon || EEs.Count == 0) return;
        if (!waveArmed)
        {
            EnsureActivityRolled();
            currentPlan = BuildFullPlan(activityLevel);
            waveArmed = true;
        }
        TryStartWave();
    }

    // Intermediate per-core spawn in absolute-time space (converted to PlannedSpawn pre-spawn delays
    // at the end). `locked` spawns come from a day whose score the designer overrode and must not be trimmed.
    private class TimedSpawn
    {
        public float time;
        public MarauderSO so;
        public float tOffset;
        public bool locked;
        public TimedSpawn(float t, MarauderSO s, float off, bool lck) { time = t; so = s; tOffset = off; locked = lck; }
    }

    /// <summary>
    /// Assemble the whole wave from the authored content (Wave Forge):
    ///  1. each core lays out its assigned collection's day (subwave times -> absolute spawn times),
    ///  2. the day's credit budget ("Day Score") = sum of the per-core formula (intensity * cores),
    ///  3. if the placed score exceeds the budget, randomly delete enemies (override-locked days are spared),
    ///  4. otherwise spend the leftover by randomly adding clusters round-robin across the cores.
    /// Falls back to the legacy procedural roll when no authoring asset is assigned.
    /// </summary>
    WavePlan BuildFullPlan(float activity)
    {
        var plan = new WavePlan { activity = activity };
        if (EEs.Count == 0) return plan;

        if (waveAuthoring == null)
        {
            foreach (EmbersEdge EE in EEs) plan.cores.Add(EE.PlanWave(activity + EE.bias));
            return plan;
        }

        int dungeon = Mathf.Clamp(GS.era, 0, 2);
        int dayIndex = eraWaveIndex; // GetDay clamps to the dungeon's day count

        // 1. Base placement from each core's collection day.
        var coreTimed = new List<KeyValuePair<EmbersEdge, List<TimedSpawn>>>();
        float placed = 0f;
        float maxDayDuration = 0f;
        foreach (EmbersEdge EE in EEs)
        {
            var timed = new List<TimedSpawn>();
            string collName = CollectionFor(EE, dungeon);
            WaveCollection coll = string.IsNullOrEmpty(collName) ? null : waveAuthoring.GetCollection(dungeon, collName);
            DayPlan day = waveAuthoring.GetDay(coll, dungeon, dayIndex);
            if (day != null)
            {
                AppendDay(timed, day);
                placed += WaveAuthoringSO.DayPoints(day); // override-aware budget contribution
                maxDayDuration = Mathf.Max(maxDayDuration, WaveAuthoringSO.DayWaveDuration(day));
            }
            coreTimed.Add(new KeyValuePair<EmbersEdge, List<TimedSpawn>>(EE, timed));
        }

        // 2. Day Score = the Main collection's day price ("base budget") × the activity roll, plus a flat
        //    share per extra (non-main) core:  base*rand + budgetPerExtraCore*numExtra*base.
        WaveCollection mainColl = waveAuthoring.GetCollection(dungeon, WaveAuthoringSO.MAIN);
        float baseBudget = WaveAuthoringSO.DayPoints(waveAuthoring.GetDay(mainColl, dungeon, dayIndex));
        int numExtra = Mathf.Max(0, EEs.Count - 1);
        float dayScore = baseBudget * (waveRand + waveAuthoring.budgetPerExtraCore * numExtra);

        // Total credit budget for the whole wave, factoring in the main core's base day price, the
        // difficulty-driven intensity roll (waveRand), and a share per extra core.
        plan.totalCredits = dayScore;
        Debug.Log($"[Wave] day {eraWaveIndex + 1}: total credits {dayScore:0} " +
                  $"(base {baseBudget:0} × [rand {waveRand:0.00} + {waveAuthoring.budgetPerExtraCore:0.00}×{numExtra} extra cores], " +
                  $"difficulty {Mathf.Clamp(SetM.difficulty, 1f, 3f):0.0}, {EEs.Count} cores)");

        // 3 / 4. Trim if over budget, else fill the leftover with clusters spread across the attack window:
        // window = max(2 * day-within-era, longest authored day across the cores' collections).
        if (placed > dayScore) TrimTimed(coreTimed, placed - dayScore);
        else FillClusters(coreTimed, dungeon, dayScore - placed, Mathf.Max(2f * (eraWaveIndex + 1), maxDayDuration));

        // 5. Convert to CorePlans (sort by time -> pre-spawn delays).
        foreach (var kv in coreTimed)
        {
            var cp = new CorePlan(kv.Key);
            kv.Value.Sort((a, b) => a.time.CompareTo(b.time)); // Acco relies on plannedSpawns being time-sorted
            foreach (var ts in kv.Value)
                if (ts.so != null) cp.spawns.Add(new PlannedSpawn(ts.so, ts.tOffset, ts.time));
            plan.cores.Add(cp);
        }
        return plan;
    }

    // The collection a core draws from this era: "Main" for the main core, a stable random one otherwise.
    string CollectionFor(EmbersEdge EE, int dungeon)
    {
        if (EE == EmbersEdge.mainCore) { EE.assignedCollection = WaveAuthoringSO.MAIN; return WaveAuthoringSO.MAIN; }
        if (string.IsNullOrEmpty(EE.assignedCollection))
            EE.assignedCollection = waveAuthoring.RandomCollectionName(dungeon, true);
        return EE.assignedCollection;
    }

    void AppendDay(List<TimedSpawn> timed, DayPlan day)
    {
        foreach (Subwave sw in day.subwaves)
            AppendGroup(timed, sw.enemies, sw.time, sw.duration, day.scoreOverridden);
    }

    // Lay a subwave/cluster onto the timeline starting at `start`: each enemy's gap to the next equals
    // price * duration / (sum of the group's prices), so the group is spread across `duration` weighted
    // by price. Order: top rows (lowest gridY) first, random within each row.
    void AppendGroup(List<TimedSpawn> timed, List<PlacedEnemy> enemies, float start, float duration, bool locked)
    {
        var ordered = OrderForSpawn(enemies);
        if (ordered.Count == 0) return;
        float sum = 0f;
        foreach (var e in ordered) sum += WaveAuthoringSO.EnemyPoints(e.so);
        // World-unit column spacing -> perimeter fraction uses the boundary's current arc length.
        float perimeter = MapManager.i != null ? MapManager.i.BoundaryPerimeter() : 0f;
        // 50% chance to mirror this whole group horizontally (negate the rim offset around the EE anchor),
        // so symmetrical variants come for free without authoring mirrored copies.
        float mirror = Random.value < 0.5f ? -1f : 1f;
        float t = start;
        foreach (var e in ordered)
        {
            if (e.so != null)
                timed.Add(new TimedSpawn(t, e.so, mirror * waveAuthoring.ColToTOffset(e.gridX, perimeter), locked));
            float slice = sum > 0f ? WaveAuthoringSO.EnemyPoints(e.so) * duration / sum : duration / ordered.Count;
            t += slice;
        }
    }

    // Spawn order within a group: rows top-down (gridY ascending), randomised within each row.
    static List<PlacedEnemy> OrderForSpawn(List<PlacedEnemy> es)
    {
        var byRow = new SortedDictionary<int, List<PlacedEnemy>>();
        foreach (var e in es)
        {
            if (!byRow.TryGetValue(e.gridY, out var row)) { row = new List<PlacedEnemy>(); byRow[e.gridY] = row; }
            row.Add(e);
        }
        var result = new List<PlacedEnemy>(es.Count);
        foreach (var kv in byRow)
        {
            var row = kv.Value;
            for (int i = row.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (row[i], row[j]) = (row[j], row[i]); }
            result.AddRange(row);
        }
        return result;
    }

    // Detail 1: over budget -> randomly delete enemies (skipping override-locked days) until within budget.
    void TrimTimed(List<KeyValuePair<EmbersEdge, List<TimedSpawn>>> coreTimed, float excess)
    {
        var pool = new List<KeyValuePair<List<TimedSpawn>, TimedSpawn>>();
        foreach (var kv in coreTimed)
            foreach (var ts in kv.Value)
                if (!ts.locked) pool.Add(new KeyValuePair<List<TimedSpawn>, TimedSpawn>(kv.Value, ts));
        // Fisher–Yates shuffle so the deletions are random.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        for (int k = 0; k < pool.Count && excess > 0f; k++)
            if (pool[k].Key.Remove(pool[k].Value))
                excess -= WaveAuthoringSO.EnemyPoints(pool[k].Value.so);
    }

    // Cluster filling, in two strict phases:
    //  A) SELECTION — round-robin across cores, randomly taking affordable clusters until the leftover
    //     can't afford any (unaffordable clusters are dropped; leftover only shrinks). No timing yet.
    //  B) PLACEMENT — decided AFTER selection: spread the chosen clusters across [0, window] weighted by
    //     cumulative price, so the added price rate stays as constant as possible through the attack.
    void FillClusters(List<KeyValuePair<EmbersEdge, List<TimedSpawn>>> coreTimed, int dungeon, float leftover, float window)
    {
        if (leftover <= 0f) return;

        var opts = new List<ClusterOption>();
        foreach (var kv in coreTimed)
        {
            string collName = CollectionFor(kv.Key, dungeon);
            WaveCollection coll = string.IsNullOrEmpty(collName) ? null : waveAuthoring.GetCollection(dungeon, collName);
            if (coll == null || coll.clusters == null) continue;
            var list = new List<ClusterPlan>();
            foreach (ClusterPlan cl in coll.clusters)
                if (WaveAuthoringSO.ClusterPoints(cl) > 0f) list.Add(cl);
            if (list.Count > 0) opts.Add(new ClusterOption { timed = kv.Value, clusters = list, favour = Mathf.Max(1, coll.clusterFavour) });
        }

        // Phase A — selection. Smooth weighted round-robin (à la nginx): each step every option gains
        // its favour, the highest-credit option is picked and pays back the total favour. A favour of 2
        // gets picked twice as often as a favour-1 option, but the picks land spread across the cycle
        // rather than back-to-back.
        var selected = new List<KeyValuePair<List<TimedSpawn>, ClusterPlan>>();
        int guard = 0;
        while (leftover > 0f && opts.Count > 0 && guard++ < 5000)
        {
            int totalFavour = 0;
            ClusterOption o = null;
            foreach (var c in opts)
            {
                c.credit += c.favour;
                totalFavour += c.favour;
                if (o == null || c.credit > o.credit) o = c;
            }
            o.credit -= totalFavour;

            o.clusters.RemoveAll(cl => WaveAuthoringSO.ClusterPoints(cl) > leftover);
            if (o.clusters.Count == 0) { opts.Remove(o); continue; }
            ClusterPlan pick = WeightedPick(o.clusters);
            selected.Add(new KeyValuePair<List<TimedSpawn>, ClusterPlan>(o.timed, pick));
            leftover -= WaveAuthoringSO.ClusterPoints(pick);
        }
        if (selected.Count == 0) return;

        // Phase B — even placement by cumulative price (constant rate). Shuffle first so cores interleave.
        float total = 0f;
        foreach (var s in selected) total += WaveAuthoringSO.ClusterPoints(s.Value);
        for (int i = selected.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (selected[i], selected[j]) = (selected[j], selected[i]); }
        float running = 0f;
        foreach (var s in selected)
        {
            float start = total > 0f ? running / total * window : 0f;
            AppendGroup(s.Key, s.Value.enemies, start, s.Value.duration, false);
            running += WaveAuthoringSO.ClusterPoints(s.Value);
        }
    }

    // Weighted random pick among clusters by their per-cluster favour (defaults to uniform).
    private static ClusterPlan WeightedPick(List<ClusterPlan> clusters)
    {
        float total = 0f;
        foreach (var cl in clusters) total += Mathf.Max(0f, cl.favour);
        if (total <= 0f) return clusters[Random.Range(0, clusters.Count)];
        float r = Random.Range(0f, total);
        foreach (var cl in clusters)
        {
            r -= Mathf.Max(0f, cl.favour);
            if (r <= 0f) return cl;
        }
        return clusters[clusters.Count - 1];
    }

    private class ClusterOption { public List<TimedSpawn> timed; public List<ClusterPlan> clusters; public int favour = 1; public int credit; }

    // Wave intensity is rolled once per cycle — by whichever of the forecast / arm / skip happens
    // first — and the difficulty bookkeeping (sinceLastBigAttack) is committed at the same moment.
    // Roll the wave's intensity once per cycle. waveRand is the budget multiplier; the roll's percentile
    // within its difficulty-dependent range picks the activity category (vocabulary) and the 0.45–1
    // visual level.  rand = max(0.9, Random.Range(0.5 + d/4, 1 + d/3)).
    private void EnsureActivityRolled()
    {
        if (activityRolled) return;
        activityRolled = true;
        float d = Mathf.Clamp(SetM.difficulty, 1f, 3f);
        float lo = 0.5f + d / 4f;
        float hi = 1f + d / 3f;
        float r = Random.Range(lo, hi);
        waveRand = Mathf.Max(0.9f, r);
        float p = hi > lo ? Mathf.Clamp01((r - lo) / (hi - lo)) : 0.5f; // percentile within the range
        activityLevel = Mathf.Lerp(0.45f, 1f, p);
        activityCategory = ClassifyActivity(p, d);
        Debug.Log($"[Wave] day {eraWaveIndex + 1}: rand={waveRand:0.00} (roll {r:0.00} in [{lo:0.00},{hi:0.00}]), activity tier {activityCategory}, difficulty {d:0.0}");
    }

    // Map a roll percentile p∈[0,1] to an activity tier (0 Weakly … 3 Extremely). Tier widths shift with
    // difficulty, interpolated between authored anchors at d = 1/1.5/2/2.5/3 — so ≤1.5 never reaches
    // Extremely and ≥2.5 never reaches Weakly (those tiers interpolate to zero width there).
    private static int ClassifyActivity(float p, float d)
    {
        float[] xs = { 1f, 1.5f, 2f, 2.5f, 3f };
        float[] wf = { 0.5f, 0.25f, 0.1f, 0f, 0f };
        float[] af = { 1f / 3f, 0.5f, 0.4f, 0.25f, 1f / 6f };
        float[] vf = { 1f / 6f, 0.25f, 0.4f, 0.5f, 1f / 3f };
        float cW = LerpAnchors(xs, wf, d);
        float cA = cW + LerpAnchors(xs, af, d);
        float cV = cA + LerpAnchors(xs, vf, d);
        p = Mathf.Clamp(p, 0f, 0.999999f);
        if (p < cW) return 0;
        if (p < cA) return 1;
        if (p < cV) return 2;
        return 3;
    }

    private static float LerpAnchors(float[] xs, float[] ys, float x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[xs.Length - 1]) return ys[ys.Length - 1];
        for (int i = 1; i < xs.Length; i++)
            if (x <= xs[i])
                return Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / (xs[i] - xs[i - 1]));
        return ys[ys.Length - 1];
    }

    public void HideWavePreview()
    {
        previewActive = false;
        if (EnemyTracker.i != null) EnemyTracker.i.HidePreview();
    }

    /// <summary>
    /// Summon the armed wave (V key / Tele-Phone). Returns false if there is nothing to summon
    /// (must do a dungeon run first, or a wave is already active). Executes the exact plan that the
    /// preview showed.
    /// </summary>
    public bool TryStartWave()
    {
        if (dayState != DayState.Day || !waveArmed) return false;
        waveArmed = false;
        HideWavePreview();
        // Hand each core its pre-rolled list; clear any core not in this plan so it spawns nothing.
        foreach (EmbersEdge EE in EEs)
        {
            EE.plannedSpawns = null;
        }
        if (currentPlan != null)
        {
            foreach (CorePlan cp in currentPlan.cores)
            {
                if (cp != null && cp.core != null) cp.core.plannedSpawns = cp.spawns;
            }
        }
        SetPreAttack();
        return true;
    }

    void SetPreAttack()
    {
        if (!RefreshManager.i.CASUALNOTREALTIME && PortalScript.i.inDungeon) //IF REGULAR MODE, WE DON'T SET PRE-ATTACK WHEN DYING TO EE... BECAUSE IT JUST HAPPENS ANYWAY FOR SOME REASON?
        {
            // Mid-core-arena fights don't launch the base wave. Mine dungeon: an active, uncleared
            // core pocket. Old DM dungeon: an undefeated room with a live EE. (The old unguarded
            // DM.i.activeRoom dereference NRE'd in the mine world, killing the death-return chain —
            // no accelerated wave, and the aborted ToHomeSequence left the arrival half-finished.)
            if (MineDungeonManager.i != null)
            {
                var ap = MineDungeonManager.i.activePocket;
                if (ap != null && !ap.Cleared && ap.Instance != null && ap.Instance.template.hasCore) return;
            }
            else if (DM.i != null && DM.i.activeRoom != null && !DM.i.activeRoom.defeated && DM.i.activeRoom.EE != null) return;
        }
        eeactive = true;
        realWaveThisCycle = true;    // a genuine wave (not the start-up phantom) -> advances eraWaveIndex on completion
        PortalScript.i.NoPortal();   // lock the dungeon teleport for the duration of the wave (all start paths)
        MapManager.SetSpin(activityLevel);
        SetActivityText();

        foreach (EmberCannon ec in EmberCannon.ecs)
        {
            ec.Activate();
        }
        foreach (EmbersEdge EE in EEs)
        {
            EE.StartCoroutine(EE.Acco(activityLevel + EE.bias));
        }
        dayState = DayState.PreAttack;
        CameraScript.i?.ApplyDimensionScale();   // enemies imminent — adopt the wave zoom
        timer = EmbersEdge.warmUpTime;
        UpdateActivitySlider(activityLevel);
    }



    // Repurposed: waves are summoned manually now, so this only handles the DEATH punishment —
    // shorten the warm-up and auto-summon once the death teleport drops the player back at base
    // (consumed in PortalScript.PortalFR via forceStartOnReturn).
    public void AccelerateWave(bool dead)
    {
        if (!dead) return;
        EmbersEdge.warmUpTime = 5f;
        this.QA(() => EmbersEdge.warmUpTime = 20f, 15);
        if (PortalScript.i.inDungeon)
        {
            forceStartOnReturn = true;
        }
    }

    private void SetToBase()
    {
        if (GS.CS().InDungeon())
        {
            PortalScript.i.swapTeleIcon = true;
        }
        else
        {
            PortalScript.i.swapTeleIcon = false;
        }
        UpdateActivitySlider(0f);
    }

    private void Start()
    {
        UIManager.i.SetTelePhone(UIManager.TeleMode.Core, 1f);
        PortalScript.i.YesPortal();
        dayState = DayState.Attack;
        timer = -10f;
        eraWaveIndex = 0;
        Time.timeScale = RefreshManager.i.STANDARDTIME;
        GS.OnNewEra += (ctx) => { UIManager.i.UpdateDayText(day); timer = 20f; maxTimer = 10f; dayState = DayState.Day; waveCompleted = false; waveArmed = false; currentPlan = null; activityRolled = false; previewActive = false; eraWaveIndex = 0; realWaveThisCycle = false; };
    }

    public void Update()
    {
        if (timeText.isActiveAndEnabled) // for sake of tutorial
        {
            float save = timer;
            timer -= Time.deltaTime;
            switch (dayState)
            {
                case DayState.Day:
                    // Peaceful at base. The next wave is summoned MANUALLY (V / Tele-Phone) once it has
                    // been armed by a dungeon run — no countdown auto-start and no countdown-driven core
                    // shifting (positions are locked when the wave is armed so the preview stays honest).
                    // Exception: SPAWNTESTMODE keeps auto-cycling waves (no dungeon run) for balancing.
                    if (RefreshManager.i.SPAWNTESTMODE && !waveArmed && !eeactive && timer <= 0f)
                    {
                        ArmWave();
                        TryStartWave();
                        break;
                    }
                    // Normally the activity + Directors only begin once you RETURN from a dungeon (ArmWave).
                    // testingDefence forecasts the wave BEFORE a dungeon run so you can test base defence.
                    if (RefreshManager.i.TESTINGDEFENCE && !previewActive && !waveArmed && !eeactive && !inBossTransition
                        && !PortalScript.i.inDungeon && !PortalScript.goingToDungeon && EEs.Count > 0)
                    {
                        ShowPreDungeonPreview();
                    }
                    break;
                case DayState.PreAttack:
                    if (RefreshManager.i.INSTASPAWN)
                    {
                        timer = 0f;
                    }
                    if(timer <= 0f)
                    {
                        dayState = DayState.Attack;
                        timeText.text = "Ember's Edge Errupted";
                        Finder.TurnOnTurrets();
                        timeText.color = Color.Lerp(timeText.color, Color.blue, 0.5f);
                        helpedWithWave = !PortalScript.i.inDungeon;
                    }
                    break;
                case DayState.Attack:
                    if(timer > -5f)
                    {
                        break;
                    }
                    for(int i = 0; i < alives.Count; i++)
                    {
                        if (alives[i] == null)
                        {
                            alives.RemoveAt(i);
                            i--;
                        }
                    }
                    if(timer <= -40f)
                    {
                        timer = save;
                    }
                    if(alives.Count == 0 && EmbersEdge.CheckFinished())
                    {
                        timer = save;
                        NextDayFR();
                    }
                    break;
            }
        }
        for(int i = 0; i < timeScales.Count; i++)
        {
            if(Time.unscaledTime > timeScales[i].expire)
            {
                CancelTS(timeScales[i].ID);
            }
        }
    }

    public void NextDayFR()
    {
        if (!waveCompleted)
        {
            waveCompleted = true;
            CameraScript.QuickLeanDistort(0f,1f);
            SetToBase();
            StartCoroutine(InvokeWakeComplete());
            if (!PortalScript.i.inDungeon || helpedWithWave)
            {
                SetNextDay();
                PortalScript.goingHomeNow = false;
            }
            timeText.text = "Ember's Edge Inactive";
            timeText.color = new Color(0.849f, 0.849f, 0.849f);
            foreach (EmbersEdge EE in EEs)
            {
                EE.StartCoroutine(EE.MakeFX());
            }
            MapManager.DeSpin();
        }
    }


    void SetShiftEEs()
    {
        timeText.text = "Ember's Edge Shifting";
        timeText.color = new Color(0.1f, 0.1f, 1);
        sinceLastBigAttack += 0.01f * GS.Era1();
        foreach(EmbersEdge e in EEs)
        {
            e.Shift();
        }
    }

    private IEnumerator InvokeWakeComplete()
    {
        yield return new WaitForSeconds(2f);
        onWaveComplete.Invoke();
    }

    public void SetNextDay()
    {
        this.QA(() => eeactive = false, 2f);
        StartCoroutine(SetNextDayI());
        IEnumerator SetNextDayI()
        {
            int d = day;
            yield return new WaitForSeconds(1.25f);
            if(d != day)
            {
                yield break;
            }
            waveCompleted = false;
            dayState = DayState.Day;
            CameraScript.i?.ApplyDimensionScale();   // wave over — back to the peaceful base zoom
            waveArmed = false;      // wave defeated -> peaceful & disarmed; player dungeon-runs (V) or skips (Tele-Phone)
            currentPlan = null;     // start a fresh forecast for the new cycle
            activityRolled = false;
            previewActive = false;
            if (realWaveThisCycle) { eraWaveIndex++; realWaveThisCycle = false; } // advance to the next authored Day
            timer = 100f + 1.5f * timer + Random.Range(60f, 90f) + 90f * activityLevel; // how fast you beat the prev wave, random, activity
            maxTimer = timer;
            helpedWithWave = false;
            Finder.TurnOffTurrets();
            OnNewDay.Invoke();
            if(RefreshManager.i.DAILYORBBOUNTY)
            {
                CallSpawnOrbs(Vector2.zero,ResourceManager.instance.initResources);
            }
            UpdateEraSlider();
            if (RefreshManager.i.SPAWNTESTMODE)
            {
                timer = 11f;
                if (Random.Range(0, 3) == 0)
                {
                    foreach (EmbersEdge e in EEs)
                    {
                        e.Shift();
                    }
                }
            }
            if (RefreshManager.i.TESTINGDEFENCE)
            {
                // Re-roll a fresh activity + Directors right after the win so you can keep testing
                // defence (still no spawning — summon it with the Tele-Phone).
                eeactive = false;
                ShowPreDungeonPreview();
            }
        }
    }
    
    

    public void CallSpawnOrbs(Vector2 pos, int[] orbs, Transform p = null)
    {
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] > 0)
            {
                StartCoroutine(SpawnOrbs(pos, i.ToString(), orbs[i], p, false));
            }
        }
    }

    public void CallSpawnOrbs(Vector2 pos, float[] orbs, Transform p = null, bool fillHarvest = false)
    {
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] > 0)
            {
                int given = Mathf.FloorToInt(orbs[i]) + Mathf.FloorToInt(ResourceManager.debt[i]);
                float left = orbs[i] + ResourceManager.debt[i] - given;
                ResourceManager.debt[i] = left;
                StartCoroutine(SpawnOrbs(pos, i.ToString(), given, p, fillHarvest));
            }
        }
    }

    public IEnumerator SpawnOrbs(Vector2 pos, string orbType, int orbNum, Transform p, bool fillHarvest)
    {
        int index = -1;
        switch (orbType)
        {
            case "general":
                index = 0;
                break;
            case "druid":
                index = 1;
                break;
            case "engineer":
                index = 2;
                break;
            case "cult":
                index = 3;
                break;
            case "0":
                index = 0;
                break;
            case "1":
                index = 1;
                break;
            case "2":
                index = 2;
                break;
            case "3":
                index = 3;
                break;
        }
        for (int i = 0; i < orbNum; i++)
        {
            if (fillHarvest)
            {
                p = SoulHarvester.GetSpaceAll(index);
                if (p != null)
                {
                    var orb = orbPools[index].Get();
                    orb.transform.position = pos;
                    orb.transform.parent = p;
                    orb.GetComponent<OrbScript>().Harvest();
                    yield return null;
                }
                else
                {
                    break;
                }
            }
            else
            {
                var orb = orbPools[index].Get();
                orb.transform.position = pos;
                orb.transform.parent = p == null ? orbParent : p;
                yield return null;
            }
        }
    }

  
 
    public Transform FindParent(GS.Parent p)
    {
        return p switch
        {
            GS.Parent.allies => Allies,
            GS.Parent.enemies => Enemies,
            GS.Parent.enemyprojectiles => EnemyProjectiles,
            GS.Parent.allyprojectiles => AllyProjectiles,
            GS.Parent.fx => FX,
            GS.Parent.ee => EE,
            GS.Parent.buildings => AllyBuildings,
            GS.Parent.loot => Loot,
            GS.Parent.misc => Misc,
            GS.Parent.followers => Followers,
            _ => null
        };
    }

    public int NewTS(float ts, float duration)
    {
        bool cont = false;
        int ID = 0;
        while (!cont)
        {
            cont = true;
            ID = Random.Range(0, 100);
            foreach (TS t in timeScales)
            {
                if(t.ID == ID)
                {
                    cont = false;
                }
            }
        }
        timeScales.Add(new TS(ts, duration, ID));
        SetTimeScale();
        return ID;
    }

    private void SetTimeScale()
    {
        float min = 10;
        foreach (TS t in timeScales)
        {
            min = (t.ts < min) ? t.ts : min;
        }
        if (min == 10)
        {
            min = RefreshManager.i.STANDARDTIME;
        }
        Time.timeScale = min;
    }

    public void CancelTS(int ID)
    {
        for(int i = 0; i < timeScales.Count; i++)
        {
            if(timeScales[i].ID == ID)
            {
                timeScales.RemoveAt(i);
                continue;
            }
        }
        SetTimeScale();
    }

    public GameObject NewP(GameObject proj, Transform t, string ta, float innaccuracy = 0f, float strength = 0f)
    {
        innaccuracy /= 2;
        Vector2 perp = 0.5f * Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(t.up);
        Vector2 dire = (Vector2)t.up + perp;
        var p = Instantiate(proj, t.position, Quaternion.identity, GS.ProjectileParent(ta));
        p.GetComponent<ProjectileScript>().SetValues(dire, ta,strength,t);
        return p;
    }
    
    /// <summary>
    /// I'm efficient
    /// </summary>
    public static ProjectileScript NewP(ProjectileScript ps, Transform t, string ta, float innaccuracy = 0f, float strength = 0f)
    {
        innaccuracy /= 2;
        var up = t.up;
        Vector2 perp = 0.5f * Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(up);
        Vector2 dire = (Vector2)up + perp;
        var p = Instantiate(ps, t.position, Quaternion.identity, GS.ProjectileParent(ta));
        p.SetValues(dire, ta,strength,t);
        return p;
    }
    
    public GameObject NewP(GameObject proj, Transform t, string ta, Vector2 dir, float innaccuracy = 0f, float strength = 0f)
    {
        if (proj == null || t == null) return null;

        Vector2 dire;
        if (dir == Vector2.zero && innaccuracy != 0)
        {
            dire = Random.insideUnitCircle.normalized;
        }
        else
        {
            innaccuracy /= 2;
            Vector2 perp = 0.5f * Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(dir).normalized;
            dire = dir.normalized + perp;
        }

        var p = Instantiate(proj, t.position, Quaternion.identity, GS.ProjectileParent(ta));

        // Standard round: ProjectileScript owns launch + faction re-tag/layer.
        if (p.TryGetComponent<ProjectileScript>(out var ps))
        {
            ps.SetValues(dire, ta, strength, t);
            return p;
        }

        // Homing seeking round (HomingDart + Seeking, NO ProjectileScript): re-tag it to the faction so its
        // Seeking hunts that faction's foes, give it an opening velocity along `dire`, and let Seeking
        // auto-acquire the nearest target. Used by the Backlash Pods / Pelter Cache reusing the Pelter dart.
        p.tag = ta;
        if (p.TryGetComponent<Seeking>(out var seek)) seek.target = null;
        if (p.TryGetComponent<Rigidbody2D>(out var rb))
            rb.linearVelocity = dire.normalized * (strength > 0f ? strength * 5f : 5f);
        return p;
    }

    public void MakeVulnerable(int vulnerableType, Transform t, Vector2 pos)
    {
         Instantiate(vulnerables[vulnerableType], pos, Quaternion.identity, t);
    }

    private void UpdateActivitySlider(float val)
    {
        StartCoroutine(UpdateActivitySliderI(1 - ((val - 0.45f) / 0.65f)));

        IEnumerator UpdateActivitySliderI(float val)
        {
            for(float i = 0f; i < 2f; i += Time.deltaTime)
            {
                yield return null;
                activitySlider.value = Mathf.Lerp(activitySlider.value, val, Time.deltaTime * 4f * i);
            }
        }
    }

    private void UpdateEraSlider()
    {
        float val = 1f - GS.EraCompletion();
        val = Mathf.Lerp(val, 1f, 0.5f * val);
        StartCoroutine(UpdateEraSliderI(val));

        IEnumerator UpdateEraSliderI(float val)
        {
            for (float i = 0f; i < 4f; i += Time.deltaTime)
            {
                yield return null;
                eraCompletionSlider.value = Mathf.Lerp(eraCompletionSlider.value, val, Time.deltaTime * 1f * i);
            }
        }
    }

    public struct TS
    {
        public TS(float tsP, float durationP, int IDP)
        {
            ts = tsP * RefreshManager.i.STANDARDTIME;
            expire = Time.unscaledTime + durationP;
            ID = IDP;
        }

        public float ts { get; }
        public float expire { get; }
        public int ID { get; }
    }
}