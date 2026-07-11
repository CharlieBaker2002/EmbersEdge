using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Generates the drone-kit prefabs and wires them into the World scene.
///   Tools/Drone Kit/1 — Build Prefabs   (safe any time; SKIPS prefabs that already exist so
///                                        hand-tuning survives; delete a prefab to regenerate it)
///   Tools/Drone Kit/2 — Wire Into World Scene   (World.unity must be open; idempotent)
/// Art comes from Assets/Resources (Drone/DroneDrill/DroneBag/DroneDock/OreChips/Telepad/
/// Bagworkshop/"drill workshop"), so runtime code can also load it directly.
/// </summary>
public static class DroneKitBuilder
{
    const string DronePath = "Assets/Prefabs/Allies/Drone.prefab";
    const string DockPath = "Assets/Prefabs/Buildings/Defence/DroneDock.prefab";
    const string TelepadPath = "Assets/Prefabs/Buildings/Defence/Telepad.prefab";
    const string ForgePath = "Assets/Prefabs/Buildings/Defence/Drill Forge.prefab";
    const string CargoloftPath = "Assets/Prefabs/Buildings/Defence/Cargoloft.prefab";
    const string ChipPath = "Assets/Resources/OreChip.prefab";
    const string ClawBotPath = "Assets/Prefabs/Allies/ClawBot.prefab";
    const string A01Path = "Assets/Prefabs/Allies/A0_1.prefab";
    const string ClawFactoryPath = "Assets/Prefabs/Buildings/Defence/ClawBot Factory.prefab";
    const string ShipFactoryPath = "Assets/Prefabs/Buildings/Defence/Fighter Ship Factory.prefab";

