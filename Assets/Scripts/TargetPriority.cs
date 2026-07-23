using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-building enemy targeting priority. Strategy picks WHERE the tower looks — closest to
/// itself (the old behaviour, still the default), closest to / furthest from the base (world
/// origin) — and the Preference strategy adds a WHO ranking on top: strongest, weakest, ranged,
/// melee, or a custom per-enemy-type filter built in <see cref="TargetFilterUI"/>. Preferences
/// are soft: when nothing matches, the tower falls back to plain closest so it never idles while
/// something is in range. Wire it with <see cref="Attach"/> from a turret's Start — it adds the
/// zero-cost tiles to the standard building UI (the preference row only shows under Preference)
/// and, for Finder-based towers, plants itself as <see cref="Finder.priority"/>.
/// </summary>
public class TargetPriority : MonoBehaviour
{
    public enum Strategy { ClosestToTower, ClosestToBase, FurthestFromBase, Preference }
    public enum Pref { None, Strongest, Weakest, Ranged, Melee, Custom }

    public Strategy strategy = Strategy.ClosestToTower;
    public Pref preference = Pref.None;

    /// <summary>Prefab-name keys the Custom preference favours (edited by TargetFilterUI).</summary>
    public readonly HashSet<string> customKeys = new HashSet<string>();

    /// <summary>False only for the default strategy — callers keep their original (cheaper) scan.</summary>
    public bool Overriding => strategy != Strategy.ClosestToTower;

    Building building;
    readonly List<(BaseTile tile, Strategy s)> stratTiles = new List<(BaseTile, Strategy)>();
    readonly List<(BaseTile tile, Pref p)> prefTiles = new List<(BaseTile, Pref)>();
    readonly Dictionary<BaseTile, Color> baseCols = new Dictionary<BaseTile, Color>();

    static readonly Color ActiveCol = new Color(0.30f, 0.85f, 0.45f, 0.85f);

    public static TargetPriority Attach(Building b, Finder f = null)
    {
        var tp = b.GetComponent<TargetPriority>();
        if (tp == null) tp = b.gameObject.AddComponent<TargetPriority>();
        tp.building = b;
        if (f != null) f.priority = tp;
        if (tp.stratTiles.Count == 0) tp.WireTiles();
        return tp;
    }

    // ─────────────────────────────── selection ───────────────────────────────

    /// <summary>Turret-facing find: the original nearest scan when no priority is set, else the
    /// prioritised one. Drop-in for GS.FindNearestEnemy(tag, pos, radius, false).</summary>
    public Transform Find(string tagP, Vector2 pos, float radius, bool allowOther = true)
        => Overriding ? GS.FindEnemyPrioritized(tagP, pos, radius, this, allowOther)
                      : GS.FindNearestEnemy(tagP, pos, radius, false, allowOther);

