using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Registers the built "wave extras" (CinderGeode / VerdantWellspring / EmberSnare, the SentryExtra set,
/// and the heal_box / orb_box pickups) into ALL per-era slots (d1/d2/d3) of
/// <see cref="MineAuthoringSO.ObjectsPalette"/> so they show up as the "Other Objects" brush row in
/// Tools &gt; Mine Forge and can be painted into mine pockets.
///
/// Why this exists: the extra PREFABS are produced by Tools &gt; Build Era-1 Extras / Build Era-1 Mine Tiles,
/// but those builders wire their MarauderSOs into WaveAuthoring.dungeon1Extras (the Wave Forge), not the Mine
/// Forge's GameObject palette. This one-click command scans the prefab folder and fills the Mine palette, so
/// "other objects palette is empty" is fixed without re-pointing the builders.
///
/// It also (re)builds the heal_box and orb_box pickup OBJECTS here, in the Extras folder — they're one-shot
/// walk-over pickups, so they belong in the Object palette, NOT the status-Tiles brush (heal/orb were never
/// persistent flooring). Building them here keeps them out of Build Era-1 Mine Tiles (which now only owns the
/// 5 persistent status tiles) and guarantees they're present before the palette scan runs.
///
/// Idempotent + additive: it unions with whatever is already in the palette (deduped) and sorts by name.
/// Re-run after building more extras/tiles to pick them up.
/// </summary>
public static class MineObjectPaletteFiller
{
    const string MinePath = "Assets/Resources/MineAuthoring.asset";
    const string ExtrasDir = "Assets/Prefabs/Extras";
    // Scan roots — FindAssets recurses, so "Assets/Prefabs/Extras" also covers ".../Extras/Tiles".
    static readonly string[] ScanFolders = { "Assets/Prefabs/Extras" };

    [MenuItem("Tools/Fill Mine Object Palette")]
    public static void Fill()
    {
        var data = AssetDatabase.LoadAssetAtPath<MineAuthoringSO>(MinePath);
        if (data == null)
        {
            EditorUtility.DisplayDialog("Fill Mine Object Palette",
                "MineAuthoring asset not found at\n" + MinePath, "OK");
            return;
        }

        // (Re)build the one-shot pickup OBJECTS into the Extras folder so they're always present + in sync with
        // their scripts. Named heal_box / orb_box and placed alongside the other extras (NOT the Tiles subfolder)
        // because they're Objects, not status tiles. The folder scan below then picks them up like any extra.
        EnsureFolder("Assets/Prefabs", "Extras");
        var heal = BuildBox<HealTile>("heal_box", t => { t.radius = 0.80f; t.effectDuration = 0f; t.magnitude = 0.12f; t.rearm = 0f; });
        var orb  = BuildBox<OrbTile> ("orb_box",  t => { t.radius = 0.85f; t.effectDuration = 0f; t.magnitude = 3f;    t.rearm = 0f; });
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Gather every extra prefab (anything carrying a WaveExtra or SentryExtra — FloorTile derives from
        // WaveExtra, the sentries from SentryExtra). heal_box / orb_box are included via this same scan.
        var found = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", ScanFolders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            if (go.GetComponent<WaveExtra>() != null || go.GetComponent<SentryExtra>() != null)
                found.Add(go);
        }
        // Belt-and-braces: make sure the freshly-built boxes are in the set even if the scan hasn't refreshed yet.
        if (heal != null && !found.Contains(heal)) found.Add(heal);
        if (orb  != null && !found.Contains(orb))  found.Add(orb);

        // Populate ALL per-era object lists (d1 / d2 / d3). For each era, union its existing list with the
        // discovered extras (so any manual per-era additions are kept), dedupe, and sort by name. There's no
        // per-era source split yet, so every dungeon gets the same extras — trim per-era in Mine Forge after.
        int perEra = 0;
        for (int era = 0; era < MineAuthoringSO.ERAS; era++)
        {
            var union = new List<GameObject>();
            var existing = data.ObjectsPalette(era);
            if (existing != null)
                union.AddRange(existing.Where(g => g != null));
            foreach (var g in found)
                if (!union.Contains(g)) union.Add(g);
            union = union.Distinct().OrderBy(g => g.name).ToList();
            data.SetObjectsPalette(era, union.ToArray());
            perEra = union.Count;
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Fill Mine Object Palette",
            $"Filled the d1 / d2 / d3 Mine 'Other Objects' palettes with {found.Count} extra prefab(s) " +
            $"({perEra} per era after de-dupe):\n\n• " +
            string.Join("\n• ", found.OrderBy(g => g.name).Select(g => g.name)) +
            "\n\nOpen Tools > Mine Forge — each era tab's 'Other Objects' brush row now lists them " +
            "(heal_box / orb_box included as Objects).",
            "OK");
    }

    // A thin shell prefab for a walk-over pickup: SpriteRenderer (the tile bakes its glyph at runtime) + the
    // configured FloorTile script. No collider/rigidbody — pocket pickups are plain-Instantiated and self-detect
    // the player by distance. Mirrors Era1TilesBuilder.BuildTile but saves to the Extras folder as an Object.
    static GameObject BuildBox<T>(string name, System.Action<T> cfg) where T : FloorTile
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

        string path = $"{ExtrasDir}/{name}.prefab";
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
