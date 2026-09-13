using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Authors the ember readout (<see cref="ResourceBarsUI"/>) as a REAL scene object under the
/// UI/Resources panel, instead of the old runtime Attach() that built it on play ([[prefab-visuals-not-code]]):
/// every strip, frame, cap, tick template and label is a child you can see, move and recolour in
/// the inspector. The script only drives fills/ticks/label at runtime.
///
/// 1 — Author: skip-if-exists (safe to re-run; re-wires refs and retires the old W/G/B/R counters).
/// 2 — Rebuild: deletes the authored object and re-creates it at kit defaults, discarding tweaks.
/// </summary>
public static class ResourceBarsKitBuilder
{
    const string ScenePath = "Assets/Scenes/World.unity";
    const string GradPath = "Assets/Sprites/UI/BarGradient.png";
    const string ObjName = "ResourceBars";

    // geometry (canvas px, panel is 300 wide anchored to the screen's right edge)
    const float BarW = 265f;
    const float ThickH = 22f;
    const float ThinH = 9f;
    const float Divide = 2f;      // hairline between the two strips of a slot
    const float Border = 2f;      // frame thickness around the slot
    const float RowSpacing = 86f;
    const float TickW = 2f;
    const float RightPad = 20f;
    const float TopPad = 30f;
    const float LabelH = 30f;

    static readonly Color frameCol = new(0.016f, 0.016f, 0.04f, 1f);   // near-black, fully opaque — the crisp edge
    static readonly Color backCol = new(0.059f, 0.059f, 0.11f, 1f);    // slot interior, a shade above the frame

    [MenuItem("Tools/Resource Bar Kit/1 — Author Into World Scene")]
    public static void Author() => Run(false);

    [MenuItem("Tools/Resource Bar Kit/2 — Rebuild (discards tweaks)")]
    public static void Rebuild()
    {
        if (!EditorUtility.DisplayDialog("Rebuild resource bar?",
                "This deletes the authored ResourceBars object and re-creates it at kit defaults.\n\nAny inspector tweaks (sizes, colours, positions) are lost.", "Rebuild", "Cancel"))
            return;
        Run(true);
    }

