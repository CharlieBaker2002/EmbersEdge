using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the Refiner building and wires it into the World scene.
///   Tools/Refiner Kit/1 — Build Prefab   (safe any time; SKIPS the prefab if it already exists
///                                         so hand-tuning survives; delete it to regenerate)
///   Tools/Refiner Kit/2 — Wire Into World Scene   (opens World.unity itself; idempotent —
///                                         adds to BlueprintManager.defaultBuildings and re-runs
///                                         BuildMenuClassifier so the palette slot lands between
///                                         the Expander and the Ember Cannon)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod RefinerKitBuilder.BuildAll
///
/// The prefab is built from scratch (no donor): Refiner.png art, ghost-disabled main script,
/// a Task OrbMagnet for the green build cost, an EmberConnector (Generator — pure source, cabled
/// to the nearest store), an authored physic box matching the 3×2 sprite, and an eat spot.
/// </summary>
public static class RefinerKitBuilder
{
    const string PrefabPath = "Assets/Prefabs/Buildings/Core Infrastructure/Refiner.prefab";
    const string ArtPath = "Assets/Refiner.png";
    const string DonorPath = "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab";
    const string PhysicPath = "Assets/Resources/Physic.prefab";
    const string ScenePath = "Assets/Scenes/World.unity";

    const int GreenCost = 15;

    public static void BuildAll()
    {
        BuildPrefab();
        WireScene();
    }

    [MenuItem("Tools/Refiner Kit/1 — Build Prefab")]
    public static void BuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log($"[RefinerKit] {PrefabPath} exists — skipped (delete it to regenerate).");
            return;
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        if (sprite == null) { Debug.LogError($"[RefinerKit] No sprite at {ArtPath}."); return; }
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"[RefinerKit] No physic prefab at {PhysicPath}."); return; }

        // sorting layer / GO layer conventions come off an existing 1-cell ember building
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;

        var root = new GameObject("Refiner");
        try
        {
            if (donor != null) root.layer = donor.layer;

            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }

            var eatSpot = new GameObject("EatSpot").transform;
            eatSpot.SetParent(root.transform, false);
            eatSpot.localPosition = new Vector3(0f, 0.35f, 0f);

            // authored physic: nested Physic prefab with its box grown to the 3×2 footprint
            // (Building.Start memorises these dims and reapplies them to the runtime body)
            var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab);
            physic.transform.SetParent(root.transform, false);
            physic.transform.localPosition = Vector3.zero;
            var box = physic.GetComponent<BoxCollider2D>();
            if (box != null) { box.size = new Vector2(2.9f, 1.9f); box.offset = Vector2.zero; }

            var connect = root.AddComponent<EmberConnector>();
            connect.taip = EmberConnector.typ.Generator;
            connect.maxEmber = 8;

            var magnet = root.AddComponent<OrbMagnet>();
            magnet.enabled = false;              // BM.Commit enables task magnets on placement
            magnet.typ = OrbMagnet.OrbType.Task;
            magnet.orbType = 1;                  // GREEN
            magnet.capacity = GreenCost;

            var refiner = root.AddComponent<Refiner>();
            refiner.enabled = false;             // ghost-disabled: the intended unbuilt state
            refiner.sr = sr;
            refiner.icon = sprite;
            refiner.size = new Vector2(3f, 2f);  // matches the 96×64 @ 32ppu art
            refiner.maxHealth = 15f;
            refiner.builtBlasts = 2;
            refiner.physic = physic.GetComponent<LifeScript>();
            refiner.connect = connect;

            var so = new SerializedObject(refiner);
            so.FindProperty("eatSpot").objectReferenceValue = eatSpot;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RefinerKit] Built {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/Refiner Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        BuildPrefab();
        var refiner = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (refiner == null)
        {
            Debug.LogError("[RefinerKit] Refiner prefab missing — prefab pass failed?");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != ScenePath)
        {
            Debug.LogError("[RefinerKit] Open World.unity first.");
            return;
        }
        bool dirty = false;

        // palette slot: insert straight after the Expander in whichever daddy offers it —
        // NEVER via BuildMenuClassifier.Reclassify, which rewrites every category from its
        // static table and clobbers any hand-arranged tile order in the scene.
        var expander = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Buildings/Core Infrastructure/Expander.prefab");
        var bm = Object.FindAnyObjectByType<BM>(FindObjectsInactive.Include);
        if (bm != null && expander != null)
        {
            foreach (var daddy in Object.FindObjectsByType<DaddyBuildingTile>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (daddy.buildings == null) continue;
                var list = new System.Collections.Generic.List<GameObject>(daddy.buildings);
                int at = list.IndexOf(expander);
                if (at < 0 || list.Contains(refiner)) continue;
                list.Insert(at + 1, refiner);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                Debug.Log($"[RefinerKit] Refiner slotted after the Expander in '{daddy.name}'.");
                break;
            }
        }
        else Debug.LogWarning("[RefinerKit] No BM/Expander found — palette slot skipped.");

        var bpm = Object.FindAnyObjectByType<BlueprintManager>(FindObjectsInactive.Include);
        if (bpm == null)
        {
            Debug.LogError("[RefinerKit] No BlueprintManager in the World scene.");
            return;
        }
        if (!bpm.defaultBuildings.Contains(refiner))
        {
            bpm.defaultBuildings.Add(refiner);
            EditorUtility.SetDirty(bpm);
            dirty = true;
            Debug.Log("[RefinerKit] Refiner added to defaultBuildings.");
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[RefinerKit] World scene saved.");
        }
        else Debug.Log("[RefinerKit] Already wired — nothing to do.");
    }
}
