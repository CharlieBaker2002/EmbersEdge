using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Turns the hand-made Tube prefab (a bare sprite on the era ore material) into the
/// connecting chip-transport building and wires it into the World scene. (The Chip Store of
/// 2026-09-13, renamed Tube on 2026-09-14: script, cluster, gauge, kit, prefab and strips all
/// moved on disk with their guids — this kit just refreshes refs and zeroes the intake fee.)
///   Tools/Tube Kit/1 — Build Art + Upgrade Prefab
///       • ART: the authored strip has five connection tiles (closed, up, up+right,
///         up+right+down, all round); a tile set also needs the STRAIGHT (up+down). The kit
///         derives it from the "up" tile (its open-top row repeated; emission = the side-wall
///         lines running straight through with only the centre pips) and appends it as the
///         6th 8×8 slice of Tube.png / TubeE.png (40→48 px, existing slice IDs kept,
///         the _Emission secondary stays). Idempotent — skipped once the strip is 48 wide.
///       • PREFAB: moves Assets/Tube.prefab beside the ember stores as "Tube"
///         (guid kept) and adds what's missing: the Tube script (ghost-disabled, 0.25
///         footprint, 1 hp, multi-drag, not rotatable — the connections spin it), a nested
///         Physic body, the Contour child (LineRenderer the cluster comet runs on) and the
///         Shield child (era-tinted fill that ripples while the shield is up).
///   Tools/Tube Kit/2 — Wire Into World Scene   (palette slot after the Collector +
///         defaultBuildings; removes the bare sprite-only "Tube" preview object)
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod TubeKitBuilder.BuildAll
/// </summary>
public static class TubeKitBuilder
{
    const string SourcePrefabPath = "Assets/Tube.prefab";
    const string PrefabPath = "Assets/Prefabs/Buildings/Storage/Tube.prefab";
    const string StripPath = "Assets/Tube.png";
    const string EmissionPath = "Assets/TubeE.png";
    const string PhysicPath = "Assets/Resources/Physic.prefab";
    const string DonorPath = "Assets/Prefabs/Buildings/Storage/Small Ember Store.prefab";
    const string AfterPrefabPath = "Assets/Prefabs/Buildings/Resource Generation/Collector.prefab";
    const string ScenePath = "Assets/Scenes/World.unity";
    const int Tile = 8, AuthoredTiles = 5, Tiles = 6;

    public static void BuildAll()
    {
        UpgradePrefab();
        WireScene();
    }

    // (The one-shot self-run after a compile now lives in ChipTransportKitBuilder — a marker file.)

