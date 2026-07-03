using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click builder for the era-1 "wave extras". Generates a prefab + a MarauderSO for each extra and
/// wires them into WaveAuthoring.dungeon1Extras so they appear in the Wave Forge "Extras" row. Two families:
///   • the bonus SentryExtra set (beam / line-of-sight traps), reusing BossP0 disc + E1-2 P waggler shots;
///   • the briefed WaveExtra set — a TRAP (Ember Snare), a NEUTRAL hazard (Cinder Geode) and a
///     player-FRIENDLY node (Verdant Wellspring), which bake their own on-palette art in code.
///
/// Idempotent: re-running overwrites the prefabs/SOs in place and re-points the palette. Run it after
/// the scripts compile (Tools > Build Era-1 Extras), then open Tools > Wave Forge and paint them in.
/// </summary>
public static class Era1ExtrasBuilder
{
    const string PrefabDir = "Assets/Prefabs/Extras";
    const string SODir = "Assets/ScriptableObjects/Extras";
    const string DiscPath = "Assets/Prefabs/Enemies/E1/BossP0.prefab";
    const string WagglerPath = "Assets/Prefabs/Enemies/E1/E1-2 P.prefab";
    const string DartPath = "Assets/Prefabs/AllyProjectiles/HomingDart.prefab";       // Pelter bullets[0]
    const string BigDartPath = "Assets/Prefabs/AllyProjectiles/BigHomingDart.prefab"; // Pelter bullets[1]
    const string WavePath = "Assets/Resources/WaveAuthoring.asset";

    [MenuItem("Tools/Build Era-1 Extras")]
    public static void Build()
    {
        EnsureFolder("Assets/Prefabs", "Extras");
        EnsureFolder("Assets/ScriptableObjects", "Extras");

        var disc = AssetDatabase.LoadAssetAtPath<GameObject>(DiscPath);
        var waggler = AssetDatabase.LoadAssetAtPath<GameObject>(WagglerPath);
        if (disc == null || waggler == null)
        {
            EditorUtility.DisplayDialog("Build Era-1 Extras",
                "Missing source projectiles:\n" + (disc == null ? DiscPath + "\n" : "") +
                (waggler == null ? WagglerPath : ""), "OK");
            return;
        }

        // The Pelter's seeking rounds, reused by the reactive pods + the pelter cache (homing).
        var dart = AssetDatabase.LoadAssetAtPath<GameObject>(DartPath);
        var bigDart = AssetDatabase.LoadAssetAtPath<GameObject>(BigDartPath);
        if (dart == null || bigDart == null)
            Debug.LogWarning("Era1ExtrasBuilder: missing Pelter rounds (" + DartPath + " / " + BigDartPath +
                             ") — Backlash Pods & Pelter Cache will spawn without projectiles.");

        // Reuse the disc's sprite + glow material as the shared on-palette body (greyscale emission via
        // `thecolor`). The runtime scripts instance the material and re-tint it per state.
        var srcSR = disc.GetComponentInChildren<SpriteRenderer>(true);
        Sprite bodySpr = srcSR != null ? srcSR.sprite : null;
        Material bodyMat = srcSR != null ? srcSR.sharedMaterial : null;

        var sos = new List<MarauderSO>();

        // (1) Disc Dropper — interior hunter: drops a ring of discs on the player.
        var beacon = Build<AirDropBeacon>("Disc Dropper", bodySpr, bodyMat, srcSR, 0.85f, 0.5f, t =>
        {
            t.wallMounted = false; t.interiorDepth = 5f;
            t.discProjectile = disc; t.detectRadius = 4f; t.discCount = 12;
        });
        sos.Add(MakeSO("Disc Dropper", beacon, 8, 2));

        // (2) Beam Trap ×4 — fixed 15u sightlines fanned across the rim; fire when crossed.
        float[] fan = { -50f, -17f, 17f, 50f };
        string[] tags = { "L", "ML", "MR", "R" };
        for (int i = 0; i < fan.Length; i++)
        {
            float ang = fan[i];
            var sentinel = Build<TripwireSentinel>("Beam Trap " + tags[i], bodySpr, bodyMat, srcSR, 0.6f, 0.4f, t =>
            {
                t.wallMounted = true; t.wagglerProjectile = waggler;
                t.beamLength = 15f; t.fireDelay = 1f; t.aimAngle = ang;
            });
            sos.Add(MakeSO("Beam Trap " + tags[i], sentinel, 5, 1));
        }

        // (3) Beam Sweeper — sweeps an 8u beam in place (searchlight); sweepArc is a half-angle in degrees.
        var sweeper = Build<SweeperSentry>("Beam Sweeper", bodySpr, bodyMat, srcSR, 0.6f, 0.4f, t =>
        {
            t.wallMounted = true; t.wagglerProjectile = waggler;
            t.beamLength = 8f; t.fireDelay = 1f; t.sweepSpeed = 2f; t.sweepArc = 55f;
        });
        sos.Add(MakeSO("Beam Sweeper", sweeper, 7, 2));

        // (4) Close Beam — short 4u beam, fires toward the player up close.
        var lurker = Build<LurkerSentry>("Close Beam", bodySpr, bodyMat, srcSR, 0.55f, 0.4f, t =>
        {
            t.wallMounted = true; t.wagglerProjectile = waggler;
            t.beamLength = 4f; t.fireDelay = 0.9f;
        });
        sos.Add(MakeSO("Close Beam", lurker, 6, 1));

        // (5) Pull Trap — interior pull + slow + point-blank disc burst.
        var snare = Build<LodestoneSnare>("Pull Trap", bodySpr, bodyMat, srcSR, 0.8f, 0.5f, t =>
        {
            t.wallMounted = false; t.interiorDepth = 6f;
            t.discProjectile = disc; t.detectRadius = 5f; t.chargeTime = 1.5f; t.discCount = 14;
        });
        sos.Add(MakeSO("Pull Trap", snare, 9, 2));

        // ---- Briefed interactive set (richer code-baked-art WaveExtra base): trap + neutral + friendly ----
        // (6) Root Trap — interior anti-enemy spring trap: roots the trigger-er + ember burst.
        var snareT = BuildBriefed<EmberSnare>("Root Trap", srcSR, 1f, 0.45f, t =>
        { t.wallMounted = false; t.interiorDepth = 5f; t.maxLifetime = 22f; });
        sos.Add(MakeSO("Root Trap", snareT, 6, 1));

        // (7) Bomb Crystal — neutral volatile crystal: any faction's fire breaks it -> blast both sides + loot.
        var geode = BuildBriefed<CinderGeode>("Bomb Crystal", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 30f; });
        sos.Add(MakeSO("Bomb Crystal", geode, 8, 2));

