using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Hoover weapon (the ore-gathering staff) as PREFAB + MECHANISM assets:
///   Tools/Hoover Kit/1 — Build Prefab & Mechanism   (imports the pixel art with its _Emission
///                                                   secondary, builds the prefab — sprite,
///                                                   Nozzle child, authored Syphon particle
///                                                   system — and the MechanismSO; SKIPS assets
///                                                   that already exist so hand-tuning survives)
///   Tools/Hoover Kit/2 — Wire Into Scenes           (World + Arena: BlueprintManager.allbps and
///                                                   every Baron's inits; idempotent; saves)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod HooverKitBuilder.BuildAll
/// </summary>
public static class HooverKitBuilder
{
    const string ArtPath = "Assets/Sprites/Weapons/Hoover.png";
    const string EmissionPath = "Assets/Sprites/Weapons/HooverE.png";
    const string PrefabPath = "Assets/Prefabs/Parts/Weapons/Hoover.prefab";
    const string SOPath = "Assets/ScriptableObjects/Weapons/Hoover.asset";
    const string DonorPrefab = "Assets/Prefabs/Parts/Weapons/Pistol.prefab";
    const string EraLitMat = "Assets/Lighting/Materials/LitPurple.mat";   // authored default; runtime swaps to GS.MatByEra(era, lit)
    static readonly string[] Scenes = { "Assets/Scenes/World.unity", "Assets/Scenes/Arena.unity" };

    public static void BuildAll()
    {
        BuildAssets();
    }

    [MenuItem("Tools/Hoover Kit/1 — Build Prefab & Mechanism")]
    public static void BuildAssets()
    {
        ImportArt();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        if (sprite == null) { Debug.LogError($"[HooverKit] No sprite at {ArtPath}."); return; }
        BuildPrefab(sprite);
        BuildMechanism(sprite);
    }

    // ------------------------------------------------------------------ art