    [MenuItem("Tools/Tube Kit/1 — Build Art + Upgrade Prefab")]
    public static void UpgradePrefab()
    {
        if (Application.isPlaying) { Debug.LogWarning("[TubeKit] Stop play mode first."); return; }
        if (!ExtendStrips()) return;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null
            && AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) != null)
        {
            string err = AssetDatabase.MoveAsset(SourcePrefabPath, PrefabPath);
            if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[TubeKit] Move failed: {err}"); return; }
            Debug.Log($"[TubeKit] Moved {SourcePrefabPath} → {PrefabPath}");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError($"[TubeKit] No Tube prefab at {PrefabPath} (or {SourcePrefabPath}).");
            return;
        }
        var frames = LoadFrames(StripPath);
        if (frames.Length < AuthoredTiles) { Debug.LogError($"[TubeKit] Only {frames.Length} sprites sliced in {StripPath}."); return; }
        var physicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhysicPath);
        if (physicPrefab == null) { Debug.LogError($"[TubeKit] No physic prefab at {PhysicPath}."); return; }
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
        var donorSR = donor != null ? donor.GetComponent<SpriteRenderer>() : null;

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var log = new List<string>();
            root.name = "Tube";
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
            // material stays the ore glow the prefab was authored with ("Glow Bright");
            // Tube cuts a copy of GS.Glow(Bright) at runtime and drives its `thecolor`

            // nested Physic body: one 0.25 cell, boxed just inside the sprite
            LifeScript life = root.GetComponentInChildren<LifeScript>(true);
            if (life == null)
            {
                var physic = (GameObject)PrefabUtility.InstantiatePrefab(physicPrefab, root.transform);
                physic.transform.localPosition = Vector3.zero;
                var box = physic.GetComponent<BoxCollider2D>();
                if (box != null) { box.size = new Vector2(0.22f, 0.22f); box.offset = Vector2.zero; }
                life = physic.GetComponent<LifeScript>();
                log.Add("Physic body added (0.22 box)");
            }

            // the contour comet's line: local space, no caps/corners (LineRenderer AABB gotcha)
            var contourT = root.transform.Find("Contour");
            LineRenderer contour;
            if (contourT == null)
            {
                var go = new GameObject("Contour");
                go.transform.SetParent(root.transform, false);
                go.layer = root.layer;
                contour = go.AddComponent<LineRenderer>();
                contour.useWorldSpace = false;
                contour.loop = true;
                contour.numCapVertices = 0;
                contour.numCornerVertices = 0;
                contour.widthMultiplier = 0.02f;
                contour.textureMode = LineTextureMode.Stretch;
                contour.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                contour.sortingLayerName = "Power Ups";
                contour.sortingOrder = 20;
                contour.startColor = contour.endColor = new Color(1f, 1f, 1f, 0f);
                contour.positionCount = 0;
                go.SetActive(false);
                log.Add("Contour child added");
            }
            else contour = contourT.GetComponent<LineRenderer>();

            // the shield fill: the solid (all-open) tile, era-tinted by the script, above the box
            var shieldT = root.transform.Find("Shield");
            SpriteRenderer shield;
            if (shieldT == null)
            {
                var go = new GameObject("Shield");
                go.transform.SetParent(root.transform, false);
                go.layer = root.layer;
                shield = go.AddComponent<SpriteRenderer>();
                shield.sprite = frames[Mathf.Min(4, frames.Length - 1)];
                shield.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                shield.sortingLayerID = sr.sortingLayerID;
                shield.sortingOrder = sr.sortingOrder + 1;
                shield.color = new Color(1f, 1f, 1f, 0f);
                go.SetActive(false);
                log.Add("Shield child added");
            }
            else shield = shieldT.GetComponent<SpriteRenderer>();

            var store = root.GetComponent<Tube>();
            if (store == null)
            {
                store = root.AddComponent<Tube>();
                store.enabled = false;             // ghost-disabled: the intended unbuilt state
                store.size = new Vector2(0.25f, 0.25f);
                store.maxHealth = 2f;
                store.builtBlasts = 1;
                store.oreRequired = 2;
                store.canOpen = true;   // keep-priority tiles live on the box UI
                store.rotatable = false;           // the connections decide the spin
                store.multiDrag = true;            // sweep rows of boxes like walls
                log.Add("Tube script added (ghost-disabled)");
            }
            store.sr = sr;
            store.icon = frames[0];
            store.frames = frames;
            store.physic = life;
            store.contour = contour;
            store.shieldFill = shield;
            store.intakePerChip = 0f;   // user rule 2026-09-14: a tube TRANSPORTS for free (the saved 0.025 fee is retired)
            ChipTransportKitUtil.StampIcon(store, Vector3.zero, 0.5f);   // the shape's no-energy sign, sized to a quarter cell

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TubeKit] {PrefabPath} upgraded: {frames.Length} connection sprites; "
                      + (log.Count > 0 ? string.Join(", ", log) : "already complete — refs refreshed"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ art

    /// <summary>Append the straight tile to both strips and slice it (see class doc).</summary>
    static bool ExtendStrips()
    {
        var basePx = ReadPixels(StripPath, out int bw, out int bh);
        var emPx = ReadPixels(EmissionPath, out int ew, out int eh);
        if (basePx == null) { Debug.LogError($"[TubeKit] Could not read {StripPath}."); return false; }
        if (bw == Tile * Tiles && bh == Tile)
        {
            EnsureSlices();   // art already extended — make sure the 6th slice exists
            return true;
        }
        if (bw != Tile * AuthoredTiles || bh != Tile)
        {
            Debug.LogError($"[TubeKit] {StripPath} is {bw}×{bh}; expected {Tile * AuthoredTiles}×{Tile} (five 8×8 tiles).");
            return false;
        }
        bool hasEm = emPx != null && ew == bw && eh == bh;
        if (!hasEm) Debug.LogWarning($"[TubeKit] {EmissionPath} missing or not {bw}×{bh} — only the base strip is extended.");

        int nw = Tile * Tiles;
        var nb = new Color32[nw * Tile];
        var ne = hasEm ? new Color32[nw * Tile] : null;
        for (int y = 0; y < Tile; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                nb[y * nw + x] = basePx[y * bw + x];
                if (hasEm) ne[y * nw + x] = emPx[y * bw + x];
            }
            // tile 1 ("open up") lives at x 8..15; y=7 is its open row (GetPixels32 is bottom-up)
            for (int x = 0; x < Tile; x++)
            {
                int tx = Tile + x;
                nb[y * nw + Tile * AuthoredTiles + x] = basePx[7 * bw + tx];
                if (!hasEm) continue;
                // side-wall lines run straight through (the open-row pattern), dark rows where the
                // authored tile breaks them, centre pips in the middle rows only
                int srcRow = (y == 5 || y == 2) ? 5 : (y == 4 || y == 3) ? 4 : 7;
                ne[y * nw + Tile * AuthoredTiles + x] = emPx[srcRow * bw + tx];
            }
        }
        WritePng(StripPath, nw, Tile, nb);
        if (hasEm) WritePng(EmissionPath, nw, Tile, ne);
        AssetDatabase.ImportAsset(StripPath, ImportAssetOptions.ForceUpdate);
        if (hasEm) AssetDatabase.ImportAsset(EmissionPath, ImportAssetOptions.ForceUpdate);
        EnsureSlices();
        Debug.Log($"[TubeKit] Straight tile derived and appended: {StripPath}{(hasEm ? " + " + EmissionPath : "")} now {nw}×{Tile}.");
        return true;
    }

    /// <summary>Six 8×8 slices named Tube_0..5 on the base strip, existing slice IDs kept.</summary>
    static void EnsureSlices()
    {
        var importer = AssetImporter.GetAtPath(StripPath) as TextureImporter;
        if (importer == null) { Debug.LogError($"[TubeKit] No texture importer at {StripPath}."); return; }
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var rects = new List<SpriteRect>(provider.GetSpriteRects() ?? new SpriteRect[0]);
        var nameIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        var pairs = new List<SpriteNameFileIdPair>();
        var oldPairs = nameIds != null ? nameIds.GetNameFileIdPairs() : null;
        if (oldPairs != null) pairs.AddRange(oldPairs);
        bool changed = false;
        for (int i = 0; i < Tiles; i++)
        {
            string name = "Tube_" + i;
            bool have = false;
            foreach (var r in rects) if (r.name == name) { have = true; break; }
            if (have) continue;
            var r2 = new SpriteRect
            {
                name = name,
                rect = new Rect(i * Tile, 0, Tile, Tile),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = GUID.Generate(),
            };
            rects.Add(r2);
            pairs.Add(new SpriteNameFileIdPair(name, r2.spriteID));
            changed = true;
        }
        if (!changed) return;
        provider.SetSpriteRects(rects.ToArray());
        if (nameIds != null && pairs.Count == rects.Count) nameIds.SetNameFileIdPairs(pairs);
        provider.Apply();
        importer.SaveAndReimport();
        Debug.Log($"[TubeKit] {StripPath} sliced into {rects.Count} tiles.");
    }

    static Color32[] ReadPixels(string assetPath, out int w, out int h)
    {
        w = h = 0;
        string full = System.IO.Path.GetFullPath(assetPath);
        if (!System.IO.File.Exists(full)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tex.LoadImage(System.IO.File.ReadAllBytes(full))) return null;
            w = tex.width; h = tex.height;
            return tex.GetPixels32();
        }
        finally { Object.DestroyImmediate(tex); }
    }

    static void WritePng(string assetPath, int w, int h, Color32[] px)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        try
        {
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(assetPath), tex.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(tex); }
    }

    /// <summary>Every sprite sliced off the strip, in numeric-suffix order (_0, _1, …).</summary>
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

    // ------------------------------------------------------------------ scene

    [MenuItem("Tools/Tube Kit/2 — Wire Into World Scene")]
    public static void WireScene()
    {
        if (Application.isPlaying) { Debug.LogWarning("[TubeKit] Stop play mode first."); return; }
        UpgradePrefab();
        var store = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (store == null) { Debug.LogError("[TubeKit] Tube prefab missing — prefab pass failed?"); return; }
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(AfterPrefabPath);
        var stripSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StripPath);

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
                if (list.Contains(store)) goto palette_done;
                int at = after != null ? list.IndexOf(after) : -1;
                if (at < 0) continue;
                list.Insert(at + 1, store);
                daddy.buildings = list.ToArray();
                EditorUtility.SetDirty(daddy);
                dirty = true;
                Debug.Log($"[TubeKit] Tube slotted after the Collector in '{daddy.name}'.");
                goto palette_done;
            }
        }
        palette_done:
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var bpm in root.GetComponentsInChildren<BlueprintManager>(true))
            {
                if (bpm.defaultBuildings == null || bpm.defaultBuildings.Contains(store)) continue;
                bpm.defaultBuildings.Add(store);
                EditorUtility.SetDirty(bpm);
                dirty = true;
                Debug.Log("[TubeKit] Tube added to defaultBuildings.");
            }
        }
        // the hand-made preview: a root "Tube" object that is just a SpriteRenderer of the
        // strip (no Building) — the prefab took its place
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "Tube" || root.GetComponent<Building>() != null) continue;
            if (PrefabUtility.IsPartOfAnyPrefab(root)) continue;
            var psr = root.GetComponent<SpriteRenderer>();
            if (psr == null || psr.sprite == null || root.GetComponents<Component>().Length > 2) continue;
            if (stripSprite != null && psr.sprite.texture != stripSprite.texture) continue;
            Object.DestroyImmediate(root);
            dirty = true;
            Debug.Log("[TubeKit] Removed the sprite-only 'Tube' preview object from the scene.");
            break;
        }
        if (dirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TubeKit] World scene saved.");
        }
        else Debug.Log("[TubeKit] Already wired — nothing to do.");
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
    }
}