    [MenuItem("Tools/Drone Kit/1 — Build Prefabs")]
    public static void BuildPrefabs()
    {
        BuildDronePrefab();
        BuildDockPrefab();
        BuildTelepadPrefab();
        BuildWorkshopPrefab(ForgePath, "Drill Forge", DroneEquipment.Drill, "drill workshop");
        BuildWorkshopPrefab(CargoloftPath, "Cargoloft", DroneEquipment.Bag, "Bagworkshop");
        BuildChipPrefab();
        WireVehicle(ClawBotPath, 2);
        WireVehicle(A01Path, 1);
        WireFactory(ClawFactoryPath);
        WireFactory(ShipFactoryPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[DroneKit] Prefab pass complete.");
    }

    [MenuItem("Tools/Drone Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != "World")
        {
            Debug.LogError("[DroneKit] Open World.unity first.");
            return;
        }
        BuildPrefabs();   // make sure everything exists

        var dock = AssetDatabase.LoadAssetAtPath<GameObject>(DockPath);
        var telepad = AssetDatabase.LoadAssetAtPath<GameObject>(TelepadPath);
        var forge = AssetDatabase.LoadAssetAtPath<GameObject>(ForgePath);
        var cargoloft = AssetDatabase.LoadAssetAtPath<GameObject>(CargoloftPath);
        var newBuildings = new[] { dock, telepad, forge, cargoloft };

        // 1) always-buildable list
        var bpm = Object.FindAnyObjectByType<BlueprintManager>(FindObjectsInactive.Include);
        if (bpm != null)
        {
            foreach (var g in newBuildings)
                if (g != null && !bpm.defaultBuildings.Contains(g))
                    bpm.defaultBuildings.Add(g);
            EditorUtility.SetDirty(bpm);
        }
        else Debug.LogWarning("[DroneKit] No BlueprintManager in scene.");

        // 2) build-menu group: append to the daddy that already offers the ClawBot Factory
        var bm = Object.FindAnyObjectByType<BM>(FindObjectsInactive.Include);
        if (bm != null)
        {
            var clawFactory = AssetDatabase.LoadAssetAtPath<GameObject>(ClawFactoryPath);
            var so = new SerializedObject(bm);
            var daddiesProp = so.FindProperty("daddies");
            DaddyBuildingTile target = null;
            for (int k = 0; k < daddiesProp.arraySize; k++)
            {
                var d = daddiesProp.GetArrayElementAtIndex(k).objectReferenceValue as DaddyBuildingTile;
                if (d == null) continue;
                if (target == null) target = d;   // fallback: first daddy
                if (d.buildings != null && d.buildings.Contains(clawFactory)) { target = d; break; }
            }
            if (target != null)
            {
                var list = (target.buildings ?? new GameObject[0]).ToList();
                foreach (var g in newBuildings)
                    if (g != null && !list.Contains(g)) list.Add(g);
                target.buildings = list.ToArray();
                EditorUtility.SetDirty(target);
                Debug.Log($"[DroneKit] Palette group '{target.name}' now offers the drone buildings.");
            }
            else Debug.LogWarning("[DroneKit] No DaddyBuildingTile found on BM.");
        }
        else Debug.LogWarning("[DroneKit] No BM in scene.");

        // 3) the manager singleton
        if (Object.FindAnyObjectByType<DroneManager>(FindObjectsInactive.Include) == null)
            new GameObject("DroneManager").AddComponent<DroneManager>();

        // 4) retire the art-mock placeholders (bare SpriteRenderer objects only — never scripts)
        foreach (var name in new[] { "Drone", "DroneWithDrill", "DroneWithBag", "DroneDock" })
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var t = FindDeep(root.transform, name);
                if (t != null && t.GetComponentsInChildren<MonoBehaviour>(true).Length == 0)
                {
                    Object.DestroyImmediate(t.gameObject);
                    Debug.Log($"[DroneKit] Removed placeholder '{name}'.");
                    break;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[DroneKit] World scene wired + saved.");
    }

    // ------------------------------------------------------------------ prefab builders

    static void BuildDronePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DronePath) != null) { Skip(DronePath); return; }
        var frames = DroneManager.LoadStripNumeric("Drone");
        var refUnit = AssetDatabase.LoadAssetAtPath<GameObject>(A01Path);
        var refSR = refUnit != null ? refUnit.GetComponentInChildren<SpriteRenderer>(true) : null;

        var root = new GameObject("Drone");
        root.layer = LayerMask.NameToLayer("Ally Units");
        root.tag = "Allies";
        var sr = root.AddComponent<SpriteRenderer>();
        if (frames.Length > 0) sr.sprite = frames[0];
        CopySorting(refSR, sr, 0);

        var rb = root.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;                   // facing is script-driven (transform.up)
        rb.linearDamping = 2f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var col = root.AddComponent<CircleCollider2D>();
        col.radius = 0.18f;

        var ls = root.AddComponent<LifeScript>();
        ls.maxHp = 5f;
        ls.hp = 5f;
        ls.orbs = new float[4];
        ls.dmgsrs = new List<SpriteRenderer> { sr };
        ls.thicknesses = new[] { 1 };

        var AS = root.AddComponent<ActionScript>();
        AS.mass = 1f;
        AS.maxVelocity = 2.5f;

        var drone = root.AddComponent<Drone>();
        drone.ls = ls;
        drone.AS = AS;
        drone.sr = sr;
        drone.frames = frames;
        ls.onDeaths = new List<MonoBehaviour> { drone };

        var conn = root.AddComponent<Connectable>();
        var cable = PylonCablePrefab();
        if (cable != null)
        {
            var cso = new SerializedObject(conn);
            cso.FindProperty("cablePrefab").objectReferenceValue = cable;
            cso.ApplyModifiedPropertiesWithoutUndo();
        }

        // drill hangs under the body
        var drill = new GameObject("DrillBit") { layer = root.layer };
        drill.transform.SetParent(root.transform, false);
        drill.transform.localPosition = new Vector3(0f, -0.234f, 0f);
        var dsr = drill.AddComponent<SpriteRenderer>();
        var drillFrames = DroneManager.LoadStripNumeric("DroneDrill");
        if (drillFrames.Length > 0) dsr.sprite = drillFrames[0];
        CopySorting(sr, dsr, -1);
        var bit = drill.AddComponent<DroneDrillBit>();
        bit.sr = dsr;
        drill.SetActive(false);

        // sack rides on the back
        var sackGo = new GameObject("Sack") { layer = root.layer };
        sackGo.transform.SetParent(root.transform, false);
        sackGo.transform.localPosition = new Vector3(0f, 0.281f, 0f);
        var ssr = sackGo.AddComponent<SpriteRenderer>();
        var bagFrames = DroneManager.LoadStripNumeric("DroneBag");
        if (bagFrames.Length > 0) ssr.sprite = bagFrames[0];
        CopySorting(sr, ssr, 1);
        sackGo.AddComponent<SackWobble>();
        sackGo.SetActive(false);

        Save(root, DronePath);
    }

