using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the battery-overhaul assets and wires them into the World scene.
///   Tools/Battery Kit/1 — Build Prefabs   (safe any time; SKIPS prefabs that already exist so
///                                          hand-tuning survives; delete a prefab to regenerate it)
///   Tools/Battery Kit/2 — Wire Into World Scene   (World.unity must be open; idempotent)
///
/// BatteryStation.prefab is a straight copy of EnergyPad.prefab (same art, same 1-cell placement,
/// same slot children + build magnet) with the main script swapped to BatteryStation — every
/// serialized field (batteryPrefab, slotTransforms, sr, ghost-disabled state) carries over.
/// PulseBattery.prefab is a copy of Battery.prefab with only the script swapped to PulseBattery
/// (same percent-sprite/coil display; the crate art lives on the in-wall glint alone). It lands
/// in Resources so MineField can spawn it from broken crate walls.
/// </summary>
public static class BatteryKitBuilder
{
    const string PadPath = "Assets/Prefabs/EnergyPad.prefab";
    const string StationPath = "Assets/Prefabs/BatteryStation.prefab";
    const string BatteryPath = "Assets/Prefabs/Battery.prefab";
    const string PulsePath = "Assets/Resources/PulseBattery.prefab";

    [MenuItem("Tools/Battery Kit/1 — Build Prefabs")]
    public static void BuildPrefabs()
    {
        BuildStationPrefab();
        BuildPulseBatteryPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[BatteryKit] Prefab pass complete.");
    }

    [MenuItem("Tools/Battery Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != "World")
        {
            Debug.LogError("[BatteryKit] Open World.unity first.");
            return;
        }
        BuildPrefabs();

        var station = AssetDatabase.LoadAssetAtPath<GameObject>(StationPath);
        if (station == null)
        {
            Debug.LogError("[BatteryKit] Station prefab missing — prefab pass failed?");
            return;
        }

        // 1) always-buildable list
        var bpm = Object.FindAnyObjectByType<BlueprintManager>(FindObjectsInactive.Include);
        if (bpm != null)
        {
            if (!bpm.defaultBuildings.Contains(station)) bpm.defaultBuildings.Add(station);
            EditorUtility.SetDirty(bpm);
        }
        else Debug.LogWarning("[BatteryKit] No BlueprintManager in scene.");

        // 2) build-menu group: the daddy that already offers the Energy Pad
        var bm = Object.FindAnyObjectByType<BM>(FindObjectsInactive.Include);
        if (bm != null)
        {
            var pad = AssetDatabase.LoadAssetAtPath<GameObject>(PadPath);
            var so = new SerializedObject(bm);
            var daddiesProp = so.FindProperty("daddies");
            DaddyBuildingTile target = null;
            for (int k = 0; k < daddiesProp.arraySize; k++)
            {
                var d = daddiesProp.GetArrayElementAtIndex(k).objectReferenceValue as DaddyBuildingTile;
                if (d == null) continue;
                if (target == null) target = d;   // fallback: first daddy
                if (d.buildings != null && d.buildings.Contains(pad)) { target = d; break; }
            }
            if (target != null)
            {
                var list = (target.buildings ?? new GameObject[0]).ToList();
                if (!list.Contains(station)) list.Add(station);
                target.buildings = list.ToArray();
                EditorUtility.SetDirty(target);
                Debug.Log($"[BatteryKit] Palette group '{target.name}' now offers the Battery Station.");
            }
            else Debug.LogWarning("[BatteryKit] No DaddyBuildingTile found on BM.");
        }
        else Debug.LogWarning("[BatteryKit] No BM in scene.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BatteryKit] World scene wired + saved.");
    }

    // ------------------------------------------------------------------ prefab builders

    static void BuildStationPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(StationPath) != null) { Skip(StationPath); return; }
        if (!AssetDatabase.CopyAsset(PadPath, StationPath))
        {
            Debug.LogError($"[BatteryKit] Could not copy {PadPath} -> {StationPath}");
            return;
        }
        var contents = PrefabUtility.LoadPrefabContents(StationPath);
        contents.name = "Battery Station";
        var pad = contents.GetComponent<EnergyPad>();
        if (pad == null || !SwapScript(pad, "Assets/Scripts/BatteryStation.cs"))
        {
            Debug.LogError("[BatteryKit] Script swap failed on the station copy.");
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.DeleteAsset(StationPath);
            return;
        }
        PrefabUtility.SaveAsPrefabAsset(contents, StationPath);
        PrefabUtility.UnloadPrefabContents(contents);
        Debug.Log($"[BatteryKit] Built {StationPath}");
    }

    static void BuildPulseBatteryPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PulsePath) != null) { Skip(PulsePath); return; }
        if (!AssetDatabase.CopyAsset(BatteryPath, PulsePath))
        {
            Debug.LogError($"[BatteryKit] Could not copy {BatteryPath} -> {PulsePath}");
            return;
        }
        var contents = PrefabUtility.LoadPrefabContents(PulsePath);
        contents.name = "PulseBattery";
        var bat = contents.GetComponent<Battery>();
        if (bat == null || !SwapScript(bat, "Assets/Scripts/PulseBattery.cs"))
        {
            Debug.LogError("[BatteryKit] Script swap failed on the pulse battery copy.");
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.DeleteAsset(PulsePath);
            return;
        }

        // Nothing else to strip or restyle: the pulse battery renders with the standard
        // percent-sprite/coil display now (crate art stays on the in-wall glint only), and its
        // doubled tank is code-set in PulseBattery.Start.
        PrefabUtility.SaveAsPrefabAsset(contents, PulsePath);
        PrefabUtility.UnloadPrefabContents(contents);
        Debug.Log($"[BatteryKit] Built {PulsePath}");
    }

    /// <summary>Swap a component's script in place, keeping every serialized field the subclass
    /// shares with the original (BatteryStation : EnergyPad, PulseBattery : Battery).</summary>
    static bool SwapScript(MonoBehaviour comp, string scriptPath)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null)
        {
            Debug.LogError($"[BatteryKit] MonoScript not found at {scriptPath}");
            return false;
        }
        var so = new SerializedObject(comp);
        var prop = so.FindProperty("m_Script");
        prop.objectReferenceValue = script;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    static void Skip(string path) => Debug.Log($"[BatteryKit] {path} exists — skipped (delete it to regenerate).");
}
