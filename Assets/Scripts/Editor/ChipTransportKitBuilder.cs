using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The chip-transport batch of 2026-09-14 in ONE menu item:
///   Tools/Chip Transport Kit/Build All (Tube + Belt + Generators)
///     1. Tube Kit      — the renamed Chip Store (prefab/strips/script moved on disk; refs refreshed,
///                        intake fee zeroed: tubes transport for free).
///     2. Belt Kit      — the hand-made Belt prefab → conveyor tile, wired after the Tube.
///     3. Crush Generator Kit — the hand-made MiniCrushGenerator → Solo Generator (2 chips → 1),
///                        the Pulse Generator prefab → Crush Generator (25 chips + 3 energy → 15),
///                        both wired into the Energy row.
/// Run it from the menu, or from the shell in ONE call against the open editor:
///   unity command eval_file <file calling ChipTransportKitBuilder.BuildAll()>
/// (A Library marker + [InitializeOnLoadMethod] delayCall self-run was tried on 2026-09-14 and
/// never fired — the delayed call is dropped across the reload — so it's gone.)
/// Headless: -executeMethod ChipTransportKitBuilder.BuildAll
/// </summary>
public static class ChipTransportKitBuilder
{
    [MenuItem("Tools/Chip Transport Kit/Build All (Tube + Belt + Generators)")]
    public static void BuildAll()
    {
        if (Application.isPlaying) { Debug.LogWarning("[ChipTransportKit] Stop play mode first."); return; }
        TubeKitBuilder.WireScene();
        BeltKitBuilder.WireScene();
        CrushGeneratorKitBuilder.WireScene();
        Debug.Log("[ChipTransportKit] Build All done.");
    }
}

/// <summary>Shared bits of the building kits: strip loading, palette/default wiring, preview removal.</summary>
public static class ChipTransportKitUtil
{
    public const string ScenePath = "Assets/Scenes/World.unity";
    public const string PhysicPath = "Assets/Resources/Physic.prefab";
    public const string DonorPath = "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab";

