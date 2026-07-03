using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click builder for the era-1 MINE-POCKET status floor tiles — the persistent walk-over flooring
/// (stun / slow / speed / root / launch). For each it generates a thin prefab (just a SpriteRenderer + the
/// configured FloorTile script — the art is baked at runtime and detection is a distance poll, so no
/// collider/rigidbody is needed) and assigns the set to MineAuthoring.statusTilePrefabs, so they paint from
/// the Mine Forge TILES brush and spawn on pocket discovery.
///
/// Heal + Orb are NOT built here — they're one-shot pickups, so they live as OBJECTS (heal_box / orb_box)
/// built by Tools > Fill Mine Object Palette into the Extras folder and painted with the Object brush.
///
/// All art is baked in code (WaveExtra / FloorTile) — no PNGs. Idempotent: re-running overwrites the prefabs
/// in place and re-points statusTilePrefabs.
/// </summary>
public static class Era1TilesBuilder
{
    const string PrefabDir = "Assets/Prefabs/Extras/Tiles";
    const string MinePath = "Assets/Resources/MineAuthoring.asset";

    [MenuItem("Tools/Build Era-1 Mine Tiles")]
    public static void Build()
    {
        EnsureFolder("Assets/Prefabs", "Extras");
        EnsureFolder("Assets/Prefabs/Extras", "Tiles");

        var stun   = BuildTile<StunTile>  ("StunTile",   t => { t.radius = 0.95f; t.effectDuration = 1.5f; t.magnitude = 0f;    t.rearm = 3f; });
        var slow   = BuildTile<SlowTile>  ("SlowTile",   t => { t.radius = 1.15f; t.effectDuration = 0.7f; t.magnitude = 0.5f;  t.rearm = 0f; });
        var speed  = BuildTile<SpeedTile> ("SpeedTile",  t => { t.radius = 1.15f; t.effectDuration = 0.7f; t.magnitude = 1.5f;  t.rearm = 0f; });
        var root   = BuildTile<RootTile>  ("RootTile",   t => { t.radius = 0.95f; t.effectDuration = 1.5f; t.magnitude = 0f;    t.rearm = 3f; });
        var launch = BuildTile<LaunchTile>("LaunchTile", t => { t.radius = 0.90f; t.effectDuration = 0f;   t.magnitude = 13f;   t.rearm = 2f; });

        // The 5 STATUS tiles are painted in the Mine Forge "Tiles" brush and spawned by the generator at their
        // cells -> MineAuthoring.statusTilePrefabs (index = PocketTileType.Stun..Launch). Heal + Orb are one-shot
        // pickups -> built as Objects (heal_box / orb_box) by Tools > Fill Mine Object Palette, not here.
        var statusTiles = new[] { stun, slow, speed, root, launch };

        var mine = AssetDatabase.LoadAssetAtPath<MineAuthoringSO>(MinePath);
        if (mine != null)
        {
            mine.statusTilePrefabs = statusTiles;
            EditorUtility.SetDirty(mine);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Build Era-1 Mine Tiles",
            "Built 5 status floor tiles into\n" + PrefabDir +
            (mine != null
                ? "\n\nStun/Slow/Speed/Root/Launch → MineAuthoring.statusTilePrefabs (paint them in the Mine Forge TILES brush — they spawn on pocket discovery). For the heal_box / orb_box pickups run Tools > Fill Mine Object Palette (Object brush; set their PlacedObject.chance, e.g. 0.2)."
                : "\n\nWARNING: MineAuthoring asset not found at " + MinePath + " — assign statusTilePrefabs manually."),
            "OK");
    }

    // A thin shell prefab: SpriteRenderer (the tile bakes its glyph at runtime) + the configured tile script.
    // No collider/rigidbody — pocket extras are plain-Instantiated and the tile self-detects the player by distance.
    static GameObject BuildTile<T>(string name, System.Action<T> cfg) where T : FloorTile
    {
        var go = new GameObject(name);

        var sr = go.AddComponent<SpriteRenderer>();
        var stock = Resources.Load<Material>("Sprite-Unlit-Default");
        if (stock != null) sr.sharedMaterial = stock;
        sr.sortingLayerName = "Buildings";
        sr.sortingOrder = 0;

        var t = go.AddComponent<T>();
        t.wallMounted = false;
        cfg?.Invoke(t);

        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }
}
