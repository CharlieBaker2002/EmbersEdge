using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the hand-made Belt prefab (a bare 8×8 sprite on Sprite-Lit-Default) into the conveyor
/// tile and wires it into the World scene.
///   Tools/Belt Kit/1 — Upgrade Prefab   (idempotent: homes Assets/Belt.prefab beside the Tube
///        — guid kept — and adds what's missing: the Belt script (ghost-disabled, 0.25 footprint,
///        NO body — invulnerable, blocks nothing — multi-drag, rotatable: R spins the direction)
///        with the 4 chevron frames off Belt.png. No physic child, no health bar.)
///   Tools/Belt Kit/2 — Wire Into World Scene   (palette slot after the Tube + defaultBuildings;
///        removes the root-level preview instance)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod BeltKitBuilder.BuildAll
/// </summary>
public static class BeltKitBuilder
{
    const string SourcePrefabPath = "Assets/Belt.prefab";
    public const string PrefabPath = "Assets/Prefabs/Buildings/Storage/Belt.prefab";
    const string StripPath = "Assets/Belt.png";
    const string AfterPrefabPath = "Assets/Prefabs/Buildings/Storage/Tube.prefab";
    const string Tag = "BeltKit";

    public static void BuildAll()
    {
        UpgradePrefab();
        WireScene();
    }

    [MenuItem("Tools/Belt Kit/1 — Upgrade Prefab")]
    public static void UpgradePrefab()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        if (!ChipTransportKitUtil.Home(SourcePrefabPath, PrefabPath, Tag)) return;
        var frames = ChipTransportKitUtil.LoadFrames(StripPath);
        if (frames.Length == 0) { Debug.LogError($"[{Tag}] No sprites sliced in {StripPath}."); return; }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(ChipTransportKitUtil.DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var log = new List<string>();
            root.name = "Belt";
            root.transform.position = Vector3.zero;
            if (donor != null) root.layer = donor.layer;

            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) { sr = root.AddComponent<SpriteRenderer>(); log.Add("SpriteRenderer added"); }
            sr.sprite = frames[0];
            sr.drawMode = SpriteDrawMode.Simple;
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }
            // material stays what the prefab was authored with (Sprite-Lit-Default — the regular sprite material)

            var belt = root.GetComponent<Belt>();
            if (belt == null)
            {
                belt = root.AddComponent<Belt>();
                belt.enabled = false;              // ghost-disabled: the intended unbuilt state
                belt.size = new Vector2(0.25f, 0.25f);
                belt.maxHealth = 1f;               // unused: no body
                belt.builtBlasts = 1;
                belt.oreRequired = 1;
                belt.canOpen = false;
                belt.rotatable = true;             // R spins the travel direction
                belt.multiDrag = true;             // sweep a run of tiles
                belt.noBody = true;                // invulnerable: nothing to hit, nothing to path around
                log.Add("Belt script added (ghost-disabled, bodiless)");
            }
            belt.sr = sr;
            belt.icon = frames[0];
            belt.frames = frames;
            belt.physic = null;
            ChipTransportKitUtil.StampIcon(belt, Vector3.zero, 0.5f);   // the line's no-energy sign, sized to a quarter cell

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Tag}] {PrefabPath} upgraded: {frames.Length} frames; "
                      + (log.Count > 0 ? string.Join(", ", log) : "already complete — refs refreshed"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/Belt Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        UpgradePrefab();
        var belt = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (belt == null) { Debug.LogError($"[{Tag}] Belt prefab missing — prefab pass failed?"); return; }
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(AfterPrefabPath);

        var scene = ChipTransportKitUtil.OpenWorld(out bool wasOpen);
        bool dirty = false;
        dirty |= ChipTransportKitUtil.SlotAfter(scene, belt, after, Tag);
        dirty |= ChipTransportKitUtil.AddDefault(scene, belt, Tag);
        dirty |= ChipTransportKitUtil.RemovePreviews(scene, belt, Tag);
        ChipTransportKitUtil.CloseWorld(scene, wasOpen, dirty, Tag);
    }
}
