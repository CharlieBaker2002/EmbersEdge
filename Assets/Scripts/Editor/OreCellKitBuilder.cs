using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Rebuilds the Cell (the base ore producer — the orb-era Cell prefab was deleted with the orb
/// purge) from its surviving art and wires it into the World scene.
///   Tools/Ore Cell Kit/1 — Build Prefab           (skip-if-exists; delete the prefab to regenerate)
///   Tools/Ore Cell Kit/2 — Wire Into World Scene  (World.unity open or opened additively; adds
///                                                 the Cell after the Chip Factory in the Utility
///                                                 palette + BlueprintManager.defaultBuildings)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod OreCellKitBuilder.BuildAll
///
/// Rebuilt from the deleted prefab's recipe: Cell_0 sprite (Sprites/Cell.png, 32 ppu) on the
/// regular lit sprite material, the House animator (CellBop idle / CellTrigger → Spawn event),
/// a soft mint point Light2D, the standard ground child and a nested Physic body. 1×1, 10 hp,
/// 4 ore to build.
/// </summary>
public static class OreCellKitBuilder
{
    const string PrefabPath = "Assets/Prefabs/Buildings/Resource Generation/Cell.prefab";
    const string ArtPath = "Assets/Sprites/Cell.png";
    const string ControllerPath = "Assets/Animation/House.controller";
    const string DonorPath = "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab";
    const string PhysicPath = "Assets/Resources/Physic.prefab";
    const string GroundSpritePath = "Assets/Sprites/BuildBox.png";
    const string GroundMatGuid = "d1b3bb1945d234b25addbf9670302d48";
    const string AfterPrefabPath = "Assets/Prefabs/Chip Factory.prefab";
    const string ScenePath = "Assets/Scenes/World.unity";

    public static void BuildAll()
    {
        BuildPrefab();
    }

    [MenuItem("Tools/Ore Cell Kit/1 — Build Prefab")]
    public static void BuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log($"[OreCellKit] {PrefabPath} exists — skipped (delete it to regenerate).");
            return;
        }
        Sprite art = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ArtPath))
            if (o is Sprite s && s.name == "Cell_0") art = s;
        if (art == null) { Debug.LogError($"[OreCellKit] No Cell_0 sprite in {ArtPath}."); return; }
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"[OreCellKit] No physic prefab at {PhysicPath}."); return; }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;
        var litMat = Resources.Load<Material>("Sprite-Lit-Default");

        var root = new GameObject("Cell");
        try
        {
            if (donor != null) root.layer = donor.layer;

            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = art;
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }
            if (litMat != null) sr.sharedMaterial = litMat;   // regular sprite material (user rule)

            var anim = root.AddComponent<Animator>();
            if (controller != null) anim.runtimeAnimatorController = controller;
            else Debug.LogWarning($"[OreCellKit] No animator controller at {ControllerPath} — the Cell will spawn without its bop.");

            // soft mint point light, as the old Cell had
            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.layer = root.layer;
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = new Color(0.7647059f, 1f, 0.9411765f, 1f);
            light.intensity = 0.1f;
            light.pointLightInnerRadius = 0.6f;
            light.pointLightOuterRadius = 0.9f;
            light.falloffIntensity = 0.6f;
            light.lightOrder = 13;
            var so = new SerializedObject(light);
            var layersProp = so.FindProperty("m_ApplyToSortingLayers");
            if (layersProp != null)
            {
                var layers = SortingLayer.layers;
                layersProp.arraySize = layers.Length;
                for (int i = 0; i < layers.Length; i++) layersProp.GetArrayElementAtIndex(i).intValue = layers[i].id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ground child (inactive until placed — Building.groundEdit)
            var ground = new GameObject("ground");
            ground.transform.SetParent(root.transform, false);
            ground.layer = root.layer;
            var gsr = ground.AddComponent<SpriteRenderer>();
            gsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GroundSpritePath);
            var gmat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GroundMatGuid));
            if (gmat != null) gsr.sharedMaterial = gmat;
            gsr.sortingOrder = -100;
            ground.SetActive(false);

            var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab);
            physic.transform.SetParent(root.transform, false);
            physic.transform.localPosition = Vector3.zero;

            var cell = root.AddComponent<OreCell>();
            cell.enabled = false;              // ghost-disabled: the intended unbuilt state
            cell.sr = sr;
            cell.icon = art;
            cell.groundEdit = ground;
            cell.size = Vector2.one;
            cell.maxHealth = 10f;
            cell.builtBlasts = 4;
            cell.oreRequired = 4;
            cell.canOpen = false;
            cell.physic = physic.GetComponent<LifeScript>();
            cell.buildingBehaviours.Add(anim);   // idle bop only once built

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OreCellKit] Built {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/Ore Cell Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        BuildPrefab();
        var cell = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (cell == null) { Debug.LogError("[OreCellKit] Cell prefab missing — prefab pass failed?"); return; }
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
                var list = new System.Collections.Generic.List<GameObject>(daddy.buildings);
                if (list.Contains(cell)) { dirty = false; goto palette_done; }
                int at = after != null ? list.IndexOf(after) : -1;
                if (at < 0) continue;
                list.Insert(at + 1, cell);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                Debug.Log($"[OreCellKit] Cell slotted after the Chip Factory in '{daddy.name}'.");
                goto palette_done;
            }
        }
        palette_done:
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var bpm in root.GetComponentsInChildren<BlueprintManager>(true))
            {
                if (bpm.defaultBuildings == null || bpm.defaultBuildings.Contains(cell)) continue;
                bpm.defaultBuildings.Add(cell);
                EditorUtility.SetDirty(bpm);
                dirty = true;
                Debug.Log("[OreCellKit] Cell added to defaultBuildings.");
            }
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[OreCellKit] World scene saved.");
        }
        else Debug.Log("[OreCellKit] Already wired — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }
}
