using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Base-scene housekeeping.
///   Tools/Base Kit/Remove Centre Constructor — deletes every Constructor instance in the World
///   scene (the pre-placed "InitRoom" at the map centre; user call 2026-09-14: "outdated
///   technology" — buildings are ore-built, and the build grid no longer gates on its radius,
///   see GridManager.RebuildRangeCache) and scrubs the dead entries it leaves in BM's building
///   list, then saves the scene.
/// Self-run: an eval sets the SessionState flag and refreshes; the fresh domain runs the pass
/// once (after play mode ends if the editor is playing). Headless: -executeMethod
/// BaseKitBuilder.RemoveCentreConstructor
/// </summary>
public static class BaseKitBuilder
{
    const string PendingKey = "BaseKit.removeConstructor";
    const string PendingThroneKey = "BaseKit.configureThrone";
    const string ScenePath = "Assets/Scenes/World.unity";
    const string Tag = "BaseKit";

    [InitializeOnLoadMethod]
    static void RunPendingAfterCompile()
    {
        if (!SessionState.GetBool(PendingKey, false) && !SessionState.GetBool(PendingThroneKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged += RunWhenEditMode;
            return;
        }
        EditorApplication.delayCall += RunPending;
    }

    static void RunWhenEditMode(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RunWhenEditMode;
        EditorApplication.delayCall += RunPending;
    }

    static void RunPending()
    {
        if (SessionState.GetBool(PendingKey, false)) { SessionState.SetBool(PendingKey, false); RemoveCentreConstructor(); }
        if (SessionState.GetBool(PendingThroneKey, false)) { SessionState.SetBool(PendingThroneKey, false); ConfigureThrone(); }
    }

    /// <summary>The Throne's footprint becomes the 2×2 world block (8×8 cells; the corners are
    /// freed at runtime by Throne.OccupiesFootprintCell). Scene field only — the body stays the
    /// authored PhysicCircle.</summary>
    [MenuItem("Tools/Base Kit/Configure Throne (2×2 block, centre store starts empty)")]
    public static void ConfigureThrone()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        var active = EditorSceneManager.GetActiveScene();
        bool wasOpen = active.path == ScenePath;
        var scene = wasOpen ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool dirty = false;
        Throne throne = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Throne>(true))
            {
                throne = t;
                var so = new SerializedObject(t);
                var size = so.FindProperty("size");
                if (size == null) continue;
                if (size.vector2Value == new Vector2(2f, 2f)) continue;
                size.vector2Value = new Vector2(2f, 2f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(t);
                dirty = true;
                Debug.Log($"[{Tag}] Throne '{t.name}': size → 2×2.");
            }
        // the store on the Throne starts EMPTY like any store (user call 2026-09-14 — the brief
        // 8/8 opening charge is retired): startEnergy 0 on the instance
        if (throne != null)
        {
            EmberStoreBuilding centre = null;
            float best = 0.6f * 0.6f;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var s in root.GetComponentsInChildren<EmberStoreBuilding>(true))
                {
                    float d = (s.transform.position - throne.transform.position).sqrMagnitude;
                    if (d <= best) { best = d; centre = s; }
                }
            if (centre != null)
            {
                var so = new SerializedObject(centre);
                var se = so.FindProperty("startEnergy");
                if (se != null && se.floatValue != 0f)
                {
                    se.floatValue = 0f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(centre);
                    dirty = true;
                    Debug.Log($"[{Tag}] Centre store '{centre.name}': startEnergy → 0 (starts empty).");
                }
            }
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{Tag}] World scene saved.");
        }
        else Debug.Log($"[{Tag}] Throne already 2×2 — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }

    [MenuItem("Tools/Base Kit/Remove Centre Constructor")]
    public static void RemoveCentreConstructor()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        var active = EditorSceneManager.GetActiveScene();
        bool wasOpen = active.path == ScenePath;
        var scene = wasOpen ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool dirty = false;
        var doomed = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Constructor>(true))
            {
                var go = PrefabUtility.IsPartOfAnyPrefab(c.gameObject)
                    ? PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject) : c.gameObject;
                if (go != null && !doomed.Contains(go)) doomed.Add(go);
            }
        foreach (var go in doomed)
        {
            Debug.Log($"[{Tag}] Removed Constructor '{go.name}' at {go.transform.position}.");
            Object.DestroyImmediate(go);
            dirty = true;
        }
        // the dead references it leaves behind
        foreach (var root in scene.GetRootGameObjects())
            foreach (var bm in root.GetComponentsInChildren<BM>(true))
            {
                if (bm.buildings == null) continue;
                int n = bm.buildings.RemoveAll(b => b == null);
                if (n > 0) { EditorUtility.SetDirty(bm); dirty = true; Debug.Log($"[{Tag}] BM.buildings: {n} dead entr{(n == 1 ? "y" : "ies")} scrubbed."); }
            }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{Tag}] World scene saved.");
        }
        else Debug.Log($"[{Tag}] No Constructor in the scene — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }
}
