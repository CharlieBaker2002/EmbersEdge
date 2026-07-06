using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mine Forge — visual authoring tool for MineAuthoringSO (the mining analogue of Wave Forge).
/// Tools > Mine Forge.  Era tabs -> a library of POCKET templates.  Per pocket you:
///   • PAINT the pocket's space (the allowed region / carved cavity) on a cell grid, and
///   • for each WAVE, drag-and-drop enemies/objects FREE-FORM into that space (later waves can reuse
///     the same spots — they spawn at different times).  Drops outside the painted space are rejected.
/// Scores are live suggestions you can override.  Auto-saves to Resources/MineAuthoring.asset.
///
/// IMGUI note (same discipline as WaveForgeWindow): every click that changes which pocket/wave is shown
/// or adds/removes/reorders a list item is deferred to the end of OnGUI via `pending`. The placement
/// canvas mutates the data lists inline (a single GetRect control — like a paint surface — so it never
/// changes the GUILayout control count).
/// </summary>
public class MineForgeWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/MineAuthoring.asset";

    MineAuthoringSO data;

    int era;          // 0/1/2
    int pocketIndex;  // selected pocket within era
    int waveIndex;    // selected wave within pocket
    bool combiMode;   // editing the era's combi-pocket library instead of its pockets
    bool spawnerMode; // editing the era's SPAWNERS instead of pockets
    int spawnerIndex; // selected spawner within era
    int dayIndex;     // selected day within spawner

    // Three top-level brush categories. Tiles carves the cavity; Extras places objects/Core/Combi into the
    // pocket; Enemies places the dungeon's enemies into the active wave. Left-click places, right-click
    // deletes — both scoped to the selected category.
    enum BrushCategory { Tiles, Extras, Enemies }
    enum BrushMode { Tile, Enemy, Object, Core, Combi }
    BrushMode brushMode = BrushMode.Enemy;
    MarauderSO brushEnemy;
    GameObject brushObject;
    PocketTileType brushTile = PocketTileType.Wall;   // which cell type the Tiles brush paints
    int tileBrushSize = 1;    // Tiles brush footprint, square: 1 / 2 / 4 / 8 cells
    bool snapToGrid = true;   // snap free-form enemy/object placement to the nearest 0.5-cell grid point
    float brushChance = 1f;   // spawn chance written onto each EXTRA (Object brush) placed — enemies have none
    bool symX;                // mirror every placement/paint left↔right (across the vertical centre line)
    bool symY;                // mirror every placement/paint top↕bottom (across the horizontal centre line)

    // Which category a brush mode belongs to (Object/Core/Combi are all "Extras").
    BrushCategory Cat =>
        brushMode == BrushMode.Tile ? BrushCategory.Tiles :
        brushMode == BrushMode.Enemy ? BrushCategory.Enemies :
        BrushCategory.Extras;

    // canvas drag state
    enum DragKind { None, Enemy, Extra, Core }
    DragKind dragKind = DragKind.None;
    int dragIdx = -1;        // index into the dragged list (wave enemies or pocket extras)
    bool paintingSpace;      // mouse held while painting the space brush
    bool eraseSpaceDrag;     // that paint stroke is erasing

    bool settingsFold;
    Vector2 listScroll, editScroll;

    bool dirty;
    System.Action pending;
    readonly Dictionary<Object, Sprite> spriteCache = new Dictionary<Object, Sprite>();

    static readonly Color ColBg      = new Color(0.16f, 0.16f, 0.19f);
    static readonly Color ColPanel   = new Color(0.21f, 0.21f, 0.25f);
    static readonly Color ColAccent  = new Color(0.95f, 0.55f, 0.22f);
    static readonly Color ColSel     = new Color(0.30f, 0.55f, 0.95f);
    static readonly Color ColCore    = new Color(0.95f, 0.55f, 0.22f, 0.7f);
    static readonly Color ColCell    = new Color(0.27f, 0.27f, 0.32f);
    static readonly Color ColCellAlt = new Color(0.24f, 0.24f, 0.29f);
    static readonly Color ColVoid    = new Color(0.12f, 0.12f, 0.14f);
    static readonly Color ColCombi   = new Color(0.55f, 0.85f, 0.95f, 0.6f);
    static readonly Color ColExtra   = new Color(0.20f, 0.40f, 0.22f, 0.55f);
    static readonly Color ColTile    = new Color(0.42f, 0.35f, 0.29f);   // painted solid tile (obstacle)

    GUIStyle hdr, sub, tally, gridCellName, orderLabel, combiArrow;
    bool stylesReady;

    static Vector2Int CombiOutward(Vector2Int tile, int w, int h)
    {
        float dx = (tile.x + 0.5f) - w * 0.5f, dy = (tile.y + 0.5f) - h * 0.5f;
        return Mathf.Abs(dx) >= Mathf.Abs(dy)
            ? new Vector2Int(dx >= 0 ? 1 : -1, 0)
            : new Vector2Int(0, dy >= 0 ? 1 : -1);
    }

    static string Glyph(Vector2Int d)
        => d == Vector2Int.up ? "▲" : d == Vector2Int.down ? "▼" : d == Vector2Int.right ? "▶" : "◀";

    [MenuItem("Tools/Mine Forge")]
    static void Open()
    {
        var w = GetWindow<MineForgeWindow>("Mine Forge");
        w.minSize = new Vector2(860, 580);
        w.Show();
    }

    // Hover tooltip: the name of the obj / tile / enemy under the cursor (set during Repaint, drawn last).
    string hoverTip;
    GUIStyle hoverStyle;

    void OnEnable()
    {
        data = AssetDatabase.LoadAssetAtPath<MineAuthoringSO>(AssetPath);
        wantsMouseMove = true; // so hover tooltips track the cursor live
    }

    void MarkDirty()
    {
        dirty = true;
        if (data) EditorUtility.SetDirty(data);
    }

    void OnInspectorUpdate()
    {
        if (dirty) { AssetDatabase.SaveAssets(); dirty = false; }
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    void OnDisable()
    {
        if (data) { EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); }
    }

    // -----------------------------------------------------------------------------------------
    void OnGUI()
    {
        EnsureStyles();
        if (Event.current.type == EventType.MouseMove) Repaint(); // keep the hover tooltip following the cursor
        hoverTip = null;
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ColBg);

        if (data == null) { DrawNoAsset(); return; }

        DrawHeader();
        DrawEraTabs();
        DrawPaletteBar();

        EditorGUILayout.BeginHorizontal();
        DrawPocketList();
        DrawPocketEditor();
        EditorGUILayout.EndHorizontal();

        DrawHoverTip();

        if (pending != null) { var p = pending; pending = null; p(); Repaint(); }
    }

    void DrawNoAsset()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical(GUILayout.Width(440));
        GUILayout.Label("Mine Forge", hdr);
        EditorGUILayout.HelpBox("No MineAuthoring asset found at\n" + AssetPath, MessageType.Info);
        if (GUILayout.Button("Create MineAuthoring asset", GUILayout.Height(34)))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var so = CreateInstance<MineAuthoringSO>();
            for (int e = 0; e < MineAuthoringSO.ERAS; e++) so.GetEra(e); // seed eras + a boss pocket each
            AssetDatabase.CreateAsset(so, AssetPath);
            AssetDatabase.SaveAssets();
            data = so;
        }
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    // ---- header / settings ----------------------------------------------------------------
    void DrawHeader()
    {
        var r = EditorGUILayout.GetControlRect(false, 30);
        EditorGUI.DrawRect(r, ColPanel);
        GUI.Label(new Rect(r.x + 8, r.y + 5, r.width - 220, 20), "⛏  MINE FORGE", hdr);
        bool foldNow = settingsFold;
        settingsFold = GUI.Toggle(new Rect(r.xMax - 200, r.y + 5, 130, 20), settingsFold, "Settings / Palette", EditorStyles.miniButton);
        GUI.Label(new Rect(r.xMax - 64, r.y + 5, 60, 20), dirty ? "saving…" : "saved", EditorStyles.miniLabel);
        if (foldNow) DrawSettings();
    }

    void DrawSettings()
    {
        EditorGUILayout.BeginVertical("box");
        var so = new SerializedObject(data);
        so.Update();
        EditorGUILayout.LabelField("Brushes (palettes)", EditorStyles.boldLabel);
        DrawSharedEnemyPalette();
        string objField = era == 0 ? "era1Objects" : era == 1 ? "era2Objects" : "era3Objects";
        var objProp = so.FindProperty(objField);
        if (objProp != null) EditorGUILayout.PropertyField(objProp, new GUIContent($"Objects (era {era + 1})"), true);
        if (so.ApplyModifiedProperties()) MarkDirty();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox("Generation tuning (dungeon area, ore clusters, rock hardness, point budgets) " +
            "lives in the SCENE on the MineDungeonManager object / its MineField component — the Mine " +
            "Forge asset is content only (pockets, combi-pockets, spawners).", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    // The per-era enemy roster is shared from Wave Forge (WaveAuthoring) so the two forges never paint
    // from different enemy sets. Shown read-only here; edit it over in Tools > Wave Forge.
    void DrawSharedEnemyPalette()
    {
        var wave = AssetDatabase.LoadAssetAtPath<WaveAuthoringSO>("Assets/Resources/WaveAuthoring.asset");
        if (wave == null)
        {
            EditorGUILayout.HelpBox("Enemy palette falls back to MineAuthoring's local arrays — no " +
                "WaveAuthoring asset found at Assets/Resources/WaveAuthoring.asset.", MessageType.Warning);
            var so = new SerializedObject(data);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("era1Enemies"), true);
            EditorGUILayout.PropertyField(so.FindProperty("era2Enemies"), true);
            EditorGUILayout.PropertyField(so.FindProperty("era3Enemies"), true);
            if (so.ApplyModifiedProperties()) MarkDirty();
            return;
        }

        EditorGUILayout.LabelField($"Enemies — Era {era + 1}  (shared from Wave Forge)", EditorStyles.miniBoldLabel);
        var pal = wave.Palette(era);
        if (pal == null || pal.Length == 0)
            EditorGUILayout.LabelField("   (Wave Forge dungeon " + (era + 1) + " palette is empty)", EditorStyles.miniLabel);
        else
        {
            using (new EditorGUI.DisabledScope(true))
                foreach (var so in pal)
                    EditorGUILayout.ObjectField(so, typeof(MarauderSO), false);
        }
        if (GUILayout.Button("Open Wave Forge to edit", EditorStyles.miniButton, GUILayout.Width(180)))
            EditorApplication.ExecuteMenuItem("Tools/Wave Forge");
    }

    // ---- era tabs -------------------------------------------------------------------------
    void DrawEraTabs()
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        for (int e = 0; e < MineAuthoringSO.ERAS; e++)
        {
            bool on = era == e;
            GUI.backgroundColor = on ? ColAccent : ColPanel;
            if (GUILayout.Toggle(on, $"Era {e + 1}", EditorStyles.miniButton, GUILayout.Height(24)) && !on)
            { int ee = e; pending = () => { era = ee; pocketIndex = 0; waveIndex = 0; }; }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    // ---- palette / brush bar --------------------------------------------------------------
    void DrawPaletteBar()
    {
        var pal = data.Palette(era);
        EditorGUILayout.BeginVertical("box");

        // Category tabs. A category swap changes which sub-options draw, so it's DEFERRED (pending) to keep
        // the Layout/Repaint control counts matched (same discipline as the era/pocket tabs).
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Brush", GUILayout.Width(38));
        CategoryTab("Tiles", BrushCategory.Tiles, 64);
        CategoryTab("Extras", BrushCategory.Extras, 64);
        CategoryTab("Enemies", BrushCategory.Enemies, 72);
        GUILayout.FlexibleSpace();
        // Symmetry: every paint/placement is mirrored across the pocket's centre line(s). Both on = 4-way.
        symX = GUILayout.Toggle(symX, new GUIContent("Sym ↔",
            "Mirror painting & placement left↔right (across the vertical centre line). Combine with Sym ↕ for 4-way."),
            EditorStyles.miniButton, GUILayout.Width(56), GUILayout.Height(28));
        symY = GUILayout.Toggle(symY, new GUIContent("Sym ↕",
            "Mirror painting & placement top↕bottom (across the horizontal centre line). Combine with Sym ↔ for 4-way."),
            EditorStyles.miniButton, GUILayout.Width(56), GUILayout.Height(28));
        GUILayout.Space(6);
        // Free-form placement snap (enemies + objects) — lands them on the nearest 0.5-cell grid point.
        snapToGrid = GUILayout.Toggle(snapToGrid, new GUIContent("Snap ½",
            "Snap placed/dragged enemies & objects to the nearest 0.5×0.5 grid point. Off = fully free-form."),
            EditorStyles.miniButton, GUILayout.Width(64), GUILayout.Height(28));
        EditorGUILayout.EndHorizontal();

        // Sub-options for the OPEN category only.
        EditorGUILayout.BeginHorizontal();
        switch (Cat)
        {
            case BrushCategory.Tiles:
                TileBrushButton("Empty", PocketTileType.Empty, 52);
                TileBrushButton("Wall", PocketTileType.Wall, 46);
                TileBrushButton("Hard", PocketTileType.HardWall, 46);
                TileBrushButton("V.Hard", PocketTileType.VeryHardWall, 52);
                GUILayout.Space(8);
                TileBrushButton("Stun", PocketTileType.Stun, 46);
                TileBrushButton("Slow", PocketTileType.Slow, 46);
                TileBrushButton("Speed", PocketTileType.Speed, 50);
                TileBrushButton("Root", PocketTileType.Root, 46);
                TileBrushButton("Launch", PocketTileType.Launch, 54);
                GUILayout.Space(12);
                GUILayout.Label("Size", GUILayout.Width(30));
                TileSizeButton(1); TileSizeButton(2); TileSizeButton(4); TileSizeButton(8);
                break;

            case BrushCategory.Enemies:
                if (pal == null || pal.Length == 0)
                    GUILayout.Label("No enemies for this dungeon — assign them in Tools > Wave Forge.", EditorStyles.miniLabel);
                else
                    foreach (var so in pal)
                    {
                        if (so == null) continue;
                        bool seld = brushMode == BrushMode.Enemy && brushEnemy == so;
                        GUI.backgroundColor = seld ? ColSel : ColPanel;
                        var r = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
                        if (GUI.Button(r, GUIContent.none)) { brushEnemy = so; brushMode = BrushMode.Enemy; }
                        GUI.backgroundColor = Color.white;
                        DrawIcon(Inset(r, 3), GetSprite(so), so.name);
                        Hover(r, so.name);
                        GUI.Label(new Rect(r.x + 1, r.yMax - 12, r.width - 2, 11), ((int)MineAuthoringSO.EnemyPoints(so)).ToString(), tally);
                    }
                break;

            case BrushCategory.Extras:
                BrushButton("Core", BrushMode.Core, 50);
                BrushButton("Combi", BrushMode.Combi, 56);
                var objPal = data.ObjectsPalette(era);
                if (objPal != null && objPal.Length > 0)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("Obj", GUILayout.Width(26));
                    foreach (var go in objPal)
                    {
                        if (go == null) continue;
                        bool seld = brushMode == BrushMode.Object && brushObject == go;
                        GUI.backgroundColor = seld ? ColSel : ColPanel;
                        var r = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
                        if (GUI.Button(r, GUIContent.none)) { brushObject = go; brushMode = BrushMode.Object; }
                        GUI.backgroundColor = Color.white;
                        DrawIcon(Inset(r, 3), GetSprite(go), go.name);
                        Hover(r, go.name);
                    }
                }
                // Spawn chance written onto each EXTRA you paint — the only placement-randomness source
                // (nothing self-scatters). Enemies ignore this and always spawn.
                GUILayout.Space(10);
                GUILayout.Label(new GUIContent("Extra %", "Probability each painted EXTRA (object) actually spawns. " +
                    "1 = always; set < 1 for occasional props/traps. Enemies always spawn (no chance). " +
                    "Edit per-extra in the Pocket Extras list."), GUILayout.Width(52));
                brushChance = Mathf.Clamp01(EditorGUILayout.Slider(brushChance, 0f, 1f, GUILayout.Width(150)));
                break;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    void CategoryTab(string label, BrushCategory cat, float w)
    {
        bool on = Cat == cat;
        GUI.backgroundColor = on ? ColAccent : ColPanel;
        if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(w), GUILayout.Height(28)) && !on)
            pending = () => SetCategory(cat);
        GUI.backgroundColor = Color.white;
    }

    // Pick a sensible default sub-brush when switching into a category.
    void SetCategory(BrushCategory cat)
    {
        switch (cat)
        {
            case BrushCategory.Tiles: brushMode = BrushMode.Tile; break;
            case BrushCategory.Enemies: brushMode = BrushMode.Enemy; break;
            default: brushMode = brushObject != null ? BrushMode.Object : BrushMode.Core; break;
        }
    }

    void TileBrushButton(string label, PocketTileType type, float w)
    {
        GUI.backgroundColor = brushTile == type ? ColSel : ColPanel;
        if (GUILayout.Button(label, GUILayout.Width(w), GUILayout.Height(34))) brushTile = type;
        GUI.backgroundColor = Color.white;
    }

    // Square brush footprint for the Tiles category: paints an n×n block of cells per click/drag.
    void TileSizeButton(int n)
    {
        GUI.backgroundColor = tileBrushSize == n ? ColSel : ColPanel;
        if (GUILayout.Button($"{n}×{n}", GUILayout.Width(38), GUILayout.Height(34))) tileBrushSize = n;
        GUI.backgroundColor = Color.white;
    }

    // Canvas cell colour by painted type: walls are solid shades (darker = harder); status tiles are open
    // cavity tinted toward their element; Empty is the plain checkerboard.
    Color TileColor(PocketTileType t, bool altCell)
    {
        Color cavity = altCell ? ColCell : ColCellAlt;
        switch (t)
        {
            case PocketTileType.Wall:         return ColTile;
            case PocketTileType.HardWall:     return new Color(0.34f, 0.38f, 0.46f);
            case PocketTileType.VeryHardWall: return new Color(0.19f, 0.21f, 0.30f);
            case PocketTileType.Stun:   return Color.Lerp(cavity, new Color(0.35f, 0.45f, 1f), 0.5f);
            case PocketTileType.Slow:   return Color.Lerp(cavity, new Color(0.3f, 0.85f, 1f), 0.5f);
            case PocketTileType.Speed:  return Color.Lerp(cavity, new Color(0.35f, 1f, 0.45f), 0.5f);
            case PocketTileType.Root:   return Color.Lerp(cavity, new Color(0.2f, 0.6f, 0.25f), 0.55f);
            case PocketTileType.Launch: return Color.Lerp(cavity, new Color(1f, 0.55f, 0.2f), 0.5f);
            default:                    return cavity;   // Empty
        }
    }

    void BrushButton(string label, BrushMode mode, float w)
    {
        GUI.backgroundColor = brushMode == mode ? ColSel : ColPanel;
        if (GUILayout.Button(label, GUILayout.Width(w), GUILayout.Height(34))) brushMode = mode;
        GUI.backgroundColor = Color.white;
    }

    List<PocketTemplate> CurrentPockets(EraMine em) => combiMode ? em.combiPockets : em.pockets;

    // ---- pocket list column ---------------------------------------------------------------
    void DrawPocketList()
    {
        var em = data.GetEra(era);
        EditorGUILayout.BeginVertical(GUILayout.Width(190));

        // mode toggle: main pockets vs the combi-pocket library vs the era's spawners
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = (!combiMode && !spawnerMode) ? ColAccent : ColPanel;
        if (GUILayout.Toggle(!combiMode && !spawnerMode, "Pockets", EditorStyles.miniButton, GUILayout.Height(22)) && (combiMode || spawnerMode))
            pending = () => { combiMode = false; spawnerMode = false; pocketIndex = 0; waveIndex = 0; };
        GUI.backgroundColor = combiMode ? ColAccent : ColPanel;
        if (GUILayout.Toggle(combiMode, "Combi", EditorStyles.miniButton, GUILayout.Height(22)) && !combiMode)
            pending = () => { combiMode = true; spawnerMode = false; pocketIndex = 0; waveIndex = 0; };
        GUI.backgroundColor = spawnerMode ? ColAccent : ColPanel;
        if (GUILayout.Toggle(spawnerMode, "Spawn", EditorStyles.miniButton, GUILayout.Height(22)) && !spawnerMode)
            pending = () => { spawnerMode = true; combiMode = false; spawnerIndex = 0; dayIndex = 0; };
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (spawnerMode) { DrawSpawnerList(em); EditorGUILayout.EndVertical(); return; }

        var list = CurrentPockets(em);
        listScroll = EditorGUILayout.BeginScrollView(listScroll, "box");
        for (int c = 0; c < list.Count; c++)
        {
            var p = list[c];
            EditorGUILayout.BeginHorizontal();
            // enablement toggle — boss is always on (required to advance the era)
            EditorGUI.BeginDisabledGroup(p.isBoss);
            bool en = EditorGUILayout.Toggle(p.isBoss || p.enabled, GUILayout.Width(16));
            if (!p.isBoss && en != p.enabled) { p.enabled = en; MarkDirty(); }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = c == pocketIndex ? ColSel : ColPanel;
            string tag = p.isBoss ? "☠ " : (p.hasCore ? "✦ " : (combiMode ? "⊕ " : "• "));
            string label = (!p.isBoss && !p.enabled) ? tag + p.name + " (off)" : tag + p.name;
            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(22)))
            { int ci = c; pending = () => { pocketIndex = ci; waveIndex = 0; }; }
            GUI.backgroundColor = Color.white;
            if (!p.isBoss && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
            {
                int ci = c;
                if (EditorUtility.DisplayDialog("Delete pocket", $"Delete '{p.name}'?", "Delete", "Cancel"))
                    pending = () => { list.RemoveAt(ci); pocketIndex = 0; waveIndex = 0; MarkDirty(); };
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button(combiMode ? "+ New Combi-Pocket" : "+ New Pocket", GUILayout.Height(24)))
            pending = () =>
            {
                int sz = combiMode ? 11 : 20;   // combi-pockets 11×11, main pockets 20×20
                var np = new PocketTemplate { name = (combiMode ? "Combi " : "Pocket ") + list.Count, width = sz, height = sz };
                FillTiles(np);                  // start solid — carve the open pocket out of it
                list.Add(np); pocketIndex = list.Count - 1; waveIndex = 0; MarkDirty();
            };
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();
    }

    // ---- spawner list column --------------------------------------------------------------
    void DrawSpawnerList(EraMine em)
    {
        if (em.spawners == null) em.spawners = new List<MineSpawnerTemplate>();
        var list = em.spawners;
        listScroll = EditorGUILayout.BeginScrollView(listScroll, "box");
        for (int c = 0; c < list.Count; c++)
        {
            var s = list[c];
            EditorGUILayout.BeginHorizontal();
            bool en = EditorGUILayout.Toggle(s.enabled, GUILayout.Width(16));
            if (en != s.enabled) { s.enabled = en; MarkDirty(); }
            GUI.backgroundColor = c == spawnerIndex ? ColSel : ColPanel;
            string label = "◈ " + s.name + (s.enabled ? "" : " (off)");
            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(22)))
            { int ci = c; pending = () => { spawnerIndex = ci; dayIndex = 0; }; }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
            {
                int ci = c;
                if (EditorUtility.DisplayDialog("Delete spawner", $"Delete '{s.name}'?", "Delete", "Cancel"))
                    pending = () => { list.RemoveAt(ci); spawnerIndex = 0; dayIndex = 0; MarkDirty(); };
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button("+ New Spawner", GUILayout.Height(24)))
            pending = () =>
            {
                var ns = new MineSpawnerTemplate { name = "Spawner " + list.Count };
                for (int d = 1; d <= GS.daysforeraComplete[era]; d++) ns.days.Add(new SpawnerDay { day = d });
                list.Add(ns); spawnerIndex = list.Count - 1; dayIndex = 0; MarkDirty();
            };
        GUI.backgroundColor = Color.white;
    }

    // ---- pocket editor column -------------------------------------------------------------
    void DrawPocketEditor()
    {
        var em = data.GetEra(era);
        if (spawnerMode) { DrawSpawnerEditor(em); return; }
        var list = CurrentPockets(em);
        if (list.Count == 0) { GUILayout.Label(combiMode ? "No combi-pockets — add one." : "No pockets — add one.", sub); return; }
        pocketIndex = Mathf.Clamp(pocketIndex, 0, list.Count - 1);
        var p = list[pocketIndex];
        if (p.waves.Count == 0) p.waves.Add(new PocketWave());     // always at least one wave to place into
        waveIndex = Mathf.Clamp(waveIndex, 0, p.waves.Count - 1);
        var wv = p.waves[waveIndex];

        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);

        // header row
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Name", GUILayout.Width(40));
        string nn = EditorGUILayout.TextField(p.name, GUILayout.Width(180));
        if (nn != p.name) { p.name = nn; MarkDirty(); }
        GUILayout.Space(8);
        if (p.isBoss) GUILayout.Label("☠ BOSS pocket (one per era, has core)", EditorStyles.miniBoldLabel);
        else if (p.hasCore) GUILayout.Label("✦ Ember core pocket (confined wave arena)", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        DrawScoreChip(GUILayoutUtility.GetRect(140, 20, GUILayout.Width(140)), MineAuthoringSO.PocketPoints(p), p.costOverridden);
        EditorGUILayout.EndHorizontal();

        // size + weight
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Size (cells)", GUILayout.Width(70));
        int nw = Mathf.Max(2, EditorGUILayout.IntField(p.width, GUILayout.Width(46)));
        int nh = Mathf.Max(2, EditorGUILayout.IntField(p.height, GUILayout.Width(46)));
        if (nw != p.width || nh != p.height)
        {
            p.width = nw; p.height = nh;
            p.tiles.RemoveAll(t => t.cell.x >= nw || t.cell.y >= nh);
            PrunePlacementsToSpace(p);
            MarkDirty();
        }
        GUILayout.Space(14);
        GUILayout.Label("Weight min/max", GUILayout.Width(96));
        float wmin = EditorGUILayout.FloatField(p.weightRange.x, GUILayout.Width(46));
        float wmax = EditorGUILayout.FloatField(p.weightRange.y, GUILayout.Width(46));
        if (!Mathf.Approximately(wmin, p.weightRange.x) || !Mathf.Approximately(wmax, p.weightRange.y))
        { p.weightRange = new Vector2(wmin, wmax); MarkDirty(); }
        GUILayout.Space(14);
        if (GUILayout.Button("Clear tiles", EditorStyles.miniButton, GUILayout.Width(74))) { p.tiles.Clear(); MarkDirty(); }
        if (GUILayout.Button("Reset tiles", EditorStyles.miniButton, GUILayout.Width(74))) { FillTiles(p); MarkDirty(); }
        EditorGUILayout.EndHorizontal();

        DrawOverrideRow(p, MineAuthoringSO.PocketAutoPoints(p));

        EditorGUILayout.Space(4);
        DrawWaveBar(p);
        EditorGUILayout.Space(2);
        GUILayout.Label(Cat == BrushCategory.Tiles
            ? "TILES: left-drag paints SOLID obstacle walls, right-drag clears. Things spawn in the OPEN cells."
            : brushMode == BrushMode.Combi
                ? "COMBI: left-click a perimeter cell to mark a 5-wide attach edge (arrow = grow direction); right-click removes it."
                : Cat == BrushCategory.Enemies
                    ? $"ENEMIES → WAVE {waveIndex + 1}: left-click places, right-click deletes (enemies only); drag to move."
                    : "EXTRAS → the POCKET (objects/Core, spawn on discovery): left-click places, right-click deletes (extras only); drag to move.",
            EditorStyles.miniLabel);
        if (combiMode)
            GUILayout.Label("This COMBI-POCKET connects via its TOP edge (▲ = the combo's inlet). Author it growing DOWNWARD; it's auto-rotated to face whichever host edge it attaches to (base → combi → combi-combi).", EditorStyles.miniLabel);
        DrawPocketCanvas(p, wv);
        EditorGUILayout.Space(6);
        DrawWavePlacedList(wv);
        EditorGUILayout.Space(4);
        DrawPocketExtrasList(p);

        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawOverrideRow(PocketTemplate p, float auto)
    {
        bool was = p.costOverridden;
        EditorGUILayout.BeginHorizontal();
        bool nb = EditorGUILayout.ToggleLeft("Override points", p.costOverridden, GUILayout.Width(120));
        if (nb != p.costOverridden) { p.costOverridden = nb; if (p.costOverridden) p.costOverride = auto; MarkDirty(); }
        if (was)
        {
            float nv = EditorGUILayout.FloatField(p.costOverride, GUILayout.Width(70));
            if (!Mathf.Approximately(nv, p.costOverride)) { p.costOverride = Mathf.Max(0f, nv); MarkDirty(); }
            GUILayout.Label($"(auto would be {(int)auto})", EditorStyles.miniLabel);
        }
        else GUILayout.Label($"auto = {(int)auto} pts (sum of every wave's enemy prices)", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // Same override idiom for spawners — their cost against the era's SPAWNER budget (avg pts per armed day).
    void DrawSpawnerOverrideRow(MineSpawnerTemplate sp, float auto)
    {
        bool was = sp.costOverridden;
        EditorGUILayout.BeginHorizontal();
        bool nb = EditorGUILayout.ToggleLeft("Override points", sp.costOverridden, GUILayout.Width(120));
        if (nb != sp.costOverridden) { sp.costOverridden = nb; if (sp.costOverridden) sp.costOverride = auto; MarkDirty(); }
        if (was)
        {
            float nv = EditorGUILayout.FloatField(sp.costOverride, GUILayout.Width(70));
            if (!Mathf.Approximately(nv, sp.costOverride)) { sp.costOverride = Mathf.Max(0f, nv); MarkDirty(); }
            GUILayout.Label($"(auto would be {(int)auto})", EditorStyles.miniLabel);
        }
        else GUILayout.Label($"auto = {(int)auto} pts (average enemy points per ARMED day)", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ---- wave selector + timing -----------------------------------------------------------
    void DrawWaveBar(PocketTemplate p)
    {
        GUILayout.Label("WAVES  (each wave has its own enemies; later waves can reuse the same spots)", sub);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < p.waves.Count; i++)
        {
            bool on = i == waveIndex;
            GUI.backgroundColor = on ? ColAccent : ColPanel;
            int n = p.waves[i].placed != null ? p.waves[i].placed.Count : 0;
            if (GUILayout.Toggle(on, $"Wave {i + 1} ({n})", EditorStyles.miniButton, GUILayout.Height(22), GUILayout.Width(86)) && !on)
            { int ii = i; pending = () => { waveIndex = ii; dragIdx = -1; }; }
        }
        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button("+ Wave", EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(22)))
            pending = () => { p.waves.Add(new PocketWave()); waveIndex = p.waves.Count - 1; dragIdx = -1; MarkDirty(); };
        GUI.backgroundColor = Color.white;
        if (p.waves.Count > 1 && GUILayout.Button("Delete wave", EditorStyles.miniButton, GUILayout.Width(86), GUILayout.Height(22)))
        { int wi = waveIndex; pending = () => { p.waves.RemoveAt(wi); waveIndex = 0; dragIdx = -1; MarkDirty(); }; }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        var wv = p.waves[waveIndex];
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        GUILayout.Label("Spawn dur (s)", GUILayout.Width(82));
        wv.spawnDuration = EditorGUILayout.FloatField(wv.spawnDuration, GUILayout.Width(48));
        GUILayout.Space(8);
        GUILayout.Label("Delay after (s)", GUILayout.Width(86));
        wv.delayAfter = EditorGUILayout.FloatField(wv.delayAfter, GUILayout.Width(48));
        GUILayout.Space(8);
        wv.waitForClear = EditorGUILayout.ToggleLeft(new GUIContent("Wait for clear", "Only start the next wave once this one is fully dead."), wv.waitForClear, GUILayout.Width(120));
        GUILayout.Space(8);
        wv.randomOrder = EditorGUILayout.ToggleLeft(new GUIContent("Random order", "Spawn this wave's placements in a random order rather than list order."), wv.randomOrder, GUILayout.Width(120));
        if (EditorGUI.EndChangeCheck()) MarkDirty();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    // ---- the placement canvas (paint space + combi-tiles + free-form drag enemies/extras/core) ----
    void DrawPocketCanvas(PocketTemplate p, PocketWave wv)
    {
        int cols = Mathf.Max(1, p.width);
        int rows = Mathf.Max(1, p.height);
        float cellPx = Mathf.Clamp(560f / Mathf.Max(cols, rows), 10f, 34f);
        var area = GUILayoutUtility.GetRect(cols * cellPx, rows * cellPx, GUILayout.ExpandWidth(false));

        EditorGUI.DrawRect(area, ColVoid);

        // open cavity (checkerboard, where enemies go) vs painted walls (shaded by hardness) and status tiles
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var tt = p.TileAt(new Vector2Int(x, y));
                var cr = new Rect(area.x + x * cellPx, area.y + (rows - 1 - y) * cellPx, cellPx - 1, cellPx - 1);
                EditorGUI.DrawRect(cr, TileColor(tt, (x + y) % 2 == 0));
            }

        // combi-tiles: a 5-wide attach edge + an arrow showing the outward direction the combo grows
        if (p.combiTiles != null)
            foreach (var ct in p.combiTiles)
            {
                Vector2Int dir = CombiOutward(ct, cols, rows);
                Vector2Int perp = new Vector2Int(-dir.y, dir.x);
                for (int s = -2; s <= 2; s++)
                {
                    int cx = ct.x + perp.x * s, cy = ct.y + perp.y * s;
                    if (cx < 0 || cx >= cols || cy < 0 || cy >= rows) continue;
                    var cr = new Rect(area.x + cx * cellPx, area.y + (rows - 1 - cy) * cellPx, cellPx - 1, cellPx - 1);
                    EditorGUI.DrawRect(cr, ColCombi);
                }
                Vector2 ap = MarkerScreen(area, rows, cellPx, new Vector2(ct.x + 0.5f + dir.x, ct.y + 0.5f + dir.y));
                GUI.Label(new Rect(ap.x - cellPx, ap.y - cellPx * 0.6f, cellPx * 2f, cellPx * 1.2f), Glyph(dir), combiArrow);
            }

        // a combi-pocket connects via its TOP edge (it gets rotated to face its host, then grows down):
        // mark that inlet with up-arrows along the top-centre 5 cells.
        if (combiMode)
        {
            int mid = cols / 2;
            for (int s = -2; s <= 2; s++)
            {
                int cx = mid + s;
                if (cx < 0 || cx >= cols) continue;
                GUI.Label(new Rect(area.x + cx * cellPx, area.y, cellPx - 1, cellPx - 1), "▲", combiArrow);
            }
        }

        float mk = Mathf.Clamp(cellPx * 0.95f, 12f, 28f);

        // per-pocket extras (objects)
        if (p.extras != null)
            foreach (var po in p.extras)
            {
                Vector2 cp = MarkerScreen(area, rows, cellPx, po.pos);
                float es = MarkerPx(po, cellPx, mk);
                var rr = new Rect(cp.x - es / 2, cp.y - es / 2, es, es);
                EditorGUI.DrawRect(rr, ColExtra);
                DrawIcon(Inset(rr, 1), GetSpriteOf(po), null);
                if (po.SpawnChance < 0.999f)
                    GUI.Label(new Rect(rr.x - 6, rr.yMax - 5, rr.width + 12, 11), Mathf.RoundToInt(po.SpawnChance * 100f) + "%", tally);
            }

        // per-pocket core
        if (p.core.present)
        {
            Vector2 cp = MarkerScreen(area, rows, cellPx, p.core.pos);
            var rr = new Rect(cp.x - 10, cp.y - 10, 20, 20);
            EditorGUI.DrawRect(rr, ColCore);
            GUI.Label(rr, "✦", tally);
        }

        // active wave's enemies
        if (wv.placed != null)
            for (int i = 0; i < wv.placed.Count; i++)
            {
                var po = wv.placed[i];
                Vector2 cp = MarkerScreen(area, rows, cellPx, po.pos);
                float es = MarkerPx(po, cellPx, mk);
                var rr = new Rect(cp.x - es / 2, cp.y - es / 2, es, es);
                EditorGUI.DrawRect(rr, new Color(0f, 0f, 0f, 0.35f));
                DrawIcon(Inset(rr, 1), GetSpriteOf(po), null);
                GUI.Label(new Rect(rr.x, rr.y - 2, es, 10), (i + 1).ToString(), orderLabel);
            }

        // Tiles brush ghost: outline the exact n×n footprint under the cursor (plus its symmetry mirrors).
        if (Event.current.type == EventType.Repaint && Cat == BrushCategory.Tiles && area.Contains(Event.current.mousePosition))
        {
            Vector2 bc = MouseCell(area, cols, rows, cellPx, Event.current.mousePosition);
            int n = Mathf.Max(1, tileBrushSize);
            Vector2Int o = BrushOrigin(bc, n);
            DrawBrushGhost(area, cols, rows, cellPx, o.x, o.y, n);
            if (symX) DrawBrushGhost(area, cols, rows, cellPx, cols - o.x - n, o.y, n);
            if (symY) DrawBrushGhost(area, cols, rows, cellPx, o.x, rows - o.y - n, n);
            if (symX && symY) DrawBrushGhost(area, cols, rows, cellPx, cols - o.x - n, rows - o.y - n, n);
        }

        // Hover tooltip: name the obj / enemy / core / tile under the cursor.
        if (Event.current.type == EventType.Repaint && area.Contains(Event.current.mousePosition))
        {
            Vector2 hm = Event.current.mousePosition;
            int he = HitMarker(p.extras, area, rows, cellPx, mk, hm);
            int hw = wv != null ? HitMarker(wv.placed, area, rows, cellPx, mk, hm) : -1;
            if (he >= 0) hoverTip = NameOf(p.extras[he]);
            else if (hw >= 0) hoverTip = NameOf(wv.placed[hw]);
            else if (p.core.present && HitCore(p, area, rows, cellPx, hm)) hoverTip = "Core";
            else
            {
                Vector2 hc = MouseCell(area, cols, rows, cellPx, hm);
                { var tt = p.TileAt(new Vector2Int(Mathf.FloorToInt(hc.x), Mathf.FloorToInt(hc.y))); if (tt != PocketTileType.Empty) hoverTip = tt.ToString(); }
            }
        }

        HandleCanvasInput(p, wv, area, cols, rows, cellPx, mk);
    }

    // Category-scoped routing — left-click places, right-click deletes, both ONLY within the selected
    // category: Tiles -> tiles; Enemies -> the active WAVE's enemies; Extras -> the POCKET's
    // objects / core / combi-tiles. You can't delete an enemy while in Extras, or vice-versa.
    void HandleCanvasInput(PocketTemplate p, PocketWave wv, Rect area, int cols, int rows, float cellPx, float mk)
    {
        var e = Event.current;

        if (e.type == EventType.MouseUp) { dragKind = DragKind.None; dragIdx = -1; paintingSpace = false; }
        if (!area.Contains(e.mousePosition)) return;

        Vector2 cell = MouseCell(area, cols, rows, cellPx, e.mousePosition);
        bool erase = e.button == 1;   // right-click deletes — scoped to the selected category

        if (e.type == EventType.MouseDown)
        {
            switch (Cat)
            {
                // TILES: left-drag paints solid tiles, right-drag clears.
                case BrushCategory.Tiles:
                    paintingSpace = true;
                    eraseSpaceDrag = erase;
                    PaintTile(p, cell, erase ? PocketTileType.Empty : brushTile);
                    break;

                // ENEMIES: place / delete / drag ONLY enemy markers in the active wave.
                case BrushCategory.Enemies:
                {
                    int hitE = HitMarker(wv.placed, area, rows, cellPx, mk, e.mousePosition);
                    if (hitE >= 0)
                    {
                        if (erase) { wv.placed.RemoveAt(hitE); MarkDirty(); }
                        else { dragKind = DragKind.Enemy; dragIdx = hitE; }
                    }
                    else if (!erase && brushEnemy != null && p.InSpace(cell))
                    {
                        Vector2 pos = Place(cell, cols, rows);
                        wv.placed.Add(new PlacedObject { enemy = brushEnemy, pos = pos });
                        dragKind = DragKind.Enemy; dragIdx = wv.placed.Count - 1;
                        foreach (var mp in MirrorPositions(pos, cols, rows))
                            if (p.InSpace(mp)) wv.placed.Add(new PlacedObject { enemy = brushEnemy, pos = mp });
                        MarkDirty();
                    }
                    break;
                }

                // EXTRAS: place / delete / drag ONLY the pocket's objects, core and combi-tiles.
                case BrushCategory.Extras:
                {
                    if (brushMode == BrushMode.Combi)
                    {
                        if (erase) HitCombiTile(p, cell);   // remove the combi tile under the cursor
                        else ToggleCombiTile(p, cell);
                        break;
                    }
                    int hitX = HitMarker(p.extras, area, rows, cellPx, mk, e.mousePosition);
                    if (hitX >= 0)
                    {
                        if (erase) { p.extras.RemoveAt(hitX); MarkDirty(); }
                        else { dragKind = DragKind.Extra; dragIdx = hitX; }
                        break;
                    }
                    if (p.core.present && HitCore(p, area, rows, cellPx, e.mousePosition))
                    {
                        if (erase) { p.core.present = false; MarkDirty(); }
                        else dragKind = DragKind.Core;
                        break;
                    }
                    if (erase) { HitCombiTile(p, cell); break; }   // right-click also clears a combi tile
                    if (p.InSpace(cell))   // place the active extras sub-brush in open space
                    {
                        Vector2 pos = Place(cell, cols, rows);
                        if (brushMode == BrushMode.Object && brushObject != null)
                        {
                            p.extras.Add(new PlacedObject { prefab = brushObject, pos = pos, chance = brushChance });
                            dragKind = DragKind.Extra; dragIdx = p.extras.Count - 1;
                            foreach (var mp in MirrorPositions(pos, cols, rows))
                                if (p.InSpace(mp)) p.extras.Add(new PlacedObject { prefab = brushObject, pos = mp, chance = brushChance });
                            MarkDirty();
                        }
                        else if (brushMode == BrushMode.Core)
                        { p.core = new CorePlacement { present = true, pos = pos }; MarkDirty(); }
                    }
                    break;
                }
            }
            e.Use(); Repaint(); return;
        }

        if (e.type == EventType.MouseDrag)
        {
            if (paintingSpace && brushMode == BrushMode.Tile)
            { PaintTile(p, cell, eraseSpaceDrag ? PocketTileType.Empty : brushTile); e.Use(); Repaint(); return; }

            if (dragKind != DragKind.None)
            {
                if (p.InSpace(cell))   // don't let you drag a marker outside the space
                {
                    Vector2 pos = Place(cell, cols, rows);
                    if (dragKind == DragKind.Enemy && dragIdx >= 0 && dragIdx < wv.placed.Count)
                    { var po = wv.placed[dragIdx]; po.pos = pos; wv.placed[dragIdx] = po; MarkDirty(); }
                    else if (dragKind == DragKind.Extra && dragIdx >= 0 && dragIdx < p.extras.Count)
                    { var po = p.extras[dragIdx]; po.pos = pos; p.extras[dragIdx] = po; MarkDirty(); }
                    else if (dragKind == DragKind.Core && p.core.present)
                    { p.core.pos = pos; MarkDirty(); }
                }
                e.Use(); Repaint(); return;
            }
        }
    }

    void ToggleCombiTile(PocketTemplate p, Vector2 cell)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, p.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(cell.y), 0, p.height - 1);
        var c = new Vector2Int(x, y);
        if (!p.combiTiles.Remove(c)) p.combiTiles.Add(c);
        MarkDirty();
    }

    bool HitCombiTile(PocketTemplate p, Vector2 cell)
    {
        var c = new Vector2Int(Mathf.FloorToInt(cell.x), Mathf.FloorToInt(cell.y));
        if (p.combiTiles != null && p.combiTiles.Remove(c)) { MarkDirty(); return true; }
        return false;
    }

    // ---- per-wave list (delete / reorder = spawn order) -----------------------------------
    void DrawWavePlacedList(PocketWave wv)
    {
        GUILayout.Label("THIS WAVE  (top = spawns first; trims to budget in this order)", sub);
        if (wv.placed == null || wv.placed.Count == 0)
        { GUILayout.Label("Drag enemies/objects onto the canvas above.", EditorStyles.miniLabel); return; }

        for (int i = 0; i < wv.placed.Count; i++)
        {
            var po = wv.placed[i];
            var row = EditorGUILayout.GetControlRect(false, 22);
            EditorGUI.DrawRect(row, (i % 2 == 0) ? ColCell : ColCellAlt);
            GUI.Label(new Rect(row.x + 4, row.y + 3, 26, 16), (i + 1).ToString(), tally);
            DrawIcon(new Rect(row.x + 28, row.y + 2, 18, 18), GetSpriteOf(po), null);
            string label = po.enemy != null ? po.enemy.name + $"  ({(int)MineAuthoringSO.EnemyPoints(po.enemy)} pts)"
                          : po.prefab != null ? po.prefab.name + "  (object)" : "(empty)";
            GUI.Label(new Rect(row.x + 50, row.y + 3, row.width - 252, 16), label, EditorStyles.miniLabel);

            // editable position — type exact coordinates instead of (or after) dragging. (Enemies have no
            // spawn chance — that's an extras-only concept, edited in the Pocket Extras list.)
            float px = EditorGUI.FloatField(new Rect(row.xMax - 196, row.y + 2, 36, 18), po.pos.x);
            float py = EditorGUI.FloatField(new Rect(row.xMax - 158, row.y + 2, 36, 18), po.pos.y);
            if (px != po.pos.x || py != po.pos.y)
            { po.pos = new Vector2(px, py); wv.placed[i] = po; MarkDirty(); }

            int ii = i;
            if (i > 0 && GUI.Button(new Rect(row.xMax - 116, row.y + 1, 26, 19), "▲", EditorStyles.miniButton))
                pending = () => { (wv.placed[ii - 1], wv.placed[ii]) = (wv.placed[ii], wv.placed[ii - 1]); MarkDirty(); };
            if (i < wv.placed.Count - 1 && GUI.Button(new Rect(row.xMax - 88, row.y + 1, 26, 19), "▼", EditorStyles.miniButton))
                pending = () => { (wv.placed[ii + 1], wv.placed[ii]) = (wv.placed[ii], wv.placed[ii + 1]); MarkDirty(); };
            if (GUI.Button(new Rect(row.xMax - 58, row.y + 1, 52, 19), "Delete", EditorStyles.miniButton))
                pending = () => { wv.placed.RemoveAt(ii); MarkDirty(); };
        }
    }

    // ---- spawner editor -------------------------------------------------------------------
    // A spawner = a PNG look (world size derives from the pixels: 16 px = 1 unit), an activation range
    // from the excavation, and per-DAY wave-grids: ONE grid maps all of a day's waves — each ROW is a
    // wave (top row first); the column never matters. Paint enemies into cells with the Enemies brush.
    void DrawSpawnerEditor(EraMine em)
    {
        if (em.spawners == null || em.spawners.Count == 0)
        { GUILayout.Label("No spawners — add one.", sub); return; }
        spawnerIndex = Mathf.Clamp(spawnerIndex, 0, em.spawners.Count - 1);
        var sp = em.spawners[spawnerIndex];
        if (sp.days == null) sp.days = new List<SpawnerDay>();
        if (sp.days.Count == 0) sp.days.Add(new SpawnerDay { day = 1 });
        dayIndex = Mathf.Clamp(dayIndex, 0, sp.days.Count - 1);
        var day = sp.days[dayIndex];

        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);

        // header row: name + look + range
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Name", GUILayout.Width(40));
        string nn = EditorGUILayout.TextField(sp.name, GUILayout.Width(180));
        if (nn != sp.name) { sp.name = nn; MarkDirty(); }
        GUILayout.Space(10);
        GUILayout.Label("◈ SPAWNER — renews daily at an unpredictable time; spawns into the nearby dig", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        DrawScoreChip(GUILayoutUtility.GetRect(140, 20, GUILayout.Width(140)), MineAuthoringSO.SpawnerPoints(sp), sp.costOverridden);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("Look (png)", "The spawner's body. World size derives from the " +
            "pixels: 16×16 px = 1×1 units, 32×32 = 2×2 …"), GUILayout.Width(66));
        var img = (Texture2D)EditorGUILayout.ObjectField(sp.image, typeof(Texture2D), false, GUILayout.Width(150));
        if (img != sp.image) { sp.image = img; MarkDirty(); }
        if (sp.image != null)
        {
            Vector2 ws = sp.WorldSize;
            GUILayout.Label($"{sp.image.width}×{sp.image.height}px  →  {ws.x:0.##}×{ws.y:0.##} units", EditorStyles.miniLabel, GUILayout.Width(180));
        }
        else GUILayout.Label("(no image — invisible)", EditorStyles.miniLabel, GUILayout.Width(180));
        GUILayout.Space(10);
        GUILayout.Label(new GUIContent("Active range", "Max distance (world units) from the nearest " +
            "excavated cell at which it operates — any further from the dig and it's fully inactive."), GUILayout.Width(80));
        float ar = EditorGUILayout.FloatField(sp.activationRange, GUILayout.Width(46));
        if (!Mathf.Approximately(ar, sp.activationRange)) { sp.activationRange = Mathf.Max(1f, ar); MarkDirty(); }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // points vs the era's spawner budget (MineField.eraNSpawnerPoints) — auto = avg pts per armed day
        DrawSpawnerOverrideRow(sp, MineAuthoringSO.SpawnerAutoPoints(sp));

        // preview thumb
        if (sp.image != null)
        {
            var pr = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64));
            EditorGUI.DrawRect(pr, ColVoid);
            GUI.DrawTexture(Inset(pr, 3), sp.image, ScaleMode.ScaleToFit);
        }

        // day tabs — which era-day this plan runs on (a spawner can carry any number of days)
        EditorGUILayout.Space(4);
        GUILayout.Label("DAYS  (each day has ONE grid: a row = a wave, top row first; columns are just layout space)", sub);
        EditorGUILayout.BeginHorizontal();
        for (int d = 0; d < sp.days.Count; d++)
        {
            bool on = d == dayIndex;
            GUI.backgroundColor = on ? ColAccent : ColPanel;
            int n = sp.days[d].cells != null ? sp.days[d].cells.Count : 0;
            if (GUILayout.Toggle(on, $"Day {sp.days[d].day} ({n})", EditorStyles.miniButton, GUILayout.Height(22), GUILayout.Width(86)) && !on)
            { int dd = d; pending = () => dayIndex = dd; }
        }
        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button("+ Day", EditorStyles.miniButton, GUILayout.Width(56), GUILayout.Height(22)))
            pending = () =>
            {
                int next = 1;
                foreach (var dd in sp.days) next = Mathf.Max(next, dd.day + 1);
                sp.days.Add(new SpawnerDay { day = next });
                dayIndex = sp.days.Count - 1; MarkDirty();
            };
        GUI.backgroundColor = Color.white;
        if (sp.days.Count > 1 && GUILayout.Button("Delete day", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(22)))
        { int di = dayIndex; pending = () => { sp.days.RemoveAt(di); dayIndex = 0; MarkDirty(); }; }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // per-day settings: which day, the author-set grid dimensions, and the wave timing
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        GUILayout.Label(new GUIContent("Day #", "1-based day within the era this plan runs on."), GUILayout.Width(38));
        day.day = Mathf.Max(1, EditorGUILayout.IntField(day.day, GUILayout.Width(36)));
        GUILayout.Space(10);
        GUILayout.Label(new GUIContent("Grid", "rows × cols. One ROW = one WAVE. Size it however you need."), GUILayout.Width(30));
        int nr = Mathf.Clamp(EditorGUILayout.IntField(day.rows, GUILayout.Width(34)), 1, 24);
        int nc = Mathf.Clamp(EditorGUILayout.IntField(day.cols, GUILayout.Width(34)), 1, 24);
        GUILayout.Space(10);
        GUILayout.Label(new GUIContent("Time / wave (s)", "Seconds over which each wave's enemies trickle in."), GUILayout.Width(92));
        day.timePerWave = EditorGUILayout.FloatField(day.timePerWave, GUILayout.Width(44));
        GUILayout.Space(8);
        GUILayout.Label(new GUIContent("Inter-wave wait (s)", "Seconds between one wave and the next."), GUILayout.Width(112));
        day.interWaveWait = EditorGUILayout.FloatField(day.interWaveWait, GUILayout.Width(44));
        if (EditorGUI.EndChangeCheck())
        {
            if (nr != day.rows || nc != day.cols) { day.rows = nr; day.cols = nc; day.PruneToGrid(); }
            MarkDirty();
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        GUILayout.Label("GRID: pick an enemy in the Enemies brush, left-click a cell to place, right-click to clear. " +
                        "A row spawns together as one wave (top row = wave 1); the column changes nothing.", EditorStyles.miniLabel);
        DrawSpawnerGrid(day);

        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // The day's wave-grid canvas: wave labels + per-row point tallies flank the cells.
    void DrawSpawnerGrid(SpawnerDay day)
    {
        int cols = Mathf.Max(1, day.cols);
        int rows = Mathf.Max(1, day.rows);
        float cellPx = Mathf.Clamp(520f / Mathf.Max(cols, rows), 18f, 40f);
        const float LBL = 40f;   // wave-label gutter
        var area = GUILayoutUtility.GetRect(LBL + cols * cellPx + 60f, rows * cellPx, GUILayout.ExpandWidth(false));
        var grid = new Rect(area.x + LBL, area.y, cols * cellPx, rows * cellPx);

        EditorGUI.DrawRect(grid, ColVoid);
        for (int r = 0; r < rows; r++)
        {
            // gutter: which wave this row is
            GUI.Label(new Rect(area.x, area.y + r * cellPx + (cellPx - 16f) * 0.5f, LBL - 6f, 16f), $"W{r + 1}", tally);
            float rowPts = 0f;
            for (int c = 0; c < cols; c++)
            {
                var cr = new Rect(grid.x + c * cellPx, grid.y + r * cellPx, cellPx - 1, cellPx - 1);
                EditorGUI.DrawRect(cr, (r + c) % 2 == 0 ? ColCell : ColCellAlt);
                var so = day.CellAt(r, c);
                if (so != null)
                {
                    rowPts += MineAuthoringSO.EnemyPoints(so);
                    DrawIcon(Inset(cr, 2), GetSprite(so), so.name);
                    Hover(cr, so.name);
                }
            }
            // per-row tally: the wave's total points
            if (rowPts > 0f)
                GUI.Label(new Rect(grid.xMax + 4f, area.y + r * cellPx + (cellPx - 16f) * 0.5f, 54f, 16f),
                          ((int)rowPts) + " pts", tally);
        }

        // input: left-click paints the selected enemy brush, right-click clears — cells only
        var e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && grid.Contains(e.mousePosition))
        {
            int c2 = Mathf.Clamp((int)((e.mousePosition.x - grid.x) / cellPx), 0, cols - 1);
            int r2 = Mathf.Clamp((int)((e.mousePosition.y - grid.y) / cellPx), 0, rows - 1);
            if (e.button == 1) { day.SetCell(r2, c2, null); MarkDirty(); }
            else if (brushMode == BrushMode.Enemy && brushEnemy != null) { day.SetCell(r2, c2, brushEnemy); MarkDirty(); }
            e.Use(); Repaint();
        }
    }

    // ---- tile helpers ---------------------------------------------------------------------
    // Bottom-left cell of an n×n block centred on the CONTINUOUS cursor position — for even sizes the
    // block shifts with which half of the tile the mouse is in (a 2×2 paints the 4 cells around the
    // nearest corner), instead of always anchoring off the hovered tile.
    static Vector2Int BrushOrigin(Vector2 cell, int n)
        => new Vector2Int(Mathf.RoundToInt(cell.x - n * 0.5f), Mathf.RoundToInt(cell.y - n * 0.5f));

    // Paint an n×n block (tileBrushSize) of `type` centred on the cursor (Empty clears them), mirrored by
    // the symmetry toggles. One entry per cell; a new WALL evicts anything standing on the painted cells.
    void PaintTile(PocketTemplate p, Vector2 cell, PocketTileType type)
    {
        int n = Mathf.Max(1, tileBrushSize);
        Vector2Int o = BrushOrigin(cell, n);
        var cells = new HashSet<Vector2Int>();
        for (int dx = 0; dx < n; dx++)
            for (int dy = 0; dy < n; dy++)
            {
                int x = o.x + dx, y = o.y + dy;
                if (x >= 0 && x < p.width && y >= 0 && y < p.height) cells.Add(new Vector2Int(x, y));
                if (symX && p.width - 1 - x >= 0 && p.width - 1 - x < p.width && y >= 0 && y < p.height)
                    cells.Add(new Vector2Int(p.width - 1 - x, y));
                if (symY && x >= 0 && x < p.width && p.height - 1 - y >= 0 && p.height - 1 - y < p.height)
                    cells.Add(new Vector2Int(x, p.height - 1 - y));
                if (symX && symY && p.width - 1 - x >= 0 && p.width - 1 - x < p.width && p.height - 1 - y >= 0 && p.height - 1 - y < p.height)
                    cells.Add(new Vector2Int(p.width - 1 - x, p.height - 1 - y));
            }
        bool changed = false, anyWall = false;
        foreach (var c in cells)
        {
            int removed = p.tiles.RemoveAll(t => t.cell == c);
            if (type != PocketTileType.Empty)
            {
                p.tiles.Add(new PocketTile(c, type));
                changed = true;
                if (PocketTiles.IsWall(type)) anyWall = true;
            }
            else if (removed > 0) changed = true;   // erased back to Empty cavity
        }
        if (anyWall) PrunePlacementsToSpace(p);
        if (changed) MarkDirty();
    }

    // Mirrored copies of a free-form position under the active symmetry toggles (original excluded;
    // degenerate mirrors on the centre line dedupe away).
    List<Vector2> MirrorPositions(Vector2 pos, int cols, int rows)
    {
        var outp = new List<Vector2>(3);
        void Add(Vector2 v)
        {
            if ((v - pos).sqrMagnitude < 1e-4f) return;
            foreach (var q in outp) if ((q - v).sqrMagnitude < 1e-4f) return;
            outp.Add(v);
        }
        if (symX) Add(new Vector2(cols - pos.x, pos.y));
        if (symY) Add(new Vector2(pos.x, rows - pos.y));
        if (symX && symY) Add(new Vector2(cols - pos.x, rows - pos.y));
        return outp;
    }

    // Fill the pocket with solid Wall, then carve a centred empty cavity (up to 10×10) to start from. Used for
    // new pockets and the "Reset tiles" button; doesn't touch placements (re-carve restores their open cells).
    void FillTiles(PocketTemplate p)
    {
        p.tiles.Clear();
        const int cav = 10;
        int cw = Mathf.Min(cav, p.width), ch = Mathf.Min(cav, p.height);
        int x0 = (p.width - cw) / 2, y0 = (p.height - ch) / 2;
        for (int y = 0; y < p.height; y++)
            for (int x = 0; x < p.width; x++)
            {
                bool open = x >= x0 && x < x0 + cw && y >= y0 && y < y0 + ch;   // empty = no tile entry
                if (!open) p.tiles.Add(new PocketTile(new Vector2Int(x, y), PocketTileType.Wall));
            }
    }

    // Drop anything (enemies, extras, core, combi-tiles) that no longer sits inside the painted space.
    void PrunePlacementsToSpace(PocketTemplate p)
    {
        if (p.waves != null)
            foreach (var w in p.waves)
                if (w?.placed != null)
                    w.placed.RemoveAll(po => !p.InSpace(po.pos));
        if (p.extras != null) p.extras.RemoveAll(po => !p.InSpace(po.pos));
        if (p.combiTiles != null) p.combiTiles.RemoveAll(c => !p.InSpace(c));
        if (p.core.present && !p.InSpace(p.core.pos)) p.core.present = false;
    }

    // ---- per-pocket extras list (objects spawned instantly on discovery) ------------------
    void DrawPocketExtrasList(PocketTemplate p)
    {
        int combi = p.combiTiles != null ? p.combiTiles.Count : 0;
        GUILayout.Label($"POCKET EXTRAS  (spawn instantly on discovery)   ·   combi-tiles: {combi}", sub);
        if (p.extras == null || p.extras.Count == 0)
        { GUILayout.Label("Object brush → drop props onto the canvas. Combi brush → mark attach cells.", EditorStyles.miniLabel); return; }

        for (int i = 0; i < p.extras.Count; i++)
        {
            var po = p.extras[i];
            var row = EditorGUILayout.GetControlRect(false, 22);
            EditorGUI.DrawRect(row, (i % 2 == 0) ? ColCell : ColCellAlt);
            DrawIcon(new Rect(row.x + 6, row.y + 2, 18, 18), GetSpriteOf(po), null);
            GUI.Label(new Rect(row.x + 28, row.y + 3, row.width - 248, 16), po.prefab != null ? po.prefab.name : "(empty)", EditorStyles.miniLabel);
            GUI.Label(new Rect(row.xMax - 190, row.y + 3, 16, 16), "ch", EditorStyles.miniLabel);
            float ec = EditorGUI.FloatField(new Rect(row.xMax - 174, row.y + 2, 32, 18), po.SpawnChance);
            float ex = EditorGUI.FloatField(new Rect(row.xMax - 138, row.y + 2, 36, 18), po.pos.x);
            float ey = EditorGUI.FloatField(new Rect(row.xMax - 100, row.y + 2, 36, 18), po.pos.y);
            if (ex != po.pos.x || ey != po.pos.y || !Mathf.Approximately(ec, po.SpawnChance))
            { po.pos = new Vector2(ex, ey); po.chance = Mathf.Clamp01(ec); p.extras[i] = po; MarkDirty(); }
            int ii = i;
            if (GUI.Button(new Rect(row.xMax - 58, row.y + 1, 52, 19), "Delete", EditorStyles.miniButton))
                pending = () => { p.extras.RemoveAt(ii); MarkDirty(); };
        }
    }

    // ---- canvas geometry ------------------------------------------------------------------
    static Vector2 MarkerScreen(Rect area, int rows, float cellPx, Vector2 cellPos)
        => new Vector2(area.x + cellPos.x * cellPx, area.y + (rows - cellPos.y) * cellPx);

    static Vector2 MouseCell(Rect area, int cols, int rows, float cellPx, Vector2 m)
        => new Vector2((m.x - area.x) / cellPx, rows - (m.y - area.y) / cellPx);

    static Vector2 ClampToCanvas(Vector2 cell, int cols, int rows)
        => new Vector2(Mathf.Clamp(cell.x, 0.05f, cols - 0.05f), Mathf.Clamp(cell.y, 0.05f, rows - 0.05f));

    // Where a free-form enemy/object actually lands: snapped to the nearest 0.5-cell grid point (when
    // snapToGrid is on), then clamped into the canvas.
    Vector2 Place(Vector2 cell, int cols, int rows)
    {
        if (snapToGrid) cell = new Vector2(Mathf.Round(cell.x * 2f) * 0.5f, Mathf.Round(cell.y * 2f) * 0.5f);
        return ClampToCanvas(cell, cols, rows);
    }

    // Translucent highlight over an n×n block of cells (clipped to the canvas) — the Tiles brush preview.
    static void DrawBrushGhost(Rect area, int cols, int rows, float cellPx, int x0, int y0, int n)
    {
        int xa = Mathf.Max(0, x0), xb = Mathf.Min(cols, x0 + n);
        int ya = Mathf.Max(0, y0), yb = Mathf.Min(rows, y0 + n);
        if (xa >= xb || ya >= yb) return;
        var r = new Rect(area.x + xa * cellPx, area.y + (rows - yb) * cellPx,
                         (xb - xa) * cellPx - 1, (yb - ya) * cellPx - 1);
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.16f));
    }

    int HitMarker(List<PlacedObject> list, Rect area, int rows, float cellPx, float mk, Vector2 m)
    {
        if (list == null) return -1;
        for (int i = list.Count - 1; i >= 0; i--)   // top-most first
        {
            Vector2 cp = MarkerScreen(area, rows, cellPx, list[i].pos);
            float r = MarkerPx(list[i], cellPx, mk) * 0.6f;   // hit area tracks the drawn (scaled) size
            if ((m - cp).sqrMagnitude <= r * r) return i;
        }
        return -1;
    }

    bool HitCore(PocketTemplate p, Rect area, int rows, float cellPx, Vector2 m)
    {
        Vector2 cp = MarkerScreen(area, rows, cellPx, p.core.pos);
        return (m - cp).sqrMagnitude <= 11f * 11f;
    }

    // ---- shared helpers -------------------------------------------------------------------
    void DrawScoreChip(Rect r, float pts, bool overridden)
    {
        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.14f));
        var c = Color.Lerp(new Color(0.45f, 0.85f, 0.45f), new Color(0.95f, 0.35f, 0.3f), Mathf.Clamp01(pts / 80f));
        var s = new GUIStyle(tally) { alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
        GUI.Label(r, (overridden ? "✎ " : "★ ") + (int)pts + " pts", s);
    }

    Sprite GetSpriteOf(PlacedObject po)
        => po.enemy != null ? GetSprite(po.enemy) : (po.prefab != null ? GetSprite(po.prefab) : null);

    Sprite GetSprite(MarauderSO so) => so != null ? GetSprite(so.prefab, so) : null;
    Sprite GetSprite(GameObject go) => GetSprite(go, go);

    Sprite GetSprite(GameObject prefab, Object key)
    {
        if (key == null) return null;
        if (spriteCache.TryGetValue(key, out var s)) return s;
        var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
        s = sr ? sr.sprite : null;
        spriteCache[key] = s;
        return s;
    }

    // ---- true-to-scale marker sizing ------------------------------------------------------
    // Draw each placed marker at the size its prefab actually occupies in the pocket — its sprite's world
    // width mapped through the mine cell size — so big enemies read big and small ones small. Clamped so a
    // giant doesn't swamp the canvas and a sprite-less prefab (e.g. a runtime-baked tile) keeps the base size.
    const float MineCellWorld = 0.5f; // MineField.cellSize — the canvas is authored in these cells
    readonly Dictionary<Object, float> sizeCache = new Dictionary<Object, float>();

    // On-canvas pixel size for a placed marker (enemy or object).
    float MarkerPx(PlacedObject po, float cellPx, float mk)
    {
        float world = po.enemy != null ? WorldSize(po.enemy.prefab, po.enemy)
                    : po.prefab != null ? WorldSize(po.prefab, po.prefab) : 0f;
        if (world <= 0f) return mk;                              // unknown size -> base marker
        return Mathf.Clamp(world / MineCellWorld * cellPx, mk * 0.5f, cellPx * 4f);
    }

    // World-space width of a prefab's first sprite, baked through its local scale chain (lossyScale is
    // unreliable on prefab assets, so multiply localScales up to the root by hand). Cached per key.
    float WorldSize(GameObject prefab, Object key)
    {
        if (key == null || prefab == null) return 0f;
        if (sizeCache.TryGetValue(key, out var w)) return w;
        var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
        w = 0f;
        if (sr != null && sr.sprite != null)
        {
            float scale = 1f;
            for (var tr = sr.transform; tr != null; tr = tr.parent)
            {
                scale *= tr.localScale.x;
                if (tr == prefab.transform) break;
            }
            w = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y) * Mathf.Abs(scale);
        }
        sizeCache[key] = w;
        return w;
    }

    void DrawIcon(Rect r, Sprite spr, string fallback)
    {
        if (spr != null && spr.texture != null)
        {
            var t = spr.texture;
            var tc = new Rect(spr.rect.x / t.width, spr.rect.y / t.height, spr.rect.width / t.width, spr.rect.height / t.height);
            GUI.DrawTextureWithTexCoords(r, t, tc, true);
            return;
        }
        if (!string.IsNullOrEmpty(fallback)) GUI.Label(r, fallback, gridCellName);
    }

    static Rect Inset(Rect r, float p) => new Rect(r.x + p, r.y + p, r.width - 2 * p, r.height - 2 * p);

    // ---- hover tooltips -------------------------------------------------------------------
    // Record the name under the cursor while drawing (Repaint); DrawHoverTip paints it last, on top.
    void Hover(Rect r, string name)
    {
        if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(name) && r.Contains(Event.current.mousePosition))
            hoverTip = name;
    }

    static string NameOf(PlacedObject po)
        => po.enemy != null ? po.enemy.name : (po.prefab != null ? po.prefab.name : null);

    void DrawHoverTip()
    {
        if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(hoverTip)) return;
        if (hoverStyle == null)
            hoverStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, padding = new RectOffset(6, 6, 3, 3), normal = { textColor = Color.white } };
        var content = new GUIContent(hoverTip);
        Vector2 sz = hoverStyle.CalcSize(content);
        Vector2 m = Event.current.mousePosition;
        var box = new Rect(m.x + 16, m.y + 14, sz.x, sz.y);
        if (box.xMax > position.width) box.x = Mathf.Max(2f, position.width - box.width - 2f);
        if (box.yMax > position.height) box.y = m.y - box.height - 6f;
        EditorGUI.DrawRect(box, new Color(0.06f, 0.06f, 0.08f, 0.96f));
        EditorGUI.DrawRect(new Rect(box.x, box.y, box.width, 1f), new Color(1f, 1f, 1f, 0.18f));
        GUI.Label(box, content, hoverStyle);
    }

    void EnsureStyles()
    {
        if (stylesReady) return;
        stylesReady = true;
        hdr = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = ColAccent } };
        sub = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
        tally = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        gridCellName = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 7, wordWrap = true, normal = { textColor = Color.white } };
        orderLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperLeft, fontSize = 8, normal = { textColor = new Color(1f, 0.95f, 0.6f) } };
        combiArrow = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = new Color(0.6f, 0.92f, 1f) } };
    }
}