    static void Run(bool rebuild)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.path != ScenePath)
        {
            Debug.LogError("[ResourceBarKit] Open World.unity first.");
            return;
        }

        var rm = Object.FindAnyObjectByType<ResourceManager>(FindObjectsInactive.Include);
        if (rm == null) { Debug.LogError("[ResourceBarKit] No ResourceManager in the scene."); return; }
        if (rm.resourceUIs == null || rm.resourceUIs.Length == 0 || rm.resourceUIs[0] == null)
        {
            Debug.LogError("[ResourceBarKit] ResourceManager.resourceUIs is empty — can't find the Resources panel.");
            return;
        }

        var panel = rm.resourceUIs[0].rectTransform.parent as RectTransform;
        if (panel == null) { Debug.LogError("[ResourceBarKit] Resources panel not found."); return; }

        Transform existing = panel.Find(ObjName);
        if (existing != null && rebuild) { Object.DestroyImmediate(existing.gameObject); existing = null; }

        ResourceBarsUI ui;
        if (existing != null)
        {
            ui = existing.GetComponent<ResourceBarsUI>();
            if (ui == null) ui = existing.gameObject.AddComponent<ResourceBarsUI>();
            Debug.Log("[ResourceBarKit] ResourceBars already authored — re-wiring refs only (run '2 — Rebuild' to reset it).");
        }
        else
        {
            ui = BuildHierarchy(panel, rm.resourceUIs[0].font);
            Debug.Log("[ResourceBarKit] ResourceBars authored under " + panel.name + ".");
        }

        WireRefs(ui);

        // the bars ARE the readout now — the old per-element text counters retire
        foreach (TextMeshProUGUI t in rm.resourceUIs)
        {
            if (t == null || !t.gameObject.activeSelf) continue;
            t.gameObject.SetActive(false);
            EditorUtility.SetDirty(t.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = ui.gameObject;
        Debug.Log("[ResourceBarKit] World scene saved. Select 'ResourceBars' to tune it.");
    }

    // ------------------------------------------------------------------ hierarchy

    static ResourceBarsUI BuildHierarchy(RectTransform panel, TMP_FontAsset font)
    {
        var go = new GameObject(ObjName, typeof(RectTransform));
        var ui = go.AddComponent<ResourceBarsUI>();
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-RightPad, -TopPad);
        rt.sizeDelta = new Vector2(BarW, RowSpacing);

        RectTransform root = MakeRect("Row0", rt, BarW, RowSpacing);

        // one fused slot: opaque frame → interior → thick strip below, thin strip above a hairline
        float innerH = ThickH + Divide + ThinH;
        RectTransform frame = MakeImg("Frame", root, BarW + 2f * Border, innerH + 2f * Border, frameCol).rectTransform;
        RectTransform inner = MakeRect("Inner", frame, BarW, innerH);
        inner.anchoredPosition = new Vector2(-Border, Border);
        MakeImg("Back", inner, BarW, innerH, backCol);

        RectTransform thick = MakeRect("Store", inner, BarW, ThickH);
        RectTransform thin = MakeRect("Held", inner, BarW, ThinH);
        thin.anchoredPosition = new Vector2(0f, ThickH + Divide);
        MakeImg("Divide", inner, BarW, Divide, frameCol).rectTransform.anchoredPosition = new Vector2(0f, ThickH);

        Color preview = PreviewCol();
        MakeStrip(thick, "StoreTicks", preview, 0.6f, true);
        MakeStrip(thin, "HeldTicks", Color.Lerp(preview, Color.white, 0.3f), 0.25f, false);

        var lgo = new GameObject("Label", typeof(RectTransform));
        var label = lgo.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.BottomRight;
        label.characterSpacing = 2f;
        label.raycastTarget = false;
        label.text = "0/0";
        label.color = Color.Lerp(preview, Color.white, 0.45f);
        var lrt = (RectTransform)lgo.transform;
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(1f, 0f);
        lrt.anchoredPosition = new Vector2(0f, innerH + 2f * Border + 3f);
        lrt.sizeDelta = new Vector2(BarW, LabelH);
        return ui;
    }

    /// <summary>Fill + leading-edge cap + the tick holder. ONE strip carries the inactive Tick
    /// template (<paramref name="withTemplate"/>) and both holders clone it — the hairline
    /// stretches to whichever holder it lands in, so a single authored tick styles both.
    /// <paramref name="fillFrac"/> is edit-mode dressing so the bar reads at a glance in the
    /// inspector — the runtime overwrites it on the first LateUpdate.</summary>
    static void MakeStrip(RectTransform strip, string ticksName, Color col, float fillFrac, bool withTemplate)
    {
        var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
        fill.sprite = Gradient();
        fill.color = col;
        fill.raycastTarget = false;
        StretchV(fill.rectTransform, strip, new Vector2(0f, 0.5f), new Vector2(BarW * fillFrac, 0f));

        var cap = new GameObject("Cap", typeof(RectTransform)).AddComponent<Image>();
        cap.color = Color.Lerp(col, Color.white, 0.65f);
        cap.raycastTarget = false;
        StretchV(cap.rectTransform, strip, new Vector2(1f, 0.5f), new Vector2(2f, 0f));
        cap.rectTransform.anchoredPosition = new Vector2(BarW * fillFrac, 0f);

        RectTransform ticks = MakeRect(ticksName, strip, BarW, strip.rect.height);
        if (!withTemplate) return;

        var tick = new GameObject("Tick (template)", typeof(RectTransform)).AddComponent<Image>();
        tick.color = frameCol;
        tick.raycastTarget = false;
        StretchV(tick.rectTransform, ticks, new Vector2(0.5f, 0.5f), new Vector2(TickW, 0f));
        tick.gameObject.SetActive(false);   // cloned per tick at runtime; never drawn itself
    }

    /// <summary>Left-anchored, vertically stretched to the strip — the fill/cap/tick recipe.</summary>
    static void StretchV(RectTransform rt, RectTransform parent, Vector2 pivot, Vector2 size)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    static RectTransform MakeRect(string nam, RectTransform parent, float w, float h)
    {
        var rt = (RectTransform)new GameObject(nam, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static Image MakeImg(string nam, RectTransform parent, float w, float h, Color col)
    {
        var img = new GameObject(nam, typeof(RectTransform)).AddComponent<Image>();
        img.color = col;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        return img;
    }

    /// <summary>Era-1 colour if the scene can offer it — edit-mode dressing only (the runtime
    /// re-samples GS.ColFromEra every frame).</summary>
    static Color PreviewCol()
    {
        var sm = Object.FindAnyObjectByType<SpawnManager>(FindObjectsInactive.Include);
        return sm != null ? sm.e1 : new Color(0.85f, 0.55f, 0.25f, 1f);
    }

    // ------------------------------------------------------------------ refs

    static void WireRefs(ResourceBarsUI ui)
    {
        Transform t = ui.transform;
        var so = new SerializedObject(ui);
        Set(so, "storeFill", Find<Image>(t, "Row0/Frame/Inner/Store/Fill"));
        Set(so, "storeCap", Find<Image>(t, "Row0/Frame/Inner/Store/Cap"));
        Set(so, "storeTicks", Find<RectTransform>(t, "Row0/Frame/Inner/Store/StoreTicks"));
        Set(so, "heldFill", Find<Image>(t, "Row0/Frame/Inner/Held/Fill"));
        Set(so, "heldCap", Find<Image>(t, "Row0/Frame/Inner/Held/Cap"));
        Set(so, "heldTicks", Find<RectTransform>(t, "Row0/Frame/Inner/Held/HeldTicks"));
        Set(so, "tickTemplate", Find<Image>(t, "Row0/Frame/Inner/Store/StoreTicks/Tick (template)"));
        Set(so, "label", Find<TextMeshProUGUI>(t, "Row0/Label"));
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    static void Set(SerializedObject so, string prop, Object val)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Debug.LogWarning($"[ResourceBarKit] No serialized field '{prop}' on ResourceBarsUI."); return; }
        if (val == null) Debug.LogWarning($"[ResourceBarKit] Child for '{prop}' not found — wire it by hand.");
        p.objectReferenceValue = val;
    }

    static T Find<T>(Transform root, string path) where T : Object
    {
        Transform c = root.Find(path);
        if (c == null) return null;
        return typeof(T) == typeof(RectTransform) ? c as T : c.GetComponent<T>();
    }

    // ------------------------------------------------------------------ gradient sprite asset

    /// <summary>The shared 1×4 vertical sheen (bright crown → dark base) the fills are tinted
    /// with — a real asset now, so it shows in the inspector and can be swapped for hand art.</summary>
    static Sprite Gradient()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(GradPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Sprites/UI")) AssetDatabase.CreateFolder("Assets/Sprites", "UI");
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 3, Color.white);
        tex.SetPixel(0, 2, Color.white);
        tex.SetPixel(0, 1, new Color(0.85f, 0.85f, 0.85f, 1f));
        tex.SetPixel(0, 0, new Color(0.6f, 0.6f, 0.6f, 1f));
        tex.Apply();
        System.IO.File.WriteAllBytes(GradPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GradPath, ImportAssetOptions.ForceSynchronousImport);

        var imp = (TextureImporter)AssetImporter.GetAtPath(GradPath);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 4f;
        imp.filterMode = FilterMode.Bilinear;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        Debug.Log("[ResourceBarKit] Built " + GradPath);
        return AssetDatabase.LoadAssetAtPath<Sprite>(GradPath);
    }
}
