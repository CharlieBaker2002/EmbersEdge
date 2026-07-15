using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the Chip Factory building and wires it into the World scene.
///   Tools/Chip Factory Kit/1 — Build Prefab   (safe any time; SKIPS the prefab if it already
///                                              exists so hand-tuning survives; delete it to regenerate)
///   Tools/Chip Factory Kit/2 — Wire Into World Scene   (World.unity must be open; idempotent —
///                                              slots the tile after the Chip Charger in its daddy
///                                              by editing the LIVE scene arrays, NEVER via
///                                              BuildMenuClassifier.Reclassify)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod ChipFactoryKitBuilder.BuildAll
///
/// Built from scratch (Refiner-kit style): the ChipShop.png strip (3 frames = era bases, frame 0
/// on the renderer; _Emission secondary already on the texture), ghost-disabled main script, a
/// Task OrbMagnet green build cost, an authored physic box on the 1×1 footprint (the 1×1.5 art
/// rides on a Body child, bottom-aligned), eat/out spots, Battery prefab + the three boost lists
/// (L1 Bouncy Bomb / L2 Shuriken Burst / L3 Nuke).
/// </summary>
public static class ChipFactoryKitBuilder
{
    const string PrefabPath = "Assets/Prefabs/Chip Factory.prefab";
    const string ArtPath = "Assets/Prefabs/ChipShop.png";
    const string DonorPath = "Assets/Prefabs/Chip Charger.prefab";
    const string PhysicPath = "Assets/Resources/Physic.prefab";
    const string BatteryPath = "Assets/Prefabs/Battery.prefab";
    const string ScenePath = "Assets/Scenes/World.unity";

    const string BoostL1 = "Assets/ScriptableObjects/Boosts/Bouncy Bomb.asset";
    const string BoostL2 = "Assets/ScriptableObjects/Boosts/Shuriken Burst.asset";
    const string BoostL3 = "Assets/ScriptableObjects/Boosts/Nuke.asset";

    const int GreenCost = 10;

    public static void BuildAll()
    {
        BuildPrefab();
        WireScene();
    }

    [MenuItem("Tools/Chip Factory Kit/1 — Build Prefab")]
    public static void BuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log($"[ChipFactoryKit] {PrefabPath} exists — skipped (delete it to regenerate).");
            return;
        }

        var frames = AssetDatabase.LoadAllAssetsAtPath(ArtPath).OfType<Sprite>()
            .OrderBy(s => s.name).ToArray();
        if (frames.Length == 0) { Debug.LogError($"[ChipFactoryKit] No sprites at {ArtPath}."); return; }
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"[ChipFactoryKit] No physic prefab at {PhysicPath}."); return; }
        var batteryGO = AssetDatabase.LoadAssetAtPath<GameObject>(BatteryPath);
        var battery = batteryGO != null ? batteryGO.GetComponent<Battery>() : null;
        if (battery == null) Debug.LogWarning($"[ChipFactoryKit] No Battery at {BatteryPath} — battery orders will no-op.");

        // sorting layer / GO layer conventions come off the Chip Charger
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        var donorSR = donor != null ? donor.GetComponentInChildren<SpriteRenderer>(true) : null;

        var root = new GameObject("Chip Factory");
        try
        {
            if (donor != null) root.layer = donor.layer;

            // 1×1.5 art on a bottom-aligned child so the 1×1 footprint sits under the door
            var body = new GameObject("Body") { layer = root.layer };
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            var sr = body.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }

            var eatSpot = new GameObject("EatSpot").transform;
            eatSpot.SetParent(root.transform, false);
            eatSpot.localPosition = new Vector3(0f, -0.3f, 0f);
            var outSpot = new GameObject("OutSpot").transform;
            outSpot.SetParent(root.transform, false);
            outSpot.localPosition = new Vector3(0f, -0.7f, 0f);

            var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab);
            physic.transform.SetParent(root.transform, false);
            physic.transform.localPosition = Vector3.zero;
            var box = physic.GetComponent<BoxCollider2D>();
            if (box != null) { box.size = new Vector2(0.9f, 0.9f); box.offset = Vector2.zero; }

            var magnet = root.AddComponent<OrbMagnet>();
            magnet.enabled = false;              // BM.Commit enables task magnets on placement
            magnet.typ = OrbMagnet.OrbType.Task;
            magnet.orbType = 1;                  // GREEN
            magnet.capacity = GreenCost;

            var factory = root.AddComponent<ChipFactory>();
            factory.enabled = false;             // ghost-disabled: the intended unbuilt state
            factory.sr = sr;
            factory.icon = frames[0];
            factory.size = new Vector2(1f, 1f);
            factory.maxHealth = 12f;
            factory.builtBlasts = 1;
            factory.physic = physic.GetComponent<LifeScript>();
            factory.batteryPrefab = battery;
            factory.level1Boosts = new[] { AssetDatabase.LoadAssetAtPath<MechanismSO>(BoostL1) };
            factory.level2Boosts = new[] { AssetDatabase.LoadAssetAtPath<MechanismSO>(BoostL2) };
            factory.level3Boosts = new[] { AssetDatabase.LoadAssetAtPath<MechanismSO>(BoostL3) };
            if (factory.level1Boosts[0] == null || factory.level2Boosts[0] == null || factory.level3Boosts[0] == null)
                Debug.LogWarning("[ChipFactoryKit] A boost MechanismSO failed to load — check the Boosts/ paths.");

            var so = new SerializedObject(factory);
            so.FindProperty("eatSpot").objectReferenceValue = eatSpot;
            so.FindProperty("outSpot").objectReferenceValue = outSpot;
            var era = so.FindProperty("eraSprites");
            era.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
                era.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ChipFactoryKit] Built {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/Chip Factory Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        BuildPrefab();
        var factory = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (factory == null)
        {
            Debug.LogError("[ChipFactoryKit] Chip Factory prefab missing — prefab pass failed?");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        bool dirty = false;

        // palette slot: straight after the Chip Charger in whichever daddy offers it —
        // live scene arrays only, never BuildMenuClassifier.Reclassify (it clobbers the
        // hand-arranged layout from its stale static table).
        var charger = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        if (charger != null)
        {
            foreach (var daddy in Object.FindObjectsByType<DaddyBuildingTile>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (daddy.buildings == null) continue;
                var list = new System.Collections.Generic.List<GameObject>(daddy.buildings);
                int at = list.IndexOf(charger);
                if (at < 0 || list.Contains(factory)) continue;
                list.Insert(at + 1, factory);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                Debug.Log($"[ChipFactoryKit] Chip Factory slotted after the Chip Charger in '{daddy.name}'.");
                break;
            }
        }
        else Debug.LogWarning("[ChipFactoryKit] Chip Charger prefab not found — palette slot skipped.");

        var bpm = Object.FindAnyObjectByType<BlueprintManager>(FindObjectsInactive.Include);
        if (bpm == null)
        {
            Debug.LogError("[ChipFactoryKit] No BlueprintManager in the World scene.");
            return;
        }
        if (!bpm.defaultBuildings.Contains(factory))
        {
            bpm.defaultBuildings.Add(factory);
            EditorUtility.SetDirty(bpm);
            dirty = true;
            Debug.Log("[ChipFactoryKit] Chip Factory added to defaultBuildings.");
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ChipFactoryKit] World scene saved.");
        }
        else Debug.Log("[ChipFactoryKit] Already wired — nothing to do.");
    }
}
