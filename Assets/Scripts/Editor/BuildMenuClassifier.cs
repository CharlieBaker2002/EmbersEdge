using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Rewires the World scene's build-menu categories (DaddyBuildingTile groups).
/// A null entry in a buildings list = new row in the palette grid (see BM.SetupDaddy).
/// Run from the menu, or headless:
///   Unity -batchmode -quit -projectPath . -executeMethod BuildMenuClassifier.Reclassify
/// </summary>
public static class BuildMenuClassifier
{
    const string ScenePath = "Assets/Scenes/World.unity";
    const string ChipCondensorPath = "Assets/Prefabs/Buildings/Core Infrastructure/Chip Condensor.prefab";

    // old daddy GameObject name -> (new name, building prefab paths; null = new row)
    // SYNCED 2026-07-13 to the hand-arranged World-scene layout (restored from git after a
    // Reclassify run clobbered it) — if you rearrange tiles in the scene, mirror it here
    // BEFORE ever running Reclassify again, because this table overwrites the scene wholesale.
    static readonly (string oldName, string newName, string[] paths)[] Categories =
    {
        ("Defence", "Defence", new[]
        {
            "Assets/Prefabs/Buildings/Defence/PelterTurret.prefab",
            "Assets/Prefabs/Buildings/Defence/Push Tower.prefab",
            "Assets/Prefabs/Buildings/Defence/Mine Sprayer.prefab",
            null,
            "Assets/Prefabs/Buildings/Core Infrastructure/Wall.prefab",
            "Assets/Prefabs/Buildings/Defence/Force Field.prefab",
        }),
        ("Utlity", "Drones & Batteries", new[]
        {
            "Assets/Prefabs/EnergyHub.prefab",
            "Assets/Prefabs/EnergyPad.prefab",
            null,
            "Assets/Prefabs/Buildings/Defence/DroneDock.prefab",
            "Assets/Prefabs/BatteryStation.prefab",
            "Assets/Resources/Telepad.prefab",
            null,
            "Assets/Prefabs/Buildings/Defence/Drill Forge.prefab",
            "Assets/Prefabs/Buildings/Defence/Cargoloft.prefab",
            null,
            "Assets/Prefabs/Buildings/Defence/Fighter Ship Factory.prefab",
            "Assets/Prefabs/Buildings/Defence/ClawBot Factory.prefab",
        }),
        ("Core Infrastructure", "Ember Infrastructure", new[]
        {
            ChipCondensorPath,
            "Assets/Prefabs/Buildings/Core Infrastructure/Expander.prefab",
            "Assets/Prefabs/Buildings/Core Infrastructure/Refiner.prefab",
            "Assets/Prefabs/Buildings/Core Infrastructure/EmberCannon.prefab",
            null,
            "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab",
            "Assets/Prefabs/Buildings/Storage/Ember Store.prefab",
            "Assets/Prefabs/Buildings/Storage/Large Ember Store.prefab",
        }),
        ("Storage", "Orb Infrastructure", new[]
        {
            "Assets/Prefabs/Buildings/Resource Generation/Cell.prefab",
            "Assets/Prefabs/Buildings/Resource Generation/White Harvester.prefab",
            null,
            "Assets/Prefabs/Buildings/Storage/Store White.prefab",
            "Assets/Prefabs/Buildings/Storage/Store Green.prefab",
            "Assets/Prefabs/Buildings/Storage/Store Blue.prefab",
            "Assets/Prefabs/Buildings/Storage/Store Red.prefab",
        }),
        ("Resource Generation", "Pylons & Generators", new[]
        {
            "Assets/Prefabs/EnergyPylon.prefab",
            "Assets/Prefabs/LongRangePylon.prefab",
            "Assets/Prefabs/MultiPylon.prefab",
            null,
            "Assets/Prefabs/Buildings/Resource Generation/Small Ember Generator.prefab",
            "Assets/Prefabs/Buildings/Resource Generation/Ember Generator.prefab",
            null,
            "Assets/Prefabs/Buildings/Resource Generation/White Generator.prefab",
            "Assets/Prefabs/Buildings/Resource Generation/Blue Generator.prefab",
            "Assets/Prefabs/Buildings/Resource Generation/Pulse Generator.prefab",
            null,
            "Assets/Prefabs/Buildings/Resource Generation/Small Soui Generator.prefab",
            "Assets/Prefabs/Buildings/Resource Generation/Soul Generator.prefab",
        }),
    };

    [MenuItem("Tools/Build Menu/Reclassify Categories")]
    public static void Reclassify()
    {
        EnsureChipCondensor();

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        BM bm = FindBM(scene);
        if (bm == null) { Debug.LogError("BuildMenuClassifier: no BM in " + ScenePath); return; }

        var so = new SerializedObject(bm);
        var daddiesProp = so.FindProperty("daddies");
        var byName = new Dictionary<string, DaddyBuildingTile>();
        for (int i = 0; i < daddiesProp.arraySize; i++)
        {
            var d = daddiesProp.GetArrayElementAtIndex(i).objectReferenceValue as DaddyBuildingTile;
            if (d != null) byName[d.gameObject.name] = d;
        }

        var ordered = new List<DaddyBuildingTile>();
        foreach (var (oldName, newName, paths) in Categories)
        {
            if (!byName.TryGetValue(oldName, out var daddy) && !byName.TryGetValue(newName, out daddy))
            {
                Debug.LogError($"BuildMenuClassifier: daddy '{oldName}' not found — skipped");
                continue;
            }
            daddy.gameObject.name = newName;
            var label = daddy.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = newName;

            var list = new GameObject[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == null) continue; // null slot = new row in the palette
                list[i] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (list[i] == null) Debug.LogError($"BuildMenuClassifier: missing prefab {paths[i]}");
            }
            daddy.buildings = list;
            EditorUtility.SetDirty(daddy);
            ordered.Add(daddy);
        }

        // Display order = the order categories are listed above.
        daddiesProp.arraySize = ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
            daddiesProp.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("BuildMenuClassifier: reclassified " + ordered.Count + " categories.");
    }

    static void EnsureChipCondensor()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ChipCondensorPath) != null) return;
        var go = new GameObject("Chip Condensor");
        PrefabUtility.SaveAsPrefabAsset(go, ChipCondensorPath);
        Object.DestroyImmediate(go);
        Debug.Log("BuildMenuClassifier: created placeholder " + ChipCondensorPath);
    }

    static BM FindBM(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var bm = root.GetComponentInChildren<BM>(true);
            if (bm != null) return bm;
        }
        return null;
    }
}
