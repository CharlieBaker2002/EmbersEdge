using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Classifies the ore-chip strip (Assets/Resources/OreChips.png, 12 slices) by its ARTWORK:
/// the chip's size class is how many ember/emission pixels it carries — 1 = small, 2 = medium,
/// 3 = large. The runtime picks a sprite as OreChips_{sizeClass*4 + 0..3}, so the slices are
/// renamed such that _0.._3 are the one-pixel chips, _4.._7 the two-pixel, _8.._11 the three-
/// pixel (x-order within a class). Emission pixels are counted on OreChipsE.png (the strip's
/// _Emission secondary); each slice keeps its own file ID through the rename, so prefab
/// references to a slice still point at the same artwork.
///   Tools/Ore Chip Kit/Classify Chip Sprites By Emission
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod OreChipKitBuilder.Classify
/// </summary>
public static class OreChipKitBuilder
{
    const string StripPath = "Assets/Resources/OreChips.png";
    const string EmissionPath = "Assets/Resources/OreChipsE.png";
    const int PerClass = 4;

    [MenuItem("Tools/Ore Chip Kit/Classify Chip Sprites By Emission")]
    public static void Classify()
    {
        if (Application.isPlaying) { Debug.LogWarning("[OreChipKit] Stop play mode first."); return; }

        var importer = AssetImporter.GetAtPath(StripPath) as TextureImporter;
        if (importer == null) { Debug.LogError($"[OreChipKit] No texture importer at {StripPath}."); return; }
        var emission = ReadPixels(EmissionPath, out int w, out int h);
        if (emission == null) { Debug.LogError($"[OreChipKit] Could not read {EmissionPath}."); return; }

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var rects = provider.GetSpriteRects();
        if (rects == null || rects.Length == 0) { Debug.LogError("[OreChipKit] Strip has no slices."); return; }

        // count the glowing pixels inside each slice
        var scored = new List<(SpriteRect rect, int glow)>();
        foreach (var r in rects) scored.Add((r, CountGlow(emission, w, h, r.rect)));
        scored.Sort((a, b) => a.glow != b.glow ? a.glow.CompareTo(b.glow) : a.rect.rect.x.CompareTo(b.rect.rect.x));

        // class = glow pixels − 1 (1 → small, 2 → medium, 3 → large); each class must own PerClass slots
        var byClass = new List<List<(SpriteRect rect, int glow)>> { new(), new(), new() };
        foreach (var s in scored) byClass[Mathf.Clamp(s.glow - 1, 0, 2)].Add(s);
        for (int c = 0; c < 3; c++)
            if (byClass[c].Count != PerClass)
                Debug.LogWarning($"[OreChipKit] Class {c} ({c + 1} emission px) has {byClass[c].Count} slices, expected {PerClass} — the runtime's {PerClass}-per-class indexing will be off.");

        // file IDs follow the ARTWORK: new name ← the id the slice had under its old name
        var nameIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        var oldPairs = nameIds != null ? nameIds.GetNameFileIdPairs() : null;
        var oldIdByName = new Dictionary<string, GUID>();
        if (oldPairs != null) foreach (var p in oldPairs) oldIdByName[p.name] = p.GetFileGUID();

        var newPairs = new List<SpriteNameFileIdPair>();
        var rows = new List<string>();
        int renamed = 0, index = 0;
        for (int c = 0; c < 3; c++)
        {
            foreach (var s in byClass[c])
            {
                string oldName = s.rect.name;
                string newName = "OreChips_" + index;
                if (oldName != newName) renamed++;
                s.rect.name = newName;
                if (oldIdByName.TryGetValue(oldName, out GUID id)) newPairs.Add(new SpriteNameFileIdPair(newName, id));
                rows.Add($"{newName,-12} ← {oldName,-12} glow={s.glow} x={s.rect.rect.x,3} size={s.rect.rect.width}x{s.rect.rect.height}  class {c}");
                index++;
            }
        }
        if (renamed == 0)
        {
            Debug.Log("[OreChipKit] Slices already classified by emission — nothing to do.\n" + string.Join("\n", rows));
            return;
        }

        provider.SetSpriteRects(rects);
        if (nameIds != null && newPairs.Count == rects.Length) nameIds.SetNameFileIdPairs(newPairs);
        provider.Apply();
        importer.SaveAndReimport();
        Debug.Log($"[OreChipKit] Renamed {renamed} slice(s) so size class = emission pixels − 1:\n" + string.Join("\n", rows));
    }

    /// <summary>Glowing texels inside a slice rect (any visible non-black emission).</summary>
    static int CountGlow(Color32[] px, int w, int h, Rect r)
    {
        int n = 0;
        int x0 = Mathf.RoundToInt(r.xMin), y0 = Mathf.RoundToInt(r.yMin);
        int x1 = Mathf.RoundToInt(r.xMax), y1 = Mathf.RoundToInt(r.yMax);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var c = px[y * w + x];
                if (c.a > 0 && (c.r > 0 || c.g > 0 || c.b > 0)) n++;
            }
        return n;
    }

    /// <summary>Raw pixels straight off the PNG (independent of import settings / compression).</summary>
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
}