    /// <summary>Every sprite sliced off a strip, in numeric-suffix order (_0, _1, …).</summary>
    public static Sprite[] LoadFrames(string path)
    {
        var list = new List<(int n, Sprite s)>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is not Sprite s) continue;
            int us = s.name.LastIndexOf('_');
            if (us >= 0 && int.TryParse(s.name.Substring(us + 1), out int n)) list.Add((n, s));
        }
        list.Sort((a, b) => a.n.CompareTo(b.n));
        var arr = new Sprite[list.Count];
        for (int k = 0; k < arr.Length; k++) arr[k] = list[k].s;
        return arr;
    }

    /// <summary>Move a hand-made prefab from the Assets root to its home (guid kept). True if the prefab exists at <paramref name="to"/> afterwards.</summary>
    public static bool Home(string from, string to, string tag)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(to) == null && AssetDatabase.LoadAssetAtPath<GameObject>(from) != null)
        {
            string err = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[{tag}] Move failed: {err}"); return false; }
            Debug.Log($"[{tag}] Moved {from} → {to}");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(to) != null) return true;
        Debug.LogError($"[{tag}] No prefab at {to} (or {from}).");
        return false;
    }

    public static Scene OpenWorld(out bool wasOpen)
    {
        var active = EditorSceneManager.GetActiveScene();
        wasOpen = active.path == ScenePath;
        return wasOpen ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
    }

    public static void CloseWorld(Scene scene, bool wasOpen, bool dirty, string tag)
    {
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{tag}] World scene saved.");
        }
        else Debug.Log($"[{tag}] Already wired — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }

    /// <summary>Slot a prefab into the build-menu row right after another (skipped if already anywhere in a palette).</summary>
    public static bool SlotAfter(Scene scene, GameObject prefab, GameObject after, string tag)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var daddy in root.GetComponentsInChildren<DaddyBuildingTile>(true))
            {
                if (daddy.buildings == null) continue;
                var list = new List<GameObject>(daddy.buildings);
                if (list.Contains(prefab)) return false;
            }
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var daddy in root.GetComponentsInChildren<DaddyBuildingTile>(true))
            {
                if (daddy.buildings == null) continue;
                var list = new List<GameObject>(daddy.buildings);
                int at = after != null ? list.IndexOf(after) : -1;
                if (at < 0) continue;
                list.Insert(at + 1, prefab);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                Debug.Log($"[{tag}] {prefab.name} slotted after {after.name} in '{daddy.name}'.");
                return true;
            }
        }
        Debug.LogWarning($"[{tag}] {prefab.name}: no palette row holds {(after != null ? after.name : "(null)")} — not slotted.");
        return false;
    }

    /// <summary>Put a prefab right after another in the build menu, MOVING it if it already sits
    /// elsewhere (a row is 4 tiles — a fifth is never shown, so a slot must sometimes change).</summary>
    public static bool ReslotAfter(Scene scene, GameObject prefab, GameObject after, string tag)
    {
        if (after == null) return SlotAfter(scene, prefab, null, tag);
        bool dirty = false;
        DaddyBuildingTile home = null;
        int homeAt = -1;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var daddy in root.GetComponentsInChildren<DaddyBuildingTile>(true))
            {
                if (daddy.buildings == null) continue;
                var list = new List<GameObject>(daddy.buildings);
                int at = list.IndexOf(after);
                int cur = list.IndexOf(prefab);
                if (at >= 0 && cur == at + 1) return false;   // already right after it
                if (cur < 0 && at < 0) continue;
                while (list.Remove(prefab)) { }
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                if (at >= 0) { home = daddy; homeAt = list.IndexOf(after); }
            }
        }
        if (home == null) { Debug.LogWarning($"[{tag}] {prefab.name}: no palette row holds {after.name} — not slotted."); return dirty; }
        var l2 = new List<GameObject>(home.buildings);
        l2.Insert(homeAt + 1, prefab);
        home.buildings = l2.ToArray();
        EditorUtility.SetDirty(home);
        Debug.Log($"[{tag}] {prefab.name} slotted after {after.name} in '{home.name}'.");
        return true;
    }

    public static bool AddDefault(Scene scene, GameObject prefab, string tag)
    {
        bool dirty = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var bpm in root.GetComponentsInChildren<BlueprintManager>(true))
            {
                if (bpm.defaultBuildings == null || bpm.defaultBuildings.Contains(prefab)) continue;
                bpm.defaultBuildings.Add(prefab);
                EditorUtility.SetDirty(bpm);
                dirty = true;
                Debug.Log($"[{tag}] {prefab.name} added to defaultBuildings.");
            }
        }
        return dirty;
    }

    /// <summary>The user's hand-placed preview: a root-level instance of the prefab (real
    /// buildings live under the Buildings parent) — the prefab took its place.</summary>
    public static bool RemovePreviews(Scene scene, GameObject prefab, string tag)
    {
        bool dirty = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null || root.transform.parent != null) continue;
            if (!PrefabUtility.IsPartOfAnyPrefab(root)) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (src == null || src != prefab) continue;
            Object.DestroyImmediate(root);
            dirty = true;
            Debug.Log($"[{tag}] Removed the root-level '{prefab.name}' preview instance from the scene.");
        }
        return dirty;
    }

    /// <summary>Size the energy-status icon on a prefab (a 0.5-unit sprite at scale 1 — a
    /// quarter-cell tile wants 0.5). The fields are protected on Building; stamped serialized.</summary>
    public static void StampIcon(Building b, Vector3 offset, float scale)
    {
        if (b == null) return;
        var so = new SerializedObject(b);
        var off = so.FindProperty("energyIconOffset");
        var sc = so.FindProperty("energyIconScale");
        if (off != null) off.vector3Value = offset;
        if (sc != null) sc.floatValue = scale;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A nested Physic body (one-cell hp/collider) boxed to <paramref name="box"/>.</summary>
    public static LifeScript EnsurePhysic(GameObject root, float box, List<string> log)
    {
        var life = root.GetComponentInChildren<LifeScript>(true);
        if (life != null) return life;
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"No physic prefab at {PhysicPath}."); return null; }
        var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab, root.transform);
        physic.transform.localPosition = Vector3.zero;
        var bc = physic.GetComponent<BoxCollider2D>();
        if (bc != null) { bc.size = new Vector2(box, box); bc.offset = Vector2.zero; }
        log.Add($"Physic body added ({box} box)");
        return physic.GetComponent<LifeScript>();
    }
}