    static void BuildDockPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(DockPath) != null) { Skip(DockPath); return; }
        var root = NewBuildingRoot("Drone Dock", FirstSprite("DroneDock"), out SpriteRenderer sr);
        var dock = root.AddComponent<DroneDock>();
        dock.sr = sr;
        dock.icon = sr.sprite;
        dock.maxHealth = 8f;
        dock.builtBlasts = 2;
        dock.dronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DronePath);
        AddTaskMagnet(root, 0, 10);
        AddTaskMagnet(root, 1, 6);
        Save(root, DockPath);
    }

    static void BuildTelepadPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TelepadPath) != null) { Skip(TelepadPath); return; }
        var root = NewBuildingRoot("Telepad", FirstSprite("Telepad"), out SpriteRenderer sr);
        var pad = root.AddComponent<Telepad>();
        pad.sr = sr;
        pad.icon = sr.sprite;
        pad.maxHealth = 6f;
        pad.builtBlasts = 0;          // orb-only build — REQUIRED for dungeon placement
        pad.dungeonBuildable = true;
        AddTaskMagnet(root, 0, 6);
        Save(root, TelepadPath);
    }

    static void BuildWorkshopPrefab(string path, string name, DroneEquipment produces, string spriteRes)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) { Skip(path); return; }
        var root = NewBuildingRoot(name, FirstSprite(spriteRes), out SpriteRenderer sr);
        var ws = root.AddComponent<EquipmentWorkshop>();
        ws.sr = sr;
        ws.icon = sr.sprite;
        ws.maxHealth = 8f;
        ws.builtBlasts = 2;
        ws.produces = produces;
        ws.itemCost = produces == DroneEquipment.Drill ? new[] { 5, 3, 0, 0 } : new[] { 5, 0, 3, 0 };
        AddTaskMagnet(root, 0, 8);
        AddTaskMagnet(root, produces == DroneEquipment.Drill ? 1 : 2, 4);
        Save(root, path);
    }

    static void BuildChipPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ChipPath) != null) { Skip(ChipPath); return; }
        var chips = DroneManager.LoadStripNumeric("OreChips");
        var root = new GameObject("OreChip");
        var sr = root.AddComponent<SpriteRenderer>();
        if (chips.Length > 0) sr.sprite = chips[0];
        CopySorting(OrbTemplateSR(), sr, 0);
        var chip = root.AddComponent<OreChip>();
        chip.sr = sr;
        Save(root, ChipPath);
    }

    static void WireVehicle(string path, int pilots)
    {
        var contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) { Debug.LogWarning($"[DroneKit] Missing vehicle prefab {path}"); return; }
        var pv = contents.GetComponent<PilotedVehicle>();
        if (pv == null)
        {
            pv = contents.AddComponent<PilotedVehicle>();
            pv.pilotsRequired = pilots;
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log($"[DroneKit] Added PilotedVehicle({pilots}) to {path}");
        }
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void WireFactory(string path)
    {
        var contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) { Debug.LogWarning($"[DroneKit] Missing factory prefab {path}"); return; }
        if (contents.GetComponentInChildren<FactoryPilotStation>(true) == null)
        {
            var ub = contents.GetComponentInChildren<UnitBuilding>(true);
            if (ub != null)
            {
                ub.gameObject.AddComponent<FactoryPilotStation>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"[DroneKit] Added FactoryPilotStation to {path}");
            }
            else Debug.LogWarning($"[DroneKit] No UnitBuilding on {path}?");
        }
        PrefabUtility.UnloadPrefabContents(contents);
    }

    // ------------------------------------------------------------------ helpers

    static GameObject NewBuildingRoot(string name, Sprite sprite, out SpriteRenderer sr)
    {
        var factory = AssetDatabase.LoadAssetAtPath<GameObject>(ClawFactoryPath);
        var root = new GameObject(name);
        if (factory != null) root.layer = factory.layer;
        sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        CopySorting(factory != null ? factory.GetComponentInChildren<SpriteRenderer>(true) : null, sr, 0);
        return root;
    }

    static void AddTaskMagnet(GameObject root, int orbType, int capacity)
    {
        var om = root.AddComponent<OrbMagnet>();
        om.typ = OrbMagnet.OrbType.Task;
        om.orbType = orbType;
        om.capacity = capacity;
        om.init = true;
        om.enabled = false;   // BM.Commit enables construction magnets
    }

    static Sprite FirstSprite(string res)
    {
        var strip = DroneManager.LoadStripNumeric(res);
        if (strip.Length > 0) return strip[0];
        var all = Resources.LoadAll<Sprite>(res);
        return all.Length > 0 ? all[0] : null;
    }

    static void CopySorting(SpriteRenderer from, SpriteRenderer to, int orderDelta)
    {
        if (to == null) return;
        if (from != null)
        {
            to.sortingLayerID = from.sortingLayerID;
            to.sortingOrder = from.sortingOrder + orderDelta;
            to.sharedMaterial = from.sharedMaterial;
        }
        else
        {
            to.sortingOrder = 10 + orderDelta;
        }
    }

    static LineRenderer PylonCablePrefab()
    {
        var pylon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnergyPylon.prefab");
        var pconn = pylon != null ? pylon.GetComponentInChildren<Connectable>(true) : null;
        if (pconn == null) return null;
        var so = new SerializedObject(pconn);
        return so.FindProperty("cablePrefab").objectReferenceValue as LineRenderer;
    }

    static SpriteRenderer OrbTemplateSR()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" }))
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (g != null && g.GetComponent<OrbScript>() != null)
            {
                var sr = g.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) return sr;
            }
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int k = 0; k < root.childCount; k++)
        {
            var hit = FindDeep(root.GetChild(k), name);
            if (hit != null) return hit;
        }
        return null;
    }

    static void Save(GameObject temp, string path)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        Debug.Log($"[DroneKit] Built {path}");
    }

    static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = System.IO.Path.GetDirectoryName(dir).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
    }

    static void Skip(string path) => Debug.Log($"[DroneKit] {path} exists — skipped (delete it to regenerate).");
}