    static void ImportArt()
    {
        var emis = AssetImporter.GetAtPath(EmissionPath) as TextureImporter;
        if (emis != null) { ApplyPixelArt(emis, null); emis.SaveAndReimport(); }
        else Debug.LogWarning($"[HooverKit] No emission map at {EmissionPath}.");

        var main = AssetImporter.GetAtPath(ArtPath) as TextureImporter;
        if (main == null) { Debug.LogError($"[HooverKit] No texture at {ArtPath}."); return; }
        var emisTex = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath);
        ApplyPixelArt(main, emisTex);
        main.SaveAndReimport();
    }

    /// <summary>Project pixel-art import: 64 ppu, point, uncompressed, single sprite pivoted
    /// bottom-centre (weapons point along +Y from the hand), optional _Emission secondary.</summary>
    static void ApplyPixelArt(TextureImporter ti, Texture2D emission)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 64;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        settings.spritePivot = new Vector2(0.5f, 0f);
        settings.spriteMeshType = SpriteMeshType.Tight;
        settings.spriteGenerateFallbackPhysicsShape = true;
        ti.SetTextureSettings(settings);
        ti.secondarySpriteTextures = emission != null
            ? new[] { new SecondarySpriteTexture { name = "_Emission", texture = emission } }
            : new SecondarySpriteTexture[0];
    }

    // ------------------------------------------------------------------ prefab

    static void BuildPrefab(Sprite sprite)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log($"[HooverKit] {PrefabPath} exists — skipped (delete it to regenerate).");
            return;
        }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefab);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(EraLitMat);
        if (mat == null && donorSR != null) mat = donorSR.sharedMaterial;

        var root = new GameObject("Hoover");
        try
        {
            if (donor != null) root.layer = donor.layer;
            root.transform.localScale = new Vector3(0.5f, 0.5f, 1f);   // parts are authored at half scale (Pistol)

            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }
            if (mat != null) sr.sharedMaterial = mat;

            float h = sprite.rect.height / sprite.pixelsPerUnit;   // sprite height in local units (pivot is the base)
            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(root.transform, false);
            nozzle.localPosition = new Vector3(0f, h, 0f);

            var syphon = BuildSyphon(root.transform, h);

            var hoover = root.AddComponent<Hoover>();
            hoover.enabled = false;   // ghost-disabled: WeaponScript.StartPart / SwapWeapons own `enabled`
            hoover.sr = sr;
            hoover.stubborn = true;
            hoover.taip = Part.PartType.Weapon;
            hoover.level = 1;
            hoover.ammoPerClip = hoover.capacity;
            hoover.maximumAmmo = 0;
            hoover.totalAmmo = 0;
            hoover.maxReload = 0f;
            hoover.attackReset = 0f;
            hoover.option = -1f;
            hoover.restockCost = new float[4];

            var so = new SerializedObject(hoover);
            so.FindProperty("nozzle").objectReferenceValue = nozzle;
            so.FindProperty("syphon").objectReferenceValue = syphon;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HooverKit] Built {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>The syphon field: a line of particles born across the far end of the cone that
    /// stream INWARD to the nozzle (radial velocity toward the system origin) and die as they
    /// arrive. Emission rate is 0 here — Hoover drives it while the trigger is held; the era
    /// glow material is assigned at runtime (it changes with the era).</summary>
    static ParticleSystem BuildSyphon(Transform root, float nozzleHeight)
    {
        var go = new GameObject("Syphon");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, nozzleHeight, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ApplySyphonSettings(ps);
        return ps;
    }

    /// <summary>The syphon's authored tuning (shared by the build and the retune menu). WORLD
    /// simulation: a streak keeps the world position it was born at and flows to wherever the
    /// nozzle is NOW, so turning the staff never swings the streaks already in flight (only
    /// the birth line turns with the aim). Deliberately sparse and small — a suggestion of a
    /// tractor field, not a beam.</summary>
    static void ApplySyphonSettings(ParticleSystem ps)
    {
        const float reach = 3.2f, speed = 6.4f;   // matches Hoover.range (a touch inside it)

        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(reach / speed * 0.92f, reach / speed * 1.02f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.045f);
        main.startColor = Color.white;
        main.maxParticles = 128;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.SingleSidedEdge;   // a line across the cone's far end
        sh.radius = 0.95f;
        sh.position = new Vector3(0f, reach, 0f);
        sh.rotation = Vector3.zero;

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.radial = new ParticleSystem.MinMaxCurve(-speed);      // toward the nozzle (the system's position)

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.45f, 1f, 1f));   // thickens as it nears the mouth

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 3.5f;
        r.velocityScale = 0f;
        r.sortingLayerName = "Power Ups";
        r.sortingOrder = 26;
    }

    /// <summary>Re-apply the syphon tuning + the Hoover's FX defaults to the EXISTING prefab
    /// (the build menu skips a prefab that already exists). Idempotent.</summary>
    [MenuItem("Tools/Hoover Kit/3 — Retune Syphon FX")]
    public static void RetuneSyphon()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError($"[HooverKit] No prefab at {PrefabPath}."); return; }
        try
        {
            var hoover = root.GetComponent<Hoover>();
            var ps = root.GetComponentInChildren<ParticleSystem>(true);
            if (ps == null) { Debug.LogError("[HooverKit] No Syphon particle system on the prefab."); return; }
            ApplySyphonSettings(ps);
            if (hoover != null)
            {
                hoover.syphonRate = 22f;
                hoover.tetherWidth = 0.018f;
                hoover.syphonGlow = 0.5f;
                hoover.tetherGlow = 0.6f;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[HooverKit] Syphon FX retuned (world space, sparse) and saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ mechanism

    static void BuildMechanism(Sprite sprite)
    {
        if (AssetDatabase.LoadAssetAtPath<MechanismSO>(SOPath) != null)
        {
            Debug.Log($"[HooverKit] {SOPath} exists — skipped.");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError("[HooverKit] Prefab missing — prefab pass failed?"); return; }
        var so = ScriptableObject.CreateInstance<MechanismSO>();
        so.name = "Hoover";
        so.s = sprite;
        so.g = prefab;
        so.p = prefab.GetComponent<Hoover>();
        so.cost = new int[4];
        so.shopCost = 60f;
        so.description = "Hold to syphon loose ore into the tank (20). Press again when full, or anywhere at base, to spray it back out — into a blueprint's intake or onto the ground for the drones.";
        so.unique = false;
        so.relevents = new List<Blueprint>();
        so.classifier = Blueprint.Classifier.Mechanism;
        so.powerRequired = 1;
        so.continual = false;
        so.level = 0;
        AssetDatabase.CreateAsset(so, SOPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HooverKit] Built {SOPath}");
    }

    // ------------------------------------------------------------------ scenes

    [MenuItem("Tools/Hoover Kit/2 — Wire Into Scenes")]
    public static void WireScenes()
    {
        var so = AssetDatabase.LoadAssetAtPath<MechanismSO>(SOPath);
        if (so == null) { Debug.LogError("[HooverKit] Build the mechanism first (menu 1)."); return; }
        // The scene the user has open is edited in place; the other is opened ADDITIVELY,
        // edited, saved and closed — no scene switch, no save-changes dialog.
        foreach (var path in Scenes)
        {
            var active = EditorSceneManager.GetActiveScene();
            bool wasOpen = active.path == path;
            var scene = wasOpen ? active : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool dirty = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var bpm in root.GetComponentsInChildren<BlueprintManager>(true))
                {
                    if (bpm.allbps == null || bpm.allbps.Contains(so)) continue;
                    bpm.allbps.Add(so);
                    EditorUtility.SetDirty(bpm);
                    dirty = true;
                }
                foreach (var baron in root.GetComponentsInChildren<Baron>(true))
                {
                    var list = new List<MechanismSO>(baron.inits ?? new MechanismSO[0]);
                    if (list.Contains(so)) continue;
                    list.Add(so);
                    baron.inits = list.ToArray();
                    EditorUtility.SetDirty(baron);
                    dirty = true;
                }
            }
            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[HooverKit] {path}: Hoover wired (allbps + barons) and saved.");
            }
            else Debug.Log($"[HooverKit] {path}: already wired.");
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