    /// <summary>Best unit among the overlap candidates for the current strategy/preference.
    /// Called by GS.FindEnemyPrioritized with its raw overlap buffer.</summary>
    public Transform PickBest(Collider2D[] buf, int n, Vector2 towerPos)
    {
        Transform best = null;
        float bestPrim = float.NegativeInfinity, bestSec = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            Collider2D c = buf[i];
            if (c == null || c.isTrigger || c.attachedRigidbody == null) continue;
            Transform t = c.attachedRigidbody.transform;
            // closest-to-tower is the universal tie-break, so equal-priority targets behave
            // exactly like the old nearest scan
            float sec = -((Vector2)c.transform.position - towerPos).sqrMagnitude;
            float prim = PrimaryScore(c, t, sec);
            if (prim > bestPrim || (prim == bestPrim && sec > bestSec))
            {
                bestPrim = prim;
                bestSec = sec;
                best = t;
            }
        }
        return best;
    }

    float PrimaryScore(Collider2D c, Transform root, float negDistSqr)
    {
        switch (strategy)
        {
            case Strategy.ClosestToBase: return -((Vector2)root.position).sqrMagnitude;
            case Strategy.FurthestFromBase: return ((Vector2)root.position).sqrMagnitude;
            case Strategy.Preference:
                switch (preference)
                {
                    case Pref.Strongest:
                    {
                        LifeScript ls = GS.ColToLS(c);
                        return ls != null ? ls.maxHp : float.MinValue * 0.25f;
                    }
                    case Pref.Weakest:
                    {
                        LifeScript ls = GS.ColToLS(c);
                        return ls != null ? -ls.maxHp : float.MinValue * 0.25f;
                    }
                    case Pref.Ranged: return IsRanged(root) ? 1f : 0f;
                    case Pref.Melee: return IsRanged(root) ? 0f : 1f;
                    case Pref.Custom:
                        return customKeys.Count > 0 && customKeys.Contains(Key(root)) ? 1f : 0f;
                    default: return negDistSqr; // Pref.None — plain closest
                }
            default: return negDistSqr; // Strategy.ClosestToTower
        }
    }

    // ─────────────────────── ranged / melee classification ───────────────────────

    // There is no shared ranged flag on Unit — classification is by enemy script type
    // (projectile-firing types, from a sweep of every E*/Jelly Shoot()/proj usage).
    static readonly HashSet<string> RangedTypes = new HashSet<string>
    {
        "E1_0", "E1_1", "E1_2", "E1_4", "E1_6", "E1_7", "E1_8",
        "E2_2", "E2_3", "E2_6", "E2_7", "Jelly2"
    };

    static readonly Dictionary<string, bool> rangedCache = new Dictionary<string, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => rangedCache.Clear();   // no-domain-reload hygiene

    public static bool IsRanged(Transform root)
    {
        string k = root.name;   // "(Clone)" names are shared per type, so one lookup per type
        if (rangedCache.TryGetValue(k, out bool r)) return r;
        r = false;
        foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>())
        {
            if (mb != null && RangedTypes.Contains(mb.GetType().Name))
            {
                r = true;
                break;
            }
        }
        rangedCache[k] = r;
        return r;
    }

    /// <summary>Per-type key for the custom filter: the spawn prefab's name (instances are
    /// "Name(Clone)"). TargetFilterUI lists the same keys off the MarauderSO roster.</summary>
    public static string Key(Transform root)
    {
        string n = root.name;
        int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
        return i < 0 ? n : n.Substring(0, i).TrimEnd();
    }

    // ─────────────────────────────── tile UI ───────────────────────────────

    static readonly int[] Free = new int[4];   // read-only zero cost shared by all setting tiles

    void WireTiles()
    {
        AddStrat("Closest To Tower", TPIcons.ClosestTower, Strategy.ClosestToTower);
        AddStrat("Closest To Base", TPIcons.ClosestBase, Strategy.ClosestToBase);
        AddStrat("Furthest From Base", TPIcons.FurthestBase, Strategy.FurthestFromBase);
        AddStrat("Preference", TPIcons.Preference, Strategy.Preference);

        AddPref("No Preference", TPIcons.NoPref, Pref.None);
        AddPref("Strongest", TPIcons.Strongest, Pref.Strongest);
        AddPref("Weakest", TPIcons.Weakest, Pref.Weakest);
        AddPref("Ranged", TPIcons.Ranged, Pref.Ranged);
        AddPref("Melee", TPIcons.Melee, Pref.Melee);
        AddPref("Custom", TPIcons.Custom, Pref.Custom, () => TargetFilterUI.Open(this));

        RefreshTiles();
    }

    void AddStrat(string nam, Sprite spr, Strategy st)
    {
        building.AddSlot(Free, nam, spr, false, () => SetStrategy(st));
        BaseTile tile = building.tiles[^1];
        stratTiles.Add((tile, st));
        baseCols[tile] = tile.init;
    }

    void AddPref(string nam, Sprite spr, Pref p, Action extra = null)
    {
        building.AddSlot(Free, nam, spr, false, () => { SetPreference(p); extra?.Invoke(); },
            false, null, null, () => strategy == Strategy.Preference);
        BaseTile tile = building.tiles[^1];
        prefTiles.Add((tile, p));
        baseCols[tile] = tile.init;
    }

    void SetStrategy(Strategy st)
    {
        strategy = st;
        building.UpdateUI();   // the preference row appears/disappears with the Preference strategy
        RefreshTiles();
    }

    void SetPreference(Pref p)
    {
        preference = p;
        RefreshTiles();
    }

    void RefreshTiles()
    {
        foreach ((BaseTile tile, Strategy s) in stratTiles) Paint(tile, strategy == s);
        foreach ((BaseTile tile, Pref p) in prefTiles) Paint(tile, preference == p);
    }

    void Paint(BaseTile t, bool on)
    {
        if (t == null) return;
        // init is repainted too so BaseTile's hover/click colour lerps settle on the right tint
        Color c = on ? Color.Lerp(baseCols[t], ActiveCol, 0.75f) : baseCols[t];
        t.init = c;
        t.background.color = c;
    }
}

/// <summary>Procedural white glyphs for the targeting tiles — no asset dependencies.</summary>
public static class TPIcons
{
    const int S = 64;
    static readonly Vector2 C = new Vector2(31.5f, 31.5f);

