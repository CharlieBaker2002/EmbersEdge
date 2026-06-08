using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wave Forge — visual authoring tool for WaveAuthoringSO.
/// Tools > Wave Forge.  Dungeon tabs -> Collections (Main + N) -> Days (subwaves w/ timed grids)
/// and Clusters (collection-wide budget fillers).  Pick a brush from the palette and paint enemies
/// onto a grid (column = position along the rim).  Scores are live suggestions you can override.
/// Auto-saves to Resources/WaveAuthoring.asset.
///
/// IMGUI note: every click that changes which item is shown (tab / collection / open day / subwave /
/// cluster) or adds/removes a list item is deferred to the end of OnGUI via `pending`, and in-row
/// conditionals capture their pre-click value — otherwise the control count would differ between the
/// Layout and event passes and Unity logs "GUILayout mismatch".
/// </summary>
public class WaveForgeWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/WaveAuthoring.asset";
    const float Cell = 30f;

    WaveAuthoringSO data;

    int dungeon;          // 0/1/2
    int collIndex;        // selected collection within dungeon
    int openDay = -1;     // expanded day (-1 = none)
    int openSub = 0;      // expanded subwave within the open day
    int openCluster = -1; // expanded cluster

    MarauderSO brush;     // active palette enemy
    bool eraser;

    bool settingsFold;
    Vector2 collScroll, editScroll;

    bool dirty;
    System.Action pending; // deferred nav / structural edits, applied at end of OnGUI
    readonly Dictionary<MarauderSO, Sprite> spriteCache = new Dictionary<MarauderSO, Sprite>();

    static readonly Color ColBg      = new Color(0.16f, 0.16f, 0.19f);
    static readonly Color ColPanel   = new Color(0.21f, 0.21f, 0.25f);
    static readonly Color ColAccent  = new Color(0.95f, 0.55f, 0.22f);
    static readonly Color ColSel     = new Color(0.30f, 0.55f, 0.95f);
    static readonly Color ColCore    = new Color(0.95f, 0.55f, 0.22f, 0.22f);
    static readonly Color ColCell    = new Color(0.27f, 0.27f, 0.32f);
    static readonly Color ColCellAlt = new Color(0.24f, 0.24f, 0.29f);

    GUIStyle hdr, sub, tally, gridCellName, expLabel;
    bool stylesReady;

    [MenuItem("Tools/Wave Forge")]
    static void Open()
    {
        var w = GetWindow<WaveForgeWindow>("Wave Forge");
        w.minSize = new Vector2(780, 540);
        w.Show();
    }

    void OnEnable() => data = AssetDatabase.LoadAssetAtPath<WaveAuthoringSO>(AssetPath);

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
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ColBg);

        if (data == null) { DrawNoAsset(); return; }

        DrawHeader();
        DrawDungeonTabs();
        DrawPaletteBar();

        EditorGUILayout.BeginHorizontal();
        DrawCollectionsColumn();
        DrawEditorColumn();
        EditorGUILayout.EndHorizontal();

        // Apply deferred nav / structural change after the frame's layout is fully built.
        if (pending != null) { var p = pending; pending = null; p(); Repaint(); }
    }

    void DrawNoAsset()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical(GUILayout.Width(420));
        GUILayout.Label("Wave Forge", hdr);
        EditorGUILayout.HelpBox("No WaveAuthoring asset found at\n" + AssetPath, MessageType.Info);
        if (GUILayout.Button("Create WaveAuthoring asset", GUILayout.Height(34)))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var so = CreateInstance<WaveAuthoringSO>();
            for (int d = 0; d < 3; d++) so.GetDungeon(d); // seed Main collections
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
        GUI.Label(new Rect(r.x + 8, r.y + 5, r.width - 220, 20), "⚔  WAVE FORGE", hdr);
        bool foldNow = settingsFold; // use this frame's value for the conditional panel
        settingsFold = GUI.Toggle(new Rect(r.xMax - 200, r.y + 5, 120, 20), settingsFold, "Settings / Palette", EditorStyles.miniButton);
        GUI.Label(new Rect(r.xMax - 70, r.y + 5, 64, 20), dirty ? "saving…" : "saved", EditorStyles.miniLabel);
        if (foldNow) DrawSettings();
    }

    void DrawSettings()
    {
        EditorGUILayout.BeginVertical("box");
        var so = new SerializedObject(data);
        so.Update();
        EditorGUILayout.LabelField("Grid / timing", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("gridCols"));
        EditorGUILayout.PropertyField(so.FindProperty("gridRows"));
        EditorGUILayout.PropertyField(so.FindProperty("unitsPerColumn"),
            new GUIContent("Units / Column", "World-unit gap along the rim between adjacent grid columns. " +
                "Fixed world units, so the formation width no longer grows when the map scales up."));
        EditorGUILayout.PropertyField(so.FindProperty("budgetPerExtraCore"));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Enemy palettes (per dungeon)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("dungeon1Enemies"), true);
        EditorGUILayout.PropertyField(so.FindProperty("dungeon2Enemies"), true);
        EditorGUILayout.PropertyField(so.FindProperty("dungeon3Enemies"), true);
        if (so.ApplyModifiedProperties()) MarkDirty();

        if (GUILayout.Button("Import palettes from SpawnManager in open scene")) ImportPalettes();
        EditorGUILayout.EndVertical();
    }

    void ImportPalettes()
    {
        var sm = Object.FindObjectOfType<SpawnManager>();
        if (sm == null) { ShowNotification(new GUIContent("No SpawnManager in the open scene")); return; }
        data.dungeon1Enemies = sm.marauders != null ? sm.marauders.ToArray() : new MarauderSO[0];
        data.dungeon2Enemies = sm.E2SOs != null ? (MarauderSO[])sm.E2SOs.Clone() : new MarauderSO[0];
        data.dungeon3Enemies = sm.E3SOs != null ? (MarauderSO[])sm.E3SOs.Clone() : new MarauderSO[0];
        MarkDirty();
        ShowNotification(new GUIContent("Imported enemy palettes"));
    }

    // ---- dungeon tabs ---------------------------------------------------------------------
    void DrawDungeonTabs()
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        for (int d = 0; d < 3; d++)
        {
            bool on = dungeon == d;
            GUI.backgroundColor = on ? ColAccent : ColPanel;
            if (GUILayout.Toggle(on, $"Dungeon {d + 1}   ({WaveAuthoringSO.DayCount(d)} days)", EditorStyles.miniButton, GUILayout.Height(24)) && !on)
            { int dd = d; pending = () => { dungeon = dd; collIndex = 0; openDay = -1; openCluster = -1; }; }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    // ---- palette bar ----------------------------------------------------------------------
    void DrawPaletteBar()
    {
        var pal = data.Palette(dungeon);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Brush", GUILayout.Width(40));

        GUI.backgroundColor = eraser ? ColSel : ColPanel;
        if (GUILayout.Button("Erase", GUILayout.Width(60), GUILayout.Height(34))) { eraser = true; brush = null; }
        GUI.backgroundColor = Color.white;

        if (pal == null || pal.Length == 0)
        {
            GUILayout.Label("No enemies for this dungeon — assign them in Settings / Palette.", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var so in pal)
            {
                if (so == null) continue;
                bool seld = !eraser && brush == so;
                GUI.backgroundColor = seld ? ColSel : ColPanel;
                var r = GUILayoutUtility.GetRect(38, 38, GUILayout.Width(38), GUILayout.Height(38));
                if (GUI.Button(r, GUIContent.none)) { brush = so; eraser = false; }
                GUI.backgroundColor = Color.white;
                DrawEnemyIcon(Inset(r, 3), so);
                GUI.Label(new Rect(r.x + 1, r.yMax - 12, r.width - 2, 11), ((int)WaveAuthoringSO.EnemyPoints(so)).ToString(), tally);
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ---- collections column ---------------------------------------------------------------
    void DrawCollectionsColumn()
    {
        var dw = data.GetDungeon(dungeon);
        EditorGUILayout.BeginVertical(GUILayout.Width(190));
        GUILayout.Label("COLLECTIONS", sub);
        collScroll = EditorGUILayout.BeginScrollView(collScroll, "box");
        for (int c = 0; c < dw.collections.Count; c++)
        {
            var coll = dw.collections[c];
            bool isMain = coll.name == WaveAuthoringSO.MAIN;
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = c == collIndex ? ColSel : ColPanel;
            if (GUILayout.Button((isMain ? "★ " : "• ") + coll.name, EditorStyles.miniButton, GUILayout.Height(22)))
            { int ci = c; pending = () => { collIndex = ci; openDay = -1; openCluster = -1; }; }
            GUI.backgroundColor = Color.white;
            if (!isMain && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
            {
                int ci = c;
                if (EditorUtility.DisplayDialog("Delete collection", $"Delete '{coll.name}'?", "Delete", "Cancel"))
                    pending = () => { dw.collections.RemoveAt(ci); collIndex = 0; openDay = -1; openCluster = -1; MarkDirty(); };
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button("+ New Collection", GUILayout.Height(24)))
            pending = () => { dw.collections.Add(new WaveCollection("Collection " + dw.collections.Count, dungeon)); collIndex = dw.collections.Count - 1; openDay = -1; openCluster = -1; MarkDirty(); };
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();
    }

    // ---- editor column --------------------------------------------------------------------
    void DrawEditorColumn()
    {
        var dw = data.GetDungeon(dungeon);
        collIndex = Mathf.Clamp(collIndex, 0, dw.collections.Count - 1);
        var coll = dw.collections[collIndex];
        coll.EnsureDays(WaveAuthoringSO.DayCount(dungeon));

        EditorGUILayout.BeginVertical();
        var hr = EditorGUILayout.GetControlRect(false, 26);
        EditorGUI.DrawRect(hr, ColPanel);
        if (coll.name == WaveAuthoringSO.MAIN)
        {
            GUI.Label(new Rect(hr.x + 8, hr.y + 4, hr.width - 16, 20), "★ Main  —  the main Ember Core's wave (always present)", sub);
        }
        else
        {
            GUI.Label(new Rect(hr.x + 8, hr.y + 5, 50, 18), "Name", EditorStyles.miniLabel);
            string nn = EditorGUI.TextField(new Rect(hr.x + 56, hr.y + 3, 220, 20), coll.name);
            if (nn != coll.name && !string.IsNullOrEmpty(nn)) { coll.name = nn; MarkDirty(); }
        }

        editScroll = EditorGUILayout.BeginScrollView(editScroll);
        DrawDaysSection(coll);
        EditorGUILayout.Space(8);
        DrawClustersSection(coll);
        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawDaysSection(WaveCollection coll)
    {
        GUILayout.Label("DAYS", sub);
        int dayCount = WaveAuthoringSO.DayCount(dungeon);
        // For non-main collections, show what an extra core using this collection is expected to bring
        // each day (budgetPerExtraCore × the Main collection's day price).
        bool nonMain = coll.name != WaveAuthoringSO.MAIN;
        WaveCollection mainColl = nonMain ? data.GetCollection(dungeon, WaveAuthoringSO.MAIN) : null;
        if (mainColl != null) mainColl.EnsureDays(dayCount);
        for (int d = 0; d < dayCount; d++)
        {
            var day = coll.days[d];
            float pts = WaveAuthoringSO.DayPoints(day);
            float auto = WaveAuthoringSO.DayAutoPoints(day);
            bool open = openDay == d;

            var row = EditorGUILayout.GetControlRect(false, 24);
            EditorGUI.DrawRect(row, open ? ColSel * 0.6f : ColPanel);
            if (GUI.Button(new Rect(row.x, row.y, row.width - 150, row.height), GUIContent.none, GUIStyle.none))
            { int dd = d; bool wasOpen = open; pending = () => { openDay = wasOpen ? -1 : dd; openSub = 0; }; }
            GUI.Label(new Rect(row.x + 8, row.y + 3, 200, 18), (open ? "▼ " : "▶ ") + "Day " + (d + 1), EditorStyles.boldLabel);
            if (nonMain && mainColl != null)
            {
                float exp = data.budgetPerExtraCore * WaveAuthoringSO.DayPoints(mainColl.days[d]);
                GUI.Label(new Rect(row.xMax - 292, row.y + 4, 138, 16), "expect ≈ " + Mathf.RoundToInt(exp), expLabel);
            }
            DrawScoreChip(new Rect(row.xMax - 146, row.y + 2, 138, 20), pts, day.scoreOverridden);

            if (!open) continue;

            EditorGUILayout.BeginVertical("box");
            DrawOverrideRow(ref day.scoreOverridden, ref day.scoreOverride, auto);

            for (int s = 0; s < day.subwaves.Count; s++)
            {
                var swv = day.subwaves[s];
                bool sopen = openSub == s;
                var sr = EditorGUILayout.GetControlRect(false, 22);
                EditorGUI.DrawRect(sr, sopen ? new Color(0.30f, 0.40f, 0.55f) : ColCellAlt);
                if (GUI.Button(new Rect(sr.x, sr.y, sr.width - 60, sr.height), GUIContent.none, GUIStyle.none))
                { int ss = s; bool wasOpen = sopen; pending = () => openSub = wasOpen ? -1 : ss; }
                GUI.Label(new Rect(sr.x + 8, sr.y + 2, sr.width - 120, 18),
                    $"{(sopen ? "▼" : "▶")}  Subwave {s + 1}    t = {swv.time:0.0}s    ({(int)WaveAuthoringSO.SubwavePoints(swv)} pts)", EditorStyles.miniBoldLabel);
                if (GUI.Button(new Rect(sr.xMax - 52, sr.y + 1, 48, 19), "Delete", EditorStyles.miniButton))
                { int si = s; var dd = day; pending = () => { dd.subwaves.RemoveAt(si); if (openSub >= dd.subwaves.Count) openSub = dd.subwaves.Count - 1; MarkDirty(); }; }

                if (!sopen) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Start (s)", GUILayout.Width(54));
                float nt = EditorGUILayout.FloatField(swv.time, GUILayout.Width(58));
                if (!Mathf.Approximately(nt, swv.time)) { swv.time = Mathf.Max(0f, nt); MarkDirty(); }
                GUILayout.Space(12);
                GUILayout.Label("Duration (s)", GUILayout.Width(74));
                float nsd = EditorGUILayout.FloatField(swv.duration, GUILayout.Width(58));
                if (!Mathf.Approximately(nsd, swv.duration)) { swv.duration = Mathf.Max(0f, nsd); MarkDirty(); }
                EditorGUILayout.EndHorizontal();
                DrawGrid(swv.enemies);
            }

            GUI.backgroundColor = ColAccent;
            if (GUILayout.Button("+ Subwave", GUILayout.Height(20)))
            { var dd = day; pending = () => { float t = dd.subwaves.Count > 0 ? dd.subwaves[dd.subwaves.Count - 1].time + 3f : 0f; dd.subwaves.Add(new Subwave { time = t }); openSub = dd.subwaves.Count - 1; MarkDirty(); }; }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }
    }

    void DrawClustersSection(WaveCollection coll)
    {
        GUILayout.Label("CLUSTERS  (collection-wide budget fillers — no time)", sub);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Favour (round-robin weight)", GUILayout.Width(170));
        int nf = EditorGUILayout.IntField(coll.clusterFavour, GUILayout.Width(48));
        if (nf != coll.clusterFavour) { coll.clusterFavour = Mathf.Max(1, nf); MarkDirty(); }
        EditorGUILayout.EndHorizontal();
        for (int c = 0; c < coll.clusters.Count; c++)
        {
            var cl = coll.clusters[c];
            float pts = WaveAuthoringSO.ClusterPoints(cl);
            float auto = WaveAuthoringSO.ClusterAutoPoints(cl);
            bool open = openCluster == c;

            var row = EditorGUILayout.GetControlRect(false, 24);
            EditorGUI.DrawRect(row, open ? ColSel * 0.6f : ColPanel);
            if (GUI.Button(new Rect(row.x, row.y, row.width - 210, row.height), GUIContent.none, GUIStyle.none))
            { int cc = c; bool wasOpen = open; pending = () => openCluster = wasOpen ? -1 : cc; }
            GUI.Label(new Rect(row.x + 8, row.y + 3, 220, 18), (open ? "▼ " : "▶ ") + "Cluster " + c + "  " + cl.name, EditorStyles.boldLabel);
            DrawScoreChip(new Rect(row.xMax - 206, row.y + 2, 138, 20), pts, cl.scoreOverridden);
            if (GUI.Button(new Rect(row.xMax - 60, row.y + 2, 52, 20), "Delete", EditorStyles.miniButton))
            { int ci = c; pending = () => { coll.clusters.RemoveAt(ci); if (openCluster >= coll.clusters.Count) openCluster = -1; MarkDirty(); }; }

            if (!open) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(40));
            string nn = EditorGUILayout.TextField(cl.name, GUILayout.Width(180));
            if (nn != cl.name) { cl.name = nn; MarkDirty(); }
            GUILayout.Space(12);
            GUILayout.Label("Duration (s)", GUILayout.Width(74));
            float ncd = EditorGUILayout.FloatField(cl.duration, GUILayout.Width(58));
            if (!Mathf.Approximately(ncd, cl.duration)) { cl.duration = Mathf.Max(0f, ncd); MarkDirty(); }
            EditorGUILayout.EndHorizontal();
            DrawOverrideRow(ref cl.scoreOverridden, ref cl.scoreOverride, auto);
            DrawGrid(cl.enemies);
            EditorGUILayout.EndVertical();
        }

        GUI.backgroundColor = ColAccent;
        if (GUILayout.Button("+ New Cluster", GUILayout.Height(22)))
            pending = () => { coll.clusters.Add(new ClusterPlan { name = "Cluster " + coll.clusters.Count }); openCluster = coll.clusters.Count - 1; MarkDirty(); };
        GUI.backgroundColor = Color.white;
    }

    void DrawOverrideRow(ref bool overridden, ref float value, float auto)
    {
        bool was = overridden; // draw this frame's controls from the pre-click value
        EditorGUILayout.BeginHorizontal();
        bool nb = EditorGUILayout.ToggleLeft("Override score", overridden, GUILayout.Width(120));
        if (nb != overridden) { overridden = nb; if (overridden) value = auto; MarkDirty(); }
        if (was)
        {
            float nv = EditorGUILayout.FloatField(value, GUILayout.Width(70));
            if (!Mathf.Approximately(nv, value)) { value = Mathf.Max(0f, nv); MarkDirty(); }
            GUILayout.Label($"(auto would be {(int)auto})", EditorStyles.miniLabel);
        }
        else GUILayout.Label($"auto = {(int)auto} pts", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ---- the grid -------------------------------------------------------------------------
    void DrawGrid(List<PlacedEnemy> list)
    {
        int cols = Mathf.Max(1, data.gridCols);
        int rows = Mathf.Max(1, data.gridRows);
        var area = GUILayoutUtility.GetRect(cols * Cell, rows * Cell, GUILayout.ExpandWidth(false));

        var e = Event.current;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var cr = new Rect(area.x + x * Cell, area.y + y * Cell, Cell - 1, Cell - 1);
                bool centre = cols > 1 && Mathf.Abs(x - (cols - 1) / 2f) < 0.6f;
                EditorGUI.DrawRect(cr, centre ? ColCore : ((x + y) % 2 == 0 ? ColCell : ColCellAlt));

                int idx = IndexAt(list, x, y);
                if (idx >= 0) DrawEnemyIcon(Inset(cr, 2), list[idx].so);

                if (cr.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                {
                    bool erase = eraser || e.button == 1;
                    if (erase) { if (idx >= 0) { list.RemoveAt(idx); MarkDirty(); } }
                    else if (brush != null)
                    {
                        if (idx >= 0) list.RemoveAt(idx);
                        list.Add(new PlacedEnemy(brush, x, y));
                        MarkDirty();
                    }
                    e.Use();
                    Repaint();
                }
            }

        var leg = GUILayoutUtility.GetRect(cols * Cell, 14, GUILayout.ExpandWidth(false));
        GUI.Label(new Rect(leg.x, leg.y, 120, 14), "◄ left along rim", EditorStyles.miniLabel);
        GUI.Label(new Rect(leg.center.x - 16, leg.y, 60, 14), "core", EditorStyles.miniLabel);
        var rl = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        GUI.Label(new Rect(leg.xMax - 120, leg.y, 120, 14), "right along rim ►", rl);
    }

    // ---- helpers --------------------------------------------------------------------------
    static int IndexAt(List<PlacedEnemy> list, int x, int y)
    {
        for (int i = 0; i < list.Count; i++) if (list[i].gridX == x && list[i].gridY == y) return i;
        return -1;
    }

    void DrawScoreChip(Rect r, float pts, bool overridden)
    {
        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.14f));
        var c = Color.Lerp(new Color(0.45f, 0.85f, 0.45f), new Color(0.95f, 0.35f, 0.3f), Mathf.Clamp01(pts / 80f));
        var s = new GUIStyle(tally) { alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };
        GUI.Label(r, (overridden ? "✎ " : "★ ") + (int)pts + " pts", s);
    }

    Sprite GetSprite(MarauderSO so)
    {
        if (so == null || so.prefab == null) return null;
        if (spriteCache.TryGetValue(so, out var s)) return s;
        var sr = so.prefab.GetComponentInChildren<SpriteRenderer>(true);
        s = sr ? sr.sprite : null;
        spriteCache[so] = s;
        return s;
    }

    void DrawEnemyIcon(Rect r, MarauderSO so)
    {
        var spr = GetSprite(so);
        if (spr != null && spr.texture != null)
        {
            var t = spr.texture;
            var tc = new Rect(spr.rect.x / t.width, spr.rect.y / t.height, spr.rect.width / t.width, spr.rect.height / t.height);
            GUI.DrawTextureWithTexCoords(r, t, tc, true);
            return;
        }
        var prev = so != null && so.prefab != null ? AssetPreview.GetAssetPreview(so.prefab) : null;
        if (prev != null) { GUI.DrawTexture(r, prev, ScaleMode.ScaleToFit); return; }
        GUI.Label(r, so != null ? so.name : "?", gridCellName);
    }

    static Rect Inset(Rect r, float p) => new Rect(r.x + p, r.y + p, r.width - 2 * p, r.height - 2 * p);

    void EnsureStyles()
    {
        if (stylesReady) return;
        stylesReady = true;
        hdr = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = ColAccent } };
        sub = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
        tally = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        gridCellName = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 7, wordWrap = true, normal = { textColor = Color.white } };
        expLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.55f, 0.7f, 0.9f) } };
    }
}
