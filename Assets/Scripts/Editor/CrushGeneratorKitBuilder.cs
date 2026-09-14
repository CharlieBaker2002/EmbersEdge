using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The two chip-fed generators (one script, CrushGenerator, two prefabs):
///   Tools/Crush Generator Kit/1 — Upgrade Solo Generator Prefab
///        The hand-made MiniCrushGenerator (renamed Solo Generator on disk, guid kept; homed with
///        the other generators) gets the CrushGenerator script — 2 chips → 1 energy, holds 1,
///        no crush energy — the 17-frame cycle off CrushGen.png, and a nested Physic body. Its
///        authored material (SpecialPurple + the CrushGenE emission) stays.
///   Tools/Crush Generator Kit/2 — Convert Pulse Generator → Crush Generator
///        The Pulse Generator prefab (Generator, typ Pulse) becomes the Crush Generator: the
///        Generator component is swapped for CrushGenerator — every Building-level field copied
///        across (size, hp, ore cost, physic, icon, behaviours…), the 34 pulse frames become the
///        crush cycle (shut on the middle frame) — 25 chips + 3 energy in one burst → 15,
///        holds 30, feeds neighbours at 2/s. The prefab is renamed Crush Generator (guid kept:
///        every palette / defaultBuildings reference survives).
///   Tools/Crush Generator Kit/3 — Wire Into World Scene   (Solo Generator after the Ember
///        Generator — the ember-generator row of the Energy palette; the row the Crush Generator
///        sits in is full at 4 — + defaultBuildings; removes the root-level preview)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod CrushGeneratorKitBuilder.BuildAll
/// </summary>
public static class CrushGeneratorKitBuilder
{
    const string Dir = "Assets/Prefabs/Buildings/Resource Generation/";
    const string SoloSourcePath = "Assets/MiniCrushGenerator.prefab";
    const string SoloOldHomePath = Dir + "MiniCrushGenerator.prefab";
    public const string SoloPrefabPath = Dir + "Solo Generator.prefab";
    const string SoloStripPath = "Assets/CrushGen.png";
    const string PulsePrefabPath = Dir + "Pulse Generator.prefab";
    public const string CrushPrefabPath = Dir + "Crush Generator.prefab";
    const string Tag = "CrushGenKit";

    public static void BuildAll()
    {
        UpgradeSolo();
        ConvertPulse();
        WireScene();
    }

    // ------------------------------------------------------------------ Solo Generator

