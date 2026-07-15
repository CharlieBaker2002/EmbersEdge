using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mecha Tree — visual authoring tool for MechaTreeSO (Tools > Mecha Tree).
/// Drag MechanismSO assets (or Part prefabs — they resolve to their MechanismSO) from the Project
/// window onto the canvas to add nodes. Left-drag moves a node; drag from a node's ○ handle (or
/// right-drag from its body) and release on another node to make it a PREREQUISITE of that node.
/// Click an edge to select it and recolour its AND-group in the side panel: a node unlocks when
/// ALL edges of ANY one colour group are owned ((A AND B) OR C). Boosts/standalones are refused —
/// they never live in the tree. Auto-saves to Resources/MechaTree.asset.
/// </summary>
public class MechaTreeWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/MechaTree.asset";
    const float PanelW = 260f;
    const float BaseNodeR = 34f;

    MechaTreeSO tree;
    List<MechanismSO> allMechs; // for Part-prefab drops

    Vector2 pan = new Vector2(60f, 60f);
    float zoom = 1f;

    int selNode = -1;
    int selDep = -1;   // index into tree.deps
    int dragNode = -1; // node being moved
    int linkFrom = -1; // node an edge is being dragged out of
    bool panning;
    bool showHelp;

    System.Action pending; // structural edits from the side panel, applied after OnGUI

    static readonly Color ColBg    = new Color(0.13f, 0.13f, 0.16f);
    static readonly Color ColGrid  = new Color(1f, 1f, 1f, 0.045f);
    static readonly Color ColNode  = new Color(0.22f, 0.22f, 0.27f);
    static readonly Color ColRim   = new Color(0.55f, 0.55f, 0.62f);
    static readonly Color ColSel   = new Color(0.30f, 0.65f, 0.95f);

    GUIStyle nameStyle, pipStyle;
    bool stylesReady;

    [MenuItem("Tools/Mecha Tree")]
    static void Open()
    {
        var w = GetWindow<MechaTreeWindow>("Mecha Tree");
        w.minSize = new Vector2(760, 480);
        w.Show();
    }

    void OnEnable()
    {
        tree = AssetDatabase.LoadAssetAtPath<MechaTreeSO>(AssetPath);
        allMechs = AssetDatabase.FindAssets("t:MechanismSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<MechanismSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null).ToList();
        Undo.undoRedoPerformed += Repaint;
        wantsMouseMove = true;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        if (tree != null) { EditorUtility.SetDirty(tree); AssetDatabase.SaveAssets(); }
    }

    void EnsureAsset()
    {
        if (tree != null) return;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        tree = CreateInstance<MechaTreeSO>();
        AssetDatabase.CreateAsset(tree, AssetPath);
        AssetDatabase.SaveAssets();
    }

    void EnsureStyles()
    {
        if (stylesReady) return;
        nameStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperCenter, wordWrap = false };
        nameStyle.normal.textColor = new Color(0.9f, 0.9f, 0.95f);
        pipStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        stylesReady = true;
    }

    // ───────────────────────────────── coords ─────────────────────────────────

    Rect CanvasRect => new Rect(0f, 22f, position.width - PanelW, position.height - 22f);
    float NodeR => BaseNodeR * zoom;

    Vector2 GraphToScreen(Vector2 g) => CanvasRect.position + pan + g * zoom;
    Vector2 ScreenToGraph(Vector2 s) => (s - CanvasRect.position - pan) / zoom;

    Vector2 LinkHandle(MechaTreeSO.Node n) => GraphToScreen(n.pos) + new Vector2(0f, NodeR + 9f * zoom);

    // ───────────────────────────────── GUI ─────────────────────────────────

    void OnGUI()
    {
        EnsureAsset();
        EnsureStyles();

        Toolbar();
        Rect canvas = CanvasRect;
        EditorGUI.DrawRect(canvas, ColBg);
        DrawGrid(canvas);

        HandleDragAndDrop(canvas);
        HandleEvents(canvas);

        DrawEdges();
        DrawLinkPreview();
        DrawNodes();

        // side panel background & content (drawn last, covers any node overdraw)
        Rect panel = new Rect(position.width - PanelW, 22f, PanelW, position.height - 22f);
        EditorGUI.DrawRect(panel, new Color(0.18f, 0.18f, 0.22f));
        GUILayout.BeginArea(new Rect(panel.x + 8f, panel.y + 8f, panel.width - 16f, panel.height - 16f));
        SidePanel();
        GUILayout.EndArea();

        if (showHelp) HelpOverlay(canvas);

        if (pending != null) { var p = pending; pending = null; p(); Repaint(); }
    }

    void Toolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Asset", EditorStyles.toolbarButton, GUILayout.Width(50f)))
            EditorGUIUtility.PingObject(tree);
        if (GUILayout.Button("Frame", EditorStyles.toolbarButton, GUILayout.Width(50f)))
            FrameAll();
        GUILayout.Space(8f);
        GUILayout.Label(tree.nodes.Count + " parts, " + tree.deps.Count + " links   |   zoom " + zoom.ToString("F2"), EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        showHelp = GUILayout.Toggle(showHelp, "?", EditorStyles.toolbarButton, GUILayout.Width(26f));
        GUILayout.EndHorizontal();
    }

    void FrameAll()
    {
        if (tree.nodes.Count == 0) { pan = new Vector2(60f, 60f); zoom = 1f; return; }
        Vector2 min = tree.nodes[0].pos, max = tree.nodes[0].pos;
        foreach (var n in tree.nodes) { min = Vector2.Min(min, n.pos); max = Vector2.Max(max, n.pos); }
        Vector2 size = max - min + Vector2.one * 200f;
        Rect c = CanvasRect;
        zoom = Mathf.Clamp(Mathf.Min(c.width / size.x, c.height / size.y), 0.35f, 1.25f);
        pan = c.size * 0.5f - (min + max) * 0.5f * zoom;
        Repaint();
    }

    void DrawGrid(Rect r)
    {
        float step = 40f * zoom;
        Handles.color = ColGrid;
        for (float x = r.x + pan.x % step; x < r.xMax; x += step)
            if (x >= r.x) Handles.DrawLine(new Vector3(x, r.y), new Vector3(x, r.yMax));
        for (float y = r.y + pan.y % step; y < r.yMax; y += step)
            if (y >= r.y) Handles.DrawLine(new Vector3(r.x, y), new Vector3(r.xMax, y));
    }

    // ───────────────────────────────── input ─────────────────────────────────

    void HandleEvents(Rect canvas)
    {
        Event e = Event.current;
        if (!canvas.Contains(e.mousePosition) && dragNode < 0 && linkFrom < 0 && !panning)
        {
            if (e.type == EventType.KeyDown) HandleKeys(e);
            return;
        }

        switch (e.type)
        {
            case EventType.ScrollWheel:
            {
                Vector2 before = ScreenToGraph(e.mousePosition);
                zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? 0.92f : 1.087f), 0.3f, 1.6f);
                pan += (GraphToScreen(before) - e.mousePosition) * -1f;
                e.Use();
                break;
            }
            case EventType.MouseDown when e.button == 2:
                panning = true; e.Use();
                break;
            case EventType.MouseDown when e.button == 0:
            {
                int handle = HandleAt(e.mousePosition);
                if (handle >= 0) { linkFrom = handle; selNode = handle; selDep = -1; }
                else
                {
                    int n = NodeAt(e.mousePosition);
                    if (n >= 0)
                    {
                        selNode = n; selDep = -1; dragNode = n;
                        Undo.RecordObject(tree, "Move Part");
                    }
                    else
                    {
                        selDep = DepAt(e.mousePosition);
                        selNode = -1;
                    }
                }
                GUI.FocusControl(null);
                e.Use();
                break;
            }
            case EventType.MouseDown when e.button == 1:
            {
                int n = NodeAt(e.mousePosition);
                if (n >= 0) { linkFrom = n; selNode = n; selDep = -1; e.Use(); }
                break;
            }
            case EventType.MouseDrag when panning:
                pan += e.delta; e.Use();
                break;
            case EventType.MouseDrag when dragNode >= 0:
            {
                var n = tree.ById(dragNode);
                if (n != null) n.pos += e.delta / zoom;
                EditorUtility.SetDirty(tree);
                e.Use();
                break;
            }
            case EventType.MouseDrag when linkFrom >= 0:
                e.Use(); // repaint keeps drawing the preview line
                break;
            case EventType.MouseUp when e.button == 2:
                panning = false; e.Use();
                break;
            case EventType.MouseUp when linkFrom >= 0:
            {
                int target = NodeAt(e.mousePosition);
                if (target >= 0 && target != linkFrom) TryAddDep(linkFrom, target);
                else if (e.button == 1 && target == linkFrom) NodeContextMenu(linkFrom);
                linkFrom = -1;
                e.Use();
                break;
            }
            case EventType.MouseUp when dragNode >= 0:
                dragNode = -1; e.Use();
                break;
            case EventType.KeyDown:
                HandleKeys(e);
                break;
            case EventType.MouseMove:
                Repaint();
                break;
        }
        if (e.type == EventType.MouseDrag || panning || linkFrom >= 0) Repaint();
    }

    void HandleKeys(Event e)
    {
        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (selNode >= 0) { DeleteNode(selNode); e.Use(); }
            else if (selDep >= 0) { DeleteDep(selDep); e.Use(); }
        }
        else if (e.keyCode == KeyCode.F) { FrameAll(); e.Use(); }
        else if (e.keyCode == KeyCode.G && selDep >= 0 && selDep < tree.deps.Count)
        {
            Undo.RecordObject(tree, "Cycle Group");
            tree.deps[selDep].group = (tree.deps[selDep].group + 1) % MechaTreeSO.GroupColors.Length;
            EditorUtility.SetDirty(tree);
            e.Use();
        }
    }

    void NodeContextMenu(int id)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Delete Part"), false, () => { DeleteNode(id); Repaint(); });
        menu.ShowAsContext();
    }

    // ───────────────────────────────── hit tests ─────────────────────────────────

    int NodeAt(Vector2 screen)
    {
        for (int i = tree.nodes.Count - 1; i >= 0; i--)
            if (Vector2.Distance(GraphToScreen(tree.nodes[i].pos), screen) <= NodeR)
                return tree.nodes[i].id;
        return -1;
    }

    int HandleAt(Vector2 screen)
    {
        foreach (var n in tree.nodes)
            if (Vector2.Distance(LinkHandle(n), screen) <= 8f * Mathf.Max(0.7f, zoom))
                return n.id;
        return -1;
    }

    int DepAt(Vector2 screen)
    {
        for (int i = 0; i < tree.deps.Count; i++)
        {
            var a = tree.ById(tree.deps[i].from);
            var b = tree.ById(tree.deps[i].to);
            if (a == null || b == null) continue;
            EdgePoints(a, b, out Vector2 p0, out Vector2 p1, out Vector2 t0, out Vector2 t1);
            Vector2 prev = p0;
            for (int s = 1; s <= 24; s++)
            {
                Vector2 pt = Bezier(p0, t0, t1, p1, s / 24f);
                if (DistToSegment(screen, prev, pt) < 6f) return i;
                prev = pt;
            }
        }
        return -1;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    static Vector2 Bezier(Vector2 p0, Vector2 t0, Vector2 t1, Vector2 p1, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * t0 + 3f * u * t * t * t1 + t * t * t * p1;
    }

    // ───────────────────────────────── structure edits ─────────────────────────────────

    void TryAddDep(int from, int to)
    {
        if (tree.deps.Any(d => d.from == from && d.to == to))
        { ShowNotification(new GUIContent("Link already exists")); return; }
        if (Reaches(to, from))
        { ShowNotification(new GUIContent("That would make a loop!")); return; }
        Undo.RecordObject(tree, "Add Dependency");
        tree.deps.Add(new MechaTreeSO.Dep { from = from, to = to, group = 0 });
        selDep = tree.deps.Count - 1;
        selNode = -1;
        EditorUtility.SetDirty(tree);
    }

    /// can `start` reach `goal` walking prerequisite→dependent edges?
    bool Reaches(int start, int goal)
    {
        var seen = new HashSet<int> { start };
        var stack = new Stack<int>(); stack.Push(start);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (cur == goal) return true;
            foreach (var d in tree.deps)
                if (d.from == cur && seen.Add(d.to)) stack.Push(d.to);
        }
        return false;
    }

    void DeleteNode(int id)
    {
        Undo.RecordObject(tree, "Delete Part");
        tree.nodes.RemoveAll(n => n.id == id);
        tree.deps.RemoveAll(d => d.from == id || d.to == id);
        if (selNode == id) selNode = -1;
        selDep = -1;
        EditorUtility.SetDirty(tree);
    }

    void DeleteDep(int index)
    {
        if (index < 0 || index >= tree.deps.Count) return;
        Undo.RecordObject(tree, "Delete Dependency");
        tree.deps.RemoveAt(index);
        selDep = -1;
        EditorUtility.SetDirty(tree);
    }

    // ───────────────────────────────── drag & drop ─────────────────────────────────

    void HandleDragAndDrop(Rect canvas)
    {
        Event e = Event.current;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
        if (!canvas.Contains(e.mousePosition)) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Vector2 at = ScreenToGraph(e.mousePosition);
            foreach (Object o in DragAndDrop.objectReferences)
            {
                MechanismSO m = Resolve(o);
                if (m == null)
                { ShowNotification(new GUIContent(o.name + ": not a mechanism (Part prefab or MechanismSO)")); continue; }
                if (!MechaTreeSO.AllowedInTree(m))
                { ShowNotification(new GUIContent(m.name + ": boosts/standalones don't go in the tree")); continue; }
                if (tree.nodes.Any(n => n.mech == m))
                { ShowNotification(new GUIContent(m.name + " is already in the tree")); continue; }
                Undo.RecordObject(tree, "Add Part");
                tree.nodes.Add(new MechaTreeSO.Node { id = tree.nextId++, mech = m, pos = at, maxLevel = 1 });
                selNode = tree.nodes[tree.nodes.Count - 1].id;
                selDep = -1;
                at += new Vector2(90f, 0f);
                EditorUtility.SetDirty(tree);
            }
        }
        e.Use();
    }

    MechanismSO Resolve(Object o)
    {
        if (o is MechanismSO m) return m;
        if (o is GameObject go)
        {
            Part p = go.GetComponent<Part>();
            if (p != null) return allMechs.FirstOrDefault(x => x.p == p);
        }
        return null;
    }

    // ───────────────────────────────── drawing ─────────────────────────────────

    void EdgePoints(MechaTreeSO.Node a, MechaTreeSO.Node b, out Vector2 p0, out Vector2 p1, out Vector2 t0, out Vector2 t1)
    {
        Vector2 ca = GraphToScreen(a.pos), cb = GraphToScreen(b.pos);
        Vector2 dir = (cb - ca).normalized;
        p0 = ca + dir * NodeR;
        p1 = cb - dir * NodeR;
        float d = Vector2.Distance(p0, p1);
        Vector2 perp = new Vector2(-dir.y, dir.x) * Mathf.Min(26f, d * 0.16f);
        t0 = p0 + dir * d * 0.3f + perp;
        t1 = p1 - dir * d * 0.3f + perp;
    }

    void DrawEdges()
    {
        for (int i = 0; i < tree.deps.Count; i++)
        {
            var dep = tree.deps[i];
            var a = tree.ById(dep.from);
            var b = tree.ById(dep.to);
            if (a == null || b == null) continue;
            EdgePoints(a, b, out Vector2 p0, out Vector2 p1, out Vector2 t0, out Vector2 t1);
            Color c = MechaTreeSO.GroupColor(dep.group);
            bool sel = i == selDep;
            if (!sel && selNode >= 0 && dep.to != selNode && dep.from != selNode) c.a = 0.55f;
            Handles.DrawBezier(p0, p1, t0, t1, c, null, sel ? 6f : 3f);
            // arrow chevron near the dependent end
            Vector2 tip = Bezier(p0, t0, t1, p1, 0.92f);
            Vector2 back = Bezier(p0, t0, t1, p1, 0.84f);
            Vector2 dr = (tip - back).normalized * 8f;
            Vector2 pp = new Vector2(-dr.y, dr.x) * 0.6f;
            Handles.color = c;
            Handles.DrawAAPolyLine(sel ? 5f : 3f, tip, tip - dr + pp);
            Handles.DrawAAPolyLine(sel ? 5f : 3f, tip, tip - dr - pp);
        }
    }

    void DrawLinkPreview()
    {
        if (linkFrom < 0) return;
        var n = tree.ById(linkFrom);
        if (n == null) { linkFrom = -1; return; }
        Vector2 from = GraphToScreen(n.pos);
        Vector2 to = Event.current.mousePosition;
        Handles.DrawBezier(from, to, from + Vector2.up * 30f, to - Vector2.up * 30f, new Color(1f, 1f, 1f, 0.8f), null, 3f);
        int over = NodeAt(to);
        if (over >= 0 && over != linkFrom)
        {
            Handles.color = Color.white;
            Handles.DrawWireDisc(GraphToScreen(tree.ById(over).pos), Vector3.forward, NodeR + 4f);
        }
    }

    void DrawNodes()
    {
        foreach (var n in tree.nodes)
        {
            Vector2 c = GraphToScreen(n.pos);
            float r = NodeR;
            bool sel = n.id == selNode;

            Handles.color = ColNode;
            Handles.DrawSolidDisc(c, Vector3.forward, r);
            Handles.color = sel ? ColSel : ColRim;
            Handles.DrawWireDisc(c, Vector3.forward, r);
            if (sel) Handles.DrawWireDisc(c, Vector3.forward, r + 2f);

            // OR badge: one dot per incoming AND-group, sitting on the top rim
            var groups = tree.GroupsInto(n.id);
            if (groups.Count > 0)
            {
                float spread = 10f * zoom;
                float x0 = c.x - (groups.Count - 1) * spread * 0.5f;
                for (int g = 0; g < groups.Count; g++)
                {
                    Handles.color = MechaTreeSO.GroupColor(groups[g]);
                    Handles.DrawSolidDisc(new Vector2(x0 + g * spread, c.y - r), Vector3.forward, 4f * Mathf.Max(0.6f, zoom));
                }
            }

            Sprite icon = MechaTreeSO.IconOf(n);
            if (icon != null)
            {
                Color prev = GUI.color;
                GUI.color = MechaTreeSO.IconTint(n);
                DrawSprite(new Rect(c.x - r * 0.62f, c.y - r * 0.62f, r * 1.24f, r * 1.24f), icon);
                GUI.color = prev;
            }

            // upgrade pips
            if (n.maxLevel > 1)
            {
                float spread = 9f * zoom;
                float x0 = c.x - (n.maxLevel - 1) * spread * 0.5f;
                for (int l = 0; l < n.maxLevel; l++)
                {
                    Handles.color = new Color(0.95f, 0.85f, 0.4f);
                    Handles.DrawSolidDisc(new Vector2(x0 + l * spread, c.y + r * 0.68f), Vector3.forward, 2.6f * Mathf.Max(0.6f, zoom));
                }
            }

            // link handle
            Handles.color = new Color(1f, 1f, 1f, 0.55f);
            Handles.DrawWireDisc(LinkHandle(n), Vector3.forward, 5f * Mathf.Max(0.7f, zoom));

            if (zoom > 0.45f)
                GUI.Label(new Rect(c.x - 70f, c.y + r + 12f * zoom, 140f, 18f), n.mech != null ? n.mech.name : "(missing)", nameStyle);
        }
    }

    static void DrawSprite(Rect r, Sprite s)
    {
        Texture2D tex = s.texture;
        if (tex == null) return;
        Rect tr = s.rect;
        GUI.DrawTextureWithTexCoords(r, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height), true);
    }

    // ───────────────────────────────── side panel ─────────────────────────────────

    void SidePanel()
    {
        GUILayout.Label("MECHA TREE", EditorStyles.boldLabel);
        GUILayout.Space(4f);

        if (selNode >= 0 && tree.ById(selNode) != null)
        {
            var n = tree.ById(selNode);
            GUILayout.Label(n.mech != null ? n.mech.name : "(missing mechanism)", EditorStyles.largeLabel);
            if (n.mech != null && n.mech.p != null)
                GUILayout.Label(n.mech.p.taip.ToString(), EditorStyles.miniLabel);
            GUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            int max = EditorGUILayout.IntSlider("Max Level", n.maxLevel, 1, 3);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(tree, "Max Level");
                n.maxLevel = max;
                EditorUtility.SetDirty(tree);
            }
            GUILayout.Label(max > 1 ? "Clicking the unlocked part in-game upgrades it." : "Single-level part.", EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();
            var ov = (Sprite)EditorGUILayout.ObjectField("Icon Override", n.iconOverride, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(tree, "Icon Override");
                n.iconOverride = ov;
                EditorUtility.SetDirty(tree);
            }
            GUILayout.Label("Empty = the part prefab's own sprite.", EditorStyles.miniLabel);

            GUILayout.Space(8f);
            GUILayout.Label("Requires", EditorStyles.boldLabel);
            GUILayout.Label(tree.RequirementText(n.id), EditorStyles.wordWrappedLabel);

            GUILayout.Space(8f);
            if (GUILayout.Button("Delete Part"))
                pending = () => DeleteNode(n.id);
        }
        else if (selDep >= 0 && selDep < tree.deps.Count)
        {
            var d = tree.deps[selDep];
            var from = tree.ById(d.from);
            var to = tree.ById(d.to);
            GUILayout.Label((from?.mech != null ? from.mech.name : "?") + "  →  " + (to?.mech != null ? to.mech.name : "?"), EditorStyles.largeLabel);
            GUILayout.Space(6f);
            GUILayout.Label("AND-group (edges of one colour must ALL be owned;\ndifferent colours are OR alternatives)", EditorStyles.wordWrappedMiniLabel);
            GUILayout.BeginHorizontal();
            for (int g = 0; g < MechaTreeSO.GroupColors.Length; g++)
            {
                GUI.backgroundColor = MechaTreeSO.GroupColor(g) * (d.group == g ? 1.4f : 0.8f);
                if (GUILayout.Button(d.group == g ? "●" : " ", GUILayout.Width(30f), GUILayout.Height(24f)))
                {
                    Undo.RecordObject(tree, "Set Group");
                    d.group = g;
                    EditorUtility.SetDirty(tree);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label("(G also cycles the group)", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            if (to != null) GUILayout.Label(to.mech != null ? to.mech.name + " requires: " + tree.RequirementText(to.id) : "", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8f);
            if (GUILayout.Button("Delete Link"))
            {
                int idx = selDep;
                pending = () => DeleteDep(idx);
            }
        }
        else
        {
            GUILayout.Label("Drop MechanismSO assets or Part prefabs\nonto the canvas to add parts.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(6f);
            GUILayout.Label("• Left-drag a part to move it\n• Drag from its ○ handle (or right-drag it)\n  onto another part → that part now\n  requires this one\n• Click a line to recolour its AND-group\n• Delete removes the selection\n• F frames everything, scroll zooms,\n  middle-drag pans", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6f);
            GUILayout.Label("In-game: N opens the tree. Unlocks are\ninstant & permanent; abilities cap at 3.", EditorStyles.wordWrappedMiniLabel);
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label(AssetPath, EditorStyles.centeredGreyMiniLabel);
    }

    void HelpOverlay(Rect canvas)
    {
        Rect r = new Rect(canvas.x + 12f, canvas.y + 12f, 380f, 128f);
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.72f));
        GUI.Label(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f),
            "Drop MechanismSOs / Part prefabs to add parts (boosts & standalones are refused).\n" +
            "Drag ○ → another part to draw a dependency (source becomes the prerequisite).\n" +
            "Edge colours are AND-groups: ALL edges of ONE colour unlock the part — " +
            "different colours are OR alternatives. Select an edge to recolour (G cycles).\n" +
            "Max Level > 1 makes the part upgradeable by re-clicking it in-game.",
            EditorStyles.wordWrappedMiniLabel);
    }
}