        // (8) Orb Plant — player-friendly: shoot to harvest green orbs + energy, heals on wilt.
        var well = BuildBriefed<VerdantWellspring>("Orb Plant", srcSR, 1f, 0.5f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 28f; });
        sos.Add(MakeSO("Orb Plant", well, 6, 2));

        // ============================ Recovered briefed set (Agent A/B handoff) ============================

        // (9-11) Backlash Pod ×3 — neutral reactive burst; the FIRST thing to damage it pops a homing-dart
        // ring AWAY from the striker, the darts re-tagged to the striker's faction (player -> hunt enemies;
        // enemy -> hunt the player). base / small / large differ only by count, scale and round.
        var pod = BuildBriefed<BacklashPod>("Backlash Pod", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 5f; t.maxLifetime = 24f; t.seekProjectile = dart; t.projectileCount = 14; });
        sos.Add(MakeSO("Backlash Pod", pod, 8, 2));

        var podSmall = BuildBriefed<BacklashPod>("Backlash Pod (Small)", srcSR, 0.7f, 0.42f, t =>
        { t.wallMounted = false; t.interiorDepth = 5f; t.maxLifetime = 24f; t.seekProjectile = dart; t.projectileCount = 8; });
        sos.Add(MakeSO("Backlash Pod (Small)", podSmall, 5, 1));

        var podLarge = BuildBriefed<BacklashPod>("Backlash Pod (Large)", srcSR, 1.45f, 0.85f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 26f; t.seekProjectile = bigDart; t.projectileCount = 22; t.projectileStrength = 2f; });
        sos.Add(MakeSO("Backlash Pod (Large)", podLarge, 12, 3));

        // (12) Pelter Cache — ally-only: shoot it to crack 7 Pelter rounds loose (12 HP, one per 2-HP step).
        var cache = BuildBriefed<PelterCache>("Pelter Cache", srcSR, 1.1f, 0.6f, t =>
        { t.wallMounted = false; t.interiorDepth = 5f; t.maxLifetime = 26f; t.roundProjectile = dart; t.bigRoundProjectile = bigDart; });
        sos.Add(MakeSO("Pelter Cache", cache, 9, 2));

        // (13-16) Orb Fount ×4 — ally-only orb harvester with character; shoot to spit colour orbs that flee
        // the player, each variant leaving a different wake (white scatter / green slow / blue stun / red mine).
        var fW = BuildBriefed<OrbFount>("Orb Fount (White)", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 13f; t.variant = OrbFount.FountColor.White; });
        sos.Add(MakeSO("Orb Fount (White)", fW, 6, 1));
        var fG = BuildBriefed<OrbFount>("Orb Fount (Green)", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 13f; t.variant = OrbFount.FountColor.Green; });
        sos.Add(MakeSO("Orb Fount (Green)", fG, 7, 2));
        var fB = BuildBriefed<OrbFount>("Orb Fount (Blue)", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 13f; t.variant = OrbFount.FountColor.Blue; });
        sos.Add(MakeSO("Orb Fount (Blue)", fB, 8, 2));
        var fR = BuildBriefed<OrbFount>("Orb Fount (Red)", srcSR, 1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 13f; t.variant = OrbFount.FountColor.Red; });
        sos.Add(MakeSO("Orb Fount (Red)", fR, 9, 3));

        // (17) Resonance Totem — ally-only: charge it with fire, then it discharges a slow + knockback nova.
        var totem = BuildBriefed<ResonanceTotem>("Resonance Totem", srcSR, 1.1f, 0.55f, t =>
        { t.wallMounted = false; t.interiorDepth = 6f; t.maxLifetime = 26f; });
        sos.Add(MakeSO("Resonance Totem", totem, 9, 2));

        // (18-21) Warden Sentry ×4 — concrete WallSentry: a fixed 15u watch-line that fires a Waggler once it
        // gets a clear look at the player. Four fixed bearings fan a stretch of rim with crossing sightlines.
        float[] wardenFan = { -45f, -15f, 15f, 45f };
        string[] wardenTags = { "FL", "L", "R", "FR" };
        for (int i = 0; i < wardenFan.Length; i++)
        {
            float ang = wardenFan[i];
            var warden = Build<WardenSentry>("Warden Sentry " + wardenTags[i], bodySpr, bodyMat, srcSR, 0.6f, 0.4f, t =>
            {
                t.wallMounted = true; t.wagglerProjectile = waggler;
                t.beamLength = 15f; t.fireDelay = 1.1f; t.aimAngle = ang;
            });
            sos.Add(MakeSO("Warden Sentry " + wardenTags[i], warden, 6, 1));
        }

        // Wire the palette (era 1 == dungeon index 0).
        var wave = AssetDatabase.LoadAssetAtPath<WaveAuthoringSO>(WavePath);
        if (wave != null)
        {
            wave.dungeon1Extras = sos.ToArray();
            EditorUtility.SetDirty(wave);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Build Era-1 Extras",
            "Built " + sos.Count + " extras into " + PrefabDir + " / " + SODir +
            (wave != null ? "\n\nWired into WaveAuthoring.dungeon1Extras — open Tools > Wave Forge, Dungeon 1, and paint them from the 'Extras' row."
                          : "\n\nWARNING: WaveAuthoring asset not found at " + WavePath + " — assign the SOs to dungeon1Extras manually."),
            "OK");
    }

    // Build a prefab: kinematic body + trigger collider + reused sprite/material + the SentryExtra script.
    static GameObject Build<T>(string name, Sprite spr, Material mat, SpriteRenderer src,
        float scale, float colRadius, System.Action<T> cfg) where T : SentryExtra
    {
        var go = new GameObject(name);
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = colRadius;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.sharedMaterial = mat;
        if (src != null) { sr.sortingLayerID = src.sortingLayerID; sr.sortingOrder = src.sortingOrder; }

        go.transform.localScale = Vector3.one * scale;

        var t = go.AddComponent<T>();
        cfg?.Invoke(t);

        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // Build a briefed WaveExtra: kinematic body + TRIGGER collider on the "Walls" layer (which the physics
    // matrix lets both projectile factions + units overlap) so its OnTriggerEnter2D fires for shots/bodies.
    // useFullKinematicContacts is required for a kinematic body to receive those trigger callbacks. The
    // script bakes its own sprite in Awake, so the placeholder sprite/material here are just stand-ins.
    static GameObject BuildBriefed<T>(string name, SpriteRenderer src, float scale, float colRadius,
        System.Action<T> cfg) where T : WaveExtra
    {
        var go = new GameObject(name);
        int walls = LayerMask.NameToLayer("Walls");
        if (walls >= 0) go.layer = walls;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.useFullKinematicContacts = true;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = colRadius;

        var sr = go.AddComponent<SpriteRenderer>();
        if (src != null) { sr.sprite = src.sprite; sr.sharedMaterial = src.sharedMaterial; sr.sortingLayerID = src.sortingLayerID; }

        go.transform.localScale = Vector3.one * scale;

        var t = go.AddComponent<T>();
        cfg?.Invoke(t);

        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static MarauderSO MakeSO(string name, GameObject prefab, int price, int rarity)
    {
        string path = $"{SODir}/{name}.asset";
        var so = AssetDatabase.LoadAssetAtPath<MarauderSO>(path);
        bool isNew = so == null;
        if (isNew) so = ScriptableObject.CreateInstance<MarauderSO>();
        so.prefab = prefab;
        so.price = price;
        so.rarity = rarity;
        so.isBuilding = false;
        if (isNew) AssetDatabase.CreateAsset(so, path);
        EditorUtility.SetDirty(so);
        return so;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }
}