    static Sprite _tower, _base, _far, _pref, _none, _strong, _weak, _ranged, _melee, _custom;

    // crosshair: you (the tower) are the reference point
    public static Sprite ClosestTower => _tower != null ? _tower : _tower = Build(p =>
        Mathf.Max(Ring(p, C, 19f, 3f), Disc(p, C, 6f)));

    // arrow driving down into the base bar
    public static Sprite ClosestBase => _base != null ? _base : _base = Build(p =>
        Mathf.Max(RectA(p, 14f, 8f, 50f, 15f),
            Line(p, 32f, 52f, 32f, 27f, 3f),
            Line(p, 32f, 20f, 21f, 31f, 3f),
            Line(p, 32f, 20f, 43f, 31f, 3f)));

    // arrow escaping up away from the base bar
    public static Sprite FurthestBase => _far != null ? _far : _far = Build(p =>
        Mathf.Max(RectA(p, 14f, 8f, 50f, 15f),
            Line(p, 32f, 24f, 32f, 46f, 3f),
            Line(p, 32f, 53f, 21f, 42f, 3f),
            Line(p, 32f, 53f, 43f, 42f, 3f)));

    // funnel/filter
    public static Sprite Preference => _pref != null ? _pref : _pref = Build(p =>
        Mathf.Max(Line(p, 12f, 52f, 52f, 52f, 2.5f),
            Line(p, 13f, 50f, 28f, 33f, 2.5f),
            Line(p, 51f, 50f, 36f, 33f, 2.5f),
            Line(p, 32f, 33f, 32f, 12f, 3f)));

    // struck-through ring
    public static Sprite NoPref => _none != null ? _none : _none = Build(p =>
        Mathf.Max(Ring(p, C, 18f, 3f), Line(p, 20f, 20f, 44f, 44f, 3f)));

    // ascending bars
    public static Sprite Strongest => _strong != null ? _strong : _strong = Build(p =>
        Mathf.Max(RectA(p, 12f, 10f, 22f, 20f),
            RectA(p, 27f, 10f, 37f, 34f),
            RectA(p, 42f, 10f, 52f, 52f)));

    // descending bars
    public static Sprite Weakest => _weak != null ? _weak : _weak = Build(p =>
        Mathf.Max(RectA(p, 12f, 10f, 22f, 52f),
            RectA(p, 27f, 10f, 37f, 34f),
            RectA(p, 42f, 10f, 52f, 20f)));

    // projectile arrow
    public static Sprite Ranged => _ranged != null ? _ranged : _ranged = Build(p =>
        Mathf.Max(Line(p, 16f, 16f, 44f, 44f, 3f),
            Line(p, 48f, 48f, 33f, 44f, 3f),
            Line(p, 48f, 48f, 44f, 33f, 3f)));

    // crossed blades
    public static Sprite Melee => _melee != null ? _melee : _melee = Build(p =>
        Mathf.Max(Line(p, 17f, 15f, 47f, 49f, 3.5f),
            Line(p, 47f, 15f, 17f, 49f, 3.5f)));

    // slider rows
    public static Sprite Custom => _custom != null ? _custom : _custom = Build(p =>
        Mathf.Max(Line(p, 12f, 20f, 52f, 20f, 2.2f),
            Line(p, 12f, 32f, 52f, 32f, 2.2f),
            Line(p, 12f, 44f, 52f, 44f, 2.2f),
            Disc(p, new Vector2(26f, 20f), 4.5f),
            Disc(p, new Vector2(42f, 32f), 4.5f),
            Disc(p, new Vector2(22f, 44f), 4.5f)));

    // signed-distance primitives with ~1px anti-aliased edges
    static float Alpha(float sd) => Mathf.Clamp01(0.5f - sd);

    static float Disc(Vector2 p, Vector2 c, float r) => Alpha(Vector2.Distance(p, c) - r);

    static float Ring(Vector2 p, Vector2 c, float r, float w) =>
        Alpha(Mathf.Abs(Vector2.Distance(p, c) - r) - w * 0.5f);

    static float Line(Vector2 p, float x0, float y0, float x1, float y1, float w)
    {
        Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1);
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Alpha(Vector2.Distance(p, a + t * ab) - w * 0.5f);
    }

    static float RectA(Vector2 p, float x0, float y0, float x1, float y1) =>
        Alpha(-Mathf.Min(Mathf.Min(p.x - x0, x1 - p.x), Mathf.Min(p.y - y0, y1 - p.y)));

    static Sprite Build(Func<Vector2, float> a)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a(new Vector2(x, y)))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }
}
