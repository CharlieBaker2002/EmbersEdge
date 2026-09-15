using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives the buildings with a reach the Collector's hover ring (user call 2026-09-14): pylons
/// (cable radius), the Cell (loose-chip ring), the turrets (Finder radius) and the Expander
/// (span). Per prefab, idempotent: a "RadiusRing" child LineRenderer under the NON-ROTATING root
/// (the Collector's authored one is reused; local space, no caps/corners — the LineRenderer
/// AABB gotcha; "Power Ups" layer, order 20, Sprites-Default, inactive) drawn at the current
/// reach, plus the shared <see cref="HoverRing"/> component on that root wired to it and to the
/// Building. The Collector's own ring fade moved into HoverRing, so it is in the list too.
///   Tools/Hover Ring Kit/Add Rings To Prefabs
/// Self-run: an eval in the open editor sets the SessionState flag and refreshes; the fresh
/// domain runs the pass once (after play mode ends if the editor is playing).
/// Headless: -executeMethod HoverRingKitBuilder.AddRings
/// </summary>
public static class HoverRingKitBuilder
{
    const string PendingKey = "HoverRingKit.runAfterCompile";
    const string Tag = "HoverRingKit";

    static readonly string[] Prefabs =
    {
        "Assets/Prefabs/Buildings/Resource Generation/Collector.prefab",
        "Assets/Prefabs/Buildings/EnergyPylon.prefab",
        "Assets/Prefabs/Buildings/Resource Generation/Cell.prefab",
        "Assets/Prefabs/Buildings/Core Infrastructure/Expander.prefab",
        "Assets/Prefabs/Buildings/Defence/PelterTurret.prefab",
        "Assets/Prefabs/Buildings/Defence/Push Tower.prefab",
        "Assets/Prefabs/Buildings/Defence/Mine Sprayer.prefab",
        "Assets/Prefabs/Buildings/Defence/SwordTurret.prefab",
        "Assets/Prefabs/Buildings/Defence/Force Field.prefab",
    };

    [InitializeOnLoadMethod]
    static void RunPendingAfterCompile()
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged += RunWhenEditMode;   // prefabs can't be saved mid-play — wait
            return;
        }
        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += AddRings;
    }

    static void RunWhenEditMode(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RunWhenEditMode;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += AddRings;
    }

    [MenuItem("Tools/Hover Ring Kit/Add Rings To Prefabs")]
    public static void AddRings()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        int done = 0;
        foreach (var path in Prefabs) if (AddRing(path)) done++;
        AssetDatabase.SaveAssets();
        Debug.Log($"[{Tag}] Hover rings on {done}/{Prefabs.Length} prefabs.");
    }

    static bool AddRing(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning($"[{Tag}] No prefab at {path}."); return false; }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var b = root.GetComponentInChildren<Building>(true);
            if (b == null) { Debug.LogWarning($"[{Tag}] {path}: no Building."); return false; }
            Transform anchor = b.hasExtraParent && b.transform.parent != null ? b.transform.parent : b.transform;

            var ringT = anchor.Find("RadiusRing");
            LineRenderer ring;
            string added = "";
            if (ringT == null)
            {
                var go = new GameObject("RadiusRing");
                go.transform.SetParent(anchor, false);
                go.layer = anchor.gameObject.layer;
                ring = go.AddComponent<LineRenderer>();
                ring.widthMultiplier = 0.035f;
                ring.textureMode = LineTextureMode.Stretch;
                ring.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                ring.sortingLayerName = "Power Ups";
                ring.sortingOrder = 20;
                ring.startColor = ring.endColor = new Color(1f, 1f, 1f, 0f);
                go.SetActive(false);
                added += "RadiusRing child, ";
            }
            else ring = ringT.GetComponent<LineRenderer>();

            var hr = anchor.GetComponent<HoverRing>();
            if (hr == null) { hr = anchor.gameObject.AddComponent<HoverRing>(); added += "HoverRing component, "; }
            hr.building = b;
            hr.ring = ring;

            float r = b.HoverRingRadius;
            if (r <= 0f)
            {
                var f = anchor.GetComponentInChildren<Finder>(true);
                if (f != null) r = f.radius;
            }
            if (r > 0f && ring != null) Collector.DrawRing(ring, r);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[{Tag}] {System.IO.Path.GetFileNameWithoutExtension(path)}: reach {r:F2} — {(added.Length > 0 ? added.TrimEnd(' ', ',') : "refs refreshed")}");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
