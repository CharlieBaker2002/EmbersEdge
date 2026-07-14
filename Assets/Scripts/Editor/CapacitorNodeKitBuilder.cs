using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the Capacitor Node prefab and wires it into the World scene.
///   Tools/Capacitor Kit/1 — Build Prefab   (safe any time; SKIPS if the prefab already exists so
///                                           hand-tuning survives; delete it to regenerate)
///   Tools/Capacitor Kit/2 — Wire Into World Scene   (World.unity must be open; idempotent)
///
/// CapacitorNode.prefab is a straight copy of EnergyHub.prefab (1-slot battery housing, same
/// build magnet + ghost-disabled state) with the main script swapped to CapacitorNode and the
/// directional `hub` flag cleared — a node feeds pylons by adjacency/tether, not a forward
/// column. If art exists at Assets/CapacitorNode.png it's applied to the body + icon; until it
/// lands the node wears the hub art (delete the prefab and rerun once the PNG arrives, or just
/// swap the sprite by hand).
/// </summary>
public static class CapacitorNodeKitBuilder
{
    const string HubPath = "Assets/Prefabs/EnergyHub.prefab";
    const string NodePath = "Assets/Prefabs/CapacitorNode.prefab";
    const string StationPath = "Assets/Prefabs/BatteryStation.prefab";
    const string ArtPath = "Assets/CapacitorNode.png";

    [MenuItem("Tools/Capacitor Kit/1 — Build Prefab")]
    public static void BuildPrefab()
    {
        BuildNodePrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[CapacitorKit] Prefab pass complete.");
    }

    /// <summary>Headless entry (-batchmode -executeMethod): opens World.unity, then wires.</summary>
    public static void WireSceneHeadless()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/World.unity");
        WireScene();
    }

    [MenuItem("Tools/Capacitor Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != "World")
        {
            Debug.LogError("[CapacitorKit] Open World.unity first.");
            return;
        }
        BuildPrefab();

        var node = AssetDatabase.LoadAssetAtPath<GameObject>(NodePath);
        if (node == null)
        {
            Debug.LogError("[CapacitorKit] Node prefab missing — prefab pass failed?");
            return;
        }

        // 1) always-buildable list
        var bpm = Object.FindAnyObjectByType<BlueprintManager>(FindObjectsInactive.Include);
        if (bpm != null)
        {
            if (!bpm.defaultBuildings.Contains(node)) bpm.defaultBuildings.Add(node);
            EditorUtility.SetDirty(bpm);
        }
        else Debug.LogWarning("[CapacitorKit] No BlueprintManager in scene.");

        // 2) build-menu group: the daddy that offers the Battery Station ("Drones & Batteries"),
        // falling back to the Energy Hub's group, then the first daddy.
        var bm = Object.FindAnyObjectByType<BM>(FindObjectsInactive.Include);
        if (bm != null)
        {
            var station = AssetDatabase.LoadAssetAtPath<GameObject>(StationPath);
            var hub = AssetDatabase.LoadAssetAtPath<GameObject>(HubPath);
            var so = new SerializedObject(bm);
            var daddiesProp = so.FindProperty("daddies");
            DaddyBuildingTile target = null, hubDaddy = null, first = null;
            for (int k = 0; k < daddiesProp.arraySize; k++)
            {
                var d = daddiesProp.GetArrayElementAtIndex(k).objectReferenceValue as DaddyBuildingTile;
                if (d == null || d.buildings == null) continue;
                if (first == null) first = d;
                if (station != null && d.buildings.Contains(station)) { target = d; break; }
                if (hubDaddy == null && hub != null && d.buildings.Contains(hub)) hubDaddy = d;
            }
            if (target == null) target = hubDaddy != null ? hubDaddy : first;
            if (target != null)
            {
                var list = (target.buildings ?? new GameObject[0]).ToList();
                if (!list.Contains(node)) list.Add(node);
                target.buildings = list.ToArray();
                EditorUtility.SetDirty(target);
                Debug.Log($"[CapacitorKit] Palette group '{target.name}' now offers the Capacitor Node.");
            }
            else Debug.LogWarning("[CapacitorKit] No DaddyBuildingTile found on BM.");
        }
        else Debug.LogWarning("[CapacitorKit] No BM in scene.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[CapacitorKit] World scene wired + saved.");
    }

    // ------------------------------------------------------------------ prefab builder

    static void BuildNodePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(NodePath) != null) { Skip(NodePath); return; }
        if (!AssetDatabase.CopyAsset(HubPath, NodePath))
        {
            Debug.LogError($"[CapacitorKit] Could not copy {HubPath} -> {NodePath}");
            return;
        }
        var contents = PrefabUtility.LoadPrefabContents(NodePath);
        contents.name = "Capacitor Node";
        var pad = contents.GetComponent<EnergyPad>();
        if (pad == null)
        {
            Debug.LogError("[CapacitorKit] Hub copy has no EnergyPad component.");
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.DeleteAsset(NodePath);
            return;
        }

        // Serialized-field edits BEFORE the script swap so the data rides across it:
        // the node is not directional (the hub copy serializes hub: 1), and the body/icon
        // take the dedicated art when it exists.
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        var padSO = new SerializedObject(pad);
        padSO.FindProperty("hub").boolValue = false;
        if (sprite != null)
        {
            var iconProp = padSO.FindProperty("icon");
            if (iconProp != null) iconProp.objectReferenceValue = sprite;
        }
        padSO.ApplyModifiedPropertiesWithoutUndo();
        if (sprite != null)
        {
            var body = pad.sr != null ? pad.sr : contents.GetComponentInChildren<SpriteRenderer>(true);
            if (body != null) body.sprite = sprite;
        }
        else Debug.Log($"[CapacitorKit] No art at {ArtPath} yet — keeping hub art (delete the prefab and rerun once it lands).");

        // Cost: the copied OrbMagnet Task magnet carries the hub's price (10 white). Tune on
        // the prefab's OrbMagnet if the node should cost differently.

        if (!SwapScript(pad, "Assets/Scripts/CapacitorNode.cs"))
        {
            Debug.LogError("[CapacitorKit] Script swap failed on the node copy.");
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.DeleteAsset(NodePath);
            return;
        }
        PrefabUtility.SaveAsPrefabAsset(contents, NodePath);
        PrefabUtility.UnloadPrefabContents(contents);
        Debug.Log($"[CapacitorKit] Built {NodePath}");
    }

    /// <summary>Swap a component's script in place, keeping every serialized field the subclass
    /// shares with the original (CapacitorNode : EnergyPad).</summary>
    static bool SwapScript(MonoBehaviour comp, string scriptPath)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null)
        {
            Debug.LogError($"[CapacitorKit] MonoScript not found at {scriptPath}");
            return false;
        }
        var so = new SerializedObject(comp);
        var prop = so.FindProperty("m_Script");
        prop.objectReferenceValue = script;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    static void Skip(string path) => Debug.Log($"[CapacitorKit] {path} exists — skipped (delete it to regenerate).");
}
