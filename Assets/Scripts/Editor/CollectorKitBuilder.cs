using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the hand-made Collector prefab (a bare sprite on the era ore material) into the ore
/// magnet building and wires it into the World scene.
///   Tools/Collector Kit/1 — Upgrade Prefab   (idempotent, edits the prefab IN PLACE: moves it from
///                                           Assets/Collector.prefab beside the other resource
///                                           buildings — guid kept, references survive — then adds
///                                           whatever is missing: the Collector script with the
///                                           36-frame cycle off CollectorBase.png, a ground child,
///                                           a nested Physic body sized to the 15 px sprite, and
///                                           the RadiusRing child the hover reach fades in on.
///                                           Re-run after retuning `radius` to redraw the ring.)
///   Tools/Collector Kit/2 — Wire Into World Scene   (palette slot after the Cell + defaultBuildings)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod CollectorKitBuilder.BuildAll
/// </summary>
public static class CollectorKitBuilder
{
    const string SourcePrefabPath = "Assets/Collector.prefab";
    const string PrefabPath = "Assets/Prefabs/Buildings/Resource Generation/Collector.prefab";
    const string StripPath = "Assets/CollectorBase.png";
    const string PhysicPath = "Assets/Resources/Physic.prefab";
    const string DonorPath = "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab";
    const string GroundSpritePath = "Assets/Sprites/BuildBox.png";
    const string GroundMatGuid = "d1b3bb1945d234b25addbf9670302d48";
    const string AfterPrefabPath = "Assets/Prefabs/Buildings/Resource Generation/Cell.prefab";
    const string ScenePath = "Assets/Scenes/World.unity";

    public static void BuildAll()
    {
        UpgradePrefab();
        WireScene();
    }

    [MenuItem("Tools/Collector Kit/1 — Upgrade Prefab")]
    public static void UpgradePrefab()
    {
        // home it with the other resource buildings first (MoveAsset keeps the guid)
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null
            && AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) != null)
        {
            string err = AssetDatabase.MoveAsset(SourcePrefabPath, PrefabPath);
            if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[CollectorKit] Move failed: {err}"); return; }
            Debug.Log($"[CollectorKit] Moved {SourcePrefabPath} → {PrefabPath}");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError($"[CollectorKit] No Collector prefab at {PrefabPath} (or {SourcePrefabPath}).");
            return;
        }
        var frames = LoadFrames(StripPath);
        if (frames.Length == 0) { Debug.LogError($"[CollectorKit] No sprites sliced in {StripPath}."); return; }
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"[CollectorKit] No physic prefab at {PhysicPath}."); return; }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var log = new List<string>();
            root.name = "Collector";
            if (donor != null) root.layer = donor.layer;

            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) { sr = root.AddComponent<SpriteRenderer>(); log.Add("SpriteRenderer added"); }
            sr.sprite = frames[0];
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }
            // the material stays the ore glow the prefab was authored with ("Glow Bright");
            // Collector cuts a copy of GS.Glow(Bright) at runtime and drives its `thecolor`

            // ground child (inactive until placed — Building.groundEdit)
            var groundT = root.transform.Find("ground");
            GameObject ground;
            if (groundT == null)
            {
                ground = new GameObject("ground");
                ground.transform.SetParent(root.transform, false);
                ground.layer = root.layer;
                var gsr = ground.AddComponent<SpriteRenderer>();
                gsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GroundSpritePath);
                var gmat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GroundMatGuid));
                if (gmat != null) gsr.sharedMaterial = gmat;
                gsr.sortingOrder = -100;
                ground.SetActive(false);
                log.Add("ground child added");
            }
            else ground = groundT.gameObject;

            // nested Physic body, boxed to the 15 px sprite (Building.Start memorises these dims)
            LifeScript life = root.GetComponentInChildren<LifeScript>(true);
            if (life == null)
            {
                var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab, root.transform);
                physic.transform.localPosition = Vector3.zero;
                var box = physic.GetComponent<BoxCollider2D>();
                if (box != null) { box.size = new Vector2(0.45f, 0.45f); box.offset = Vector2.zero; }
                life = physic.GetComponent<LifeScript>();
                log.Add("Physic body added (0.45 box)");
            }

            // the hover reach ring: a faint local-space circle, inactive until hovered
            var ringT = root.transform.Find("RadiusRing");
            LineRenderer ring;
            if (ringT == null)
            {
                var ringGo = new GameObject("RadiusRing");
                ringGo.transform.SetParent(root.transform, false);
                ringGo.layer = root.layer;
                ring = ringGo.AddComponent<LineRenderer>();
                ring.widthMultiplier = 0.035f;
                ring.textureMode = LineTextureMode.Stretch;
                ring.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                ring.sortingLayerName = "Power Ups";
                ring.sortingOrder = 20;
                ring.startColor = ring.endColor = new Color(1f, 1f, 1f, 0f);
                ringGo.SetActive(false);
                log.Add("RadiusRing child added");
            }
            else ring = ringT.GetComponent<LineRenderer>();

            var col = root.GetComponent<Collector>();
            if (col == null)
            {
                col = root.AddComponent<Collector>();
                col.enabled = false;             // ghost-disabled: the intended unbuilt state
                col.size = Vector2.one;
                col.maxHealth = 8f;
                col.builtBlasts = 2;
                col.oreRequired = 3;
                col.canOpen = false;
                log.Add("Collector script added (ghost-disabled)");
            }
            col.sr = sr;
            col.icon = frames[0];
            col.frames = frames;
            col.groundEdit = ground;
            col.physic = life;
            col.radiusRing = ring;
            if (ring != null) Collector.DrawRing(ring, col.radius);   // authored at the current reach

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CollectorKit] {PrefabPath} upgraded: {frames.Length} frames; "
                      + (log.Count > 0 ? string.Join(", ", log) : "already complete — refs refreshed"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Every sprite sliced off the strip, in numeric-suffix order (_0, _1, … _35).</summary>
    static Sprite[] LoadFrames(string path)
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

    [MenuItem("Tools/Collector Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        UpgradePrefab();
        var collector = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (collector == null) { Debug.LogError("[CollectorKit] Collector prefab missing — prefab pass failed?"); return; }
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(AfterPrefabPath);

        var active = EditorSceneManager.GetActiveScene();
        bool wasOpen = active.path == ScenePath;
        var scene = wasOpen ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool dirty = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var daddy in root.GetComponentsInChildren<DaddyBuildingTile>(true))
            {
                if (daddy.buildings == null) continue;
                var list = new List<GameObject>(daddy.buildings);
                if (list.Contains(collector)) goto palette_done;
                int at = after != null ? list.IndexOf(after) : -1;
                if (at < 0) continue;
                list.Insert(at + 1, collector);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                Debug.Log($"[CollectorKit] Collector slotted after the Cell in '{daddy.name}'.");
                goto palette_done;
            }
        }
        palette_done:
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var bpm in root.GetComponentsInChildren<BlueprintManager>(true))
            {
                if (bpm.defaultBuildings == null || bpm.defaultBuildings.Contains(collector)) continue;
                bpm.defaultBuildings.Add(collector);
                EditorUtility.SetDirty(bpm);
                dirty = true;
                Debug.Log("[CollectorKit] Collector added to defaultBuildings.");
            }
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CollectorKit] World scene saved.");
        }
        else Debug.Log("[CollectorKit] Already wired — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }
}