    [MenuItem("Tools/Crush Generator Kit/1 — Upgrade Solo Generator Prefab")]
    public static void UpgradeSolo()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SoloPrefabPath) == null)
        {
            if (!ChipTransportKitUtil.Home(SoloOldHomePath, SoloPrefabPath, Tag)
                && !ChipTransportKitUtil.Home(SoloSourcePath, SoloPrefabPath, Tag)) return;
        }
        var frames = ChipTransportKitUtil.LoadFrames(SoloStripPath);
        if (frames.Length == 0) { Debug.LogError($"[{Tag}] No sprites sliced in {SoloStripPath}."); return; }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(ChipTransportKitUtil.DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;

        var root = PrefabUtility.LoadPrefabContents(SoloPrefabPath);
        try
        {
            var log = new List<string>();
            root.name = "Solo Generator";
            root.transform.position = Vector3.zero;
            if (donor != null) root.layer = donor.layer;

            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) { sr = root.AddComponent<SpriteRenderer>(); log.Add("SpriteRenderer added"); }
            sr.sprite = frames[0];
            sr.drawMode = SpriteDrawMode.Simple;
            if (donorSR != null)
            {
                sr.sortingLayerID = donorSR.sortingLayerID;
                sr.sortingOrder = donorSR.sortingOrder;
            }
            // material stays what the prefab was authored with (SpecialPurple + CrushGenE emission)

            var life = ChipTransportKitUtil.EnsurePhysic(root, 0.45f, log);

            var gen = root.GetComponent<CrushGenerator>();
            if (gen == null)
            {
                gen = root.AddComponent<CrushGenerator>();
                gen.enabled = false;               // ghost-disabled: the intended unbuilt state
                gen.size = new Vector2(0.5f, 0.5f);
                gen.maxHealth = 6f;
                gen.builtBlasts = 2;
                gen.oreRequired = 2;
                gen.canOpen = false;
                gen.rotatable = true;
                gen.chipsPerCrush = 2;
                gen.energyPerCrush = 1f;
                gen.maxEnergy = 1f;
                gen.drawRate = 1f;
                gen.crushEnergy = 0f;
                gen.crushFrame = 8;
                gen.crushFps = 24f;
                gen.resetFps = 4f;
                gen.intakeRadius = 0.55f;
                log.Add("CrushGenerator script added (Solo: 2 chips → 1 energy)");
            }
            gen.sr = sr;
            gen.icon = frames[0];
            gen.frames = frames;
            gen.physic = life;

            PrefabUtility.SaveAsPrefabAsset(root, SoloPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Tag}] {SoloPrefabPath} upgraded: {frames.Length} frames; "
                      + (log.Count > 0 ? string.Join(", ", log) : "already complete — refs refreshed"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ Crush Generator (ex Pulse)

    static string CrushPath()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CrushPrefabPath) != null) return CrushPrefabPath;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PulsePrefabPath) != null) return PulsePrefabPath;
        return null;
    }

    [MenuItem("Tools/Crush Generator Kit/2 — Convert Pulse Generator → Crush Generator")]
    public static void ConvertPulse()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        string path = CrushPath();
        if (path == null) { Debug.LogError($"[{Tag}] Neither {CrushPrefabPath} nor {PulsePrefabPath} exists."); return; }

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var log = new List<string>();
            root.name = "Crush Generator";
            var old = root.GetComponent<Generator>();
            var gen = root.GetComponent<CrushGenerator>();
            if (gen == null)
            {
                gen = root.AddComponent<CrushGenerator>();
                if (old != null)
                {
                    // carry every Building-level field across (size, hp, ore cost, physic, icon, behaviours…)
                    var src = new SerializedObject(old);
                    var dst = new SerializedObject(gen);
                    var it = src.GetIterator();
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = false;
                        if (it.name == "m_Script") continue;
                        var d = dst.FindProperty(it.name);
                        if (d != null && d.propertyType == it.propertyType) dst.CopyFromSerializedProperty(it);
                    }
                    dst.ApplyModifiedPropertiesWithoutUndo();
                    // the pulse frames become the crush cycle: shut on the middle frame
                    var sp = src.FindProperty("sprs");
                    if (sp != null && sp.isArray && sp.arraySize > 0)
                    {
                        var frames = new Sprite[sp.arraySize];
                        for (int i = 0; i < frames.Length; i++) frames[i] = sp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                        gen.frames = frames;
                        gen.crushFrame = frames.Length / 2;
                    }
                    gen.buildingBehaviours.Remove(old);
                    Object.DestroyImmediate(old, true);
                    log.Add("Generator (Pulse) swapped for CrushGenerator");
                }
                else log.Add("CrushGenerator script added");
                gen.enabled = false;               // ghost-disabled
                gen.chipsPerCrush = 25;
                gen.energyPerCrush = 15f;
                gen.crushEnergy = 3f;   // one burst
                gen.maxEnergy = 30f;
                gen.drawRate = 2f;
                gen.crushFps = 24f;
                gen.resetFps = 6f;
                gen.intakeRadius = 0.9f;
            }
            else if (old != null)
            {
                gen.buildingBehaviours.Remove(old);
                Object.DestroyImmediate(old, true);
                log.Add("stale Generator component removed");
            }
            if (gen.sr == null) gen.sr = root.GetComponent<SpriteRenderer>();
            if (gen.icon == null && gen.frames != null && gen.frames.Length > 0) gen.icon = gen.frames[0];
            if (gen.physic == null) gen.physic = root.GetComponentInChildren<LifeScript>(true);
            if (gen.sr != null && gen.frames != null && gen.frames.Length > 0) gen.sr.sprite = gen.frames[0];

            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Tag}] {path}: " + (log.Count > 0 ? string.Join(", ", log) : "already converted — refs refreshed"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        if (path == PulsePrefabPath)
        {
            string err = AssetDatabase.MoveAsset(PulsePrefabPath, CrushPrefabPath);
            if (!string.IsNullOrEmpty(err)) Debug.LogError($"[{Tag}] Rename failed: {err}");
            else Debug.Log($"[{Tag}] Renamed {PulsePrefabPath} → {CrushPrefabPath} (guid kept).");
        }
    }

    // ------------------------------------------------------------------ scene

    [MenuItem("Tools/Crush Generator Kit/3 — Wire Into World Scene")]
    public static void WireScene()
    {
        if (Application.isPlaying) { Debug.LogWarning($"[{Tag}] Stop play mode first."); return; }
        UpgradeSolo();
        ConvertPulse();
        var solo = AssetDatabase.LoadAssetAtPath<GameObject>(SoloPrefabPath);
        if (solo == null) { Debug.LogError($"[{Tag}] Solo Generator prefab missing — prefab pass failed?"); return; }
        // The Energy palette's last row (White, Soul, Blue, Crush) is full — a row holds 4 tiles
        // and a fifth is never drawn — so the Solo joins the ember-generator row above it.
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Ember Generator.prefab");

        var scene = ChipTransportKitUtil.OpenWorld(out bool wasOpen);
        bool dirty = false;
        dirty |= ChipTransportKitUtil.ReslotAfter(scene, solo, after, Tag);
        dirty |= ChipTransportKitUtil.AddDefault(scene, solo, Tag);
        dirty |= ChipTransportKitUtil.RemovePreviews(scene, solo, Tag);
        ChipTransportKitUtil.CloseWorld(scene, wasOpen, dirty, Tag);
    }
}
