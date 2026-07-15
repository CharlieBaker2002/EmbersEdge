using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// In-game mecha tech tree (N to toggle). Renders the tree authored in Tools > Mecha Tree
/// (Resources/MechaTree) as a full-screen paused menu built entirely in code under the UI canvas.
/// Clicking a reachable part unlocks it instantly (AddParts, permanent — survives death); clicking
/// an unlocked upgradeable part levels it up, shown as a bright line creeping around its ring.
/// Unlock state is derived live from MechaSuit.m.parts (parts are matched by name), so deaths,
/// restarts and manifestor defaults all stay truthful with zero bookkeeping.
/// </summary>
public class MechaTreeUI : MonoBehaviour
{
    public static MechaTreeUI i;

    const float NodeSize = 110f;
    const float NodeRadius = NodeSize * 0.5f;
    const float RingRadius = NodeSize * 0.5f - 6f;

    MechaTreeSO tree;
    GameObject root;
    CanvasGroup cg;
    RectTransform content;
    TextMeshProUGUI slotsText;
    int pauseID;
    bool open;
    System.Action escHandler;

    enum NodeState { Locked, Available, CapBlocked, Unlocked, Maxed }

    class NodeWidget
    {
        public MechaTreeSO.Node node;
        public RectTransform rt;
        public Image disc, ring, icon, flash;
        public RectTransform tip;
        public TextMeshProUGUI label, lvl;
        public Color tint = Color.white; // the prefab's SpriteRenderer tint
        public NodeState state = (NodeState)(-1);
    }

    class EdgeWidget
    {
        public MechaTreeSO.Dep dep;
        public RectTransform rt;
        public Image line;
        public RectTransform pulse;
        public Image pulseImg;
        public float len;
    }

    readonly Dictionary<int, NodeWidget> widgets = new Dictionary<int, NodeWidget>();
    readonly List<EdgeWidget> edges = new List<EdgeWidget>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (i != null) return;
        var go = new GameObject("MechaTreeUI");
        DontDestroyOnLoad(go);
        i = go.AddComponent<MechaTreeUI>();
    }

    void Awake()
    {
        if (i != null && i != this) { Destroy(this); return; }
        i = this;
        escHandler = Close;
    }

    void OnDestroy()
    {
        if (i == this) i = null;
    }

    void Update()
    {
        if (open && (root == null || CharacterScript.dead)) { Close(); return; }
        if (Keyboard.current == null || !Keyboard.current.nKey.wasPressedThisFrame) return;
        if (open) { Close(); return; }
        TryOpen();
    }

    // ───────────────────────────────── open / close ─────────────────────────────────

    void TryOpen()
    {
        if (UIManager.i == null || MechaSuit.m == null || CharacterScript.dead) return;
        if (UIManager.i.pauseUI.activeInHierarchy || UIManager.partOptionOpened) return;
        if (BM.i != null && BM.i.UI.activeInHierarchy) return;
        if (TutorialManager.tutorial) return;
        tree = Resources.Load<MechaTreeSO>("MechaTree");
        if (tree == null || tree.nodes.Count == 0)
        {
            CM.Message("The mecha tree hasn't been authored yet (Tools > Mecha Tree).");
            return;
        }
        open = true;
        pauseID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
        EscapeRouter.i.Push(escHandler);
        Build();
        cg.alpha = 0f;
        LeanTween.value(root, 0f, 1f, 0.25f).setOnUpdate((float f) => { if (cg != null) cg.alpha = f; }).setIgnoreTimeScale(true);
    }

    void Close()
    {
        if (!open) return;
        open = false;
        if (SpawnManager.instance != null) SpawnManager.instance.CancelTS(pauseID);
        if (EscapeRouter.i != null) EscapeRouter.i.Remove(escHandler);
        widgets.Clear();
        edges.Clear();
        if (root == null) return;
        GameObject dying = root;
        root = null;
        LeanTween.cancel(dying);
        LeanTween.value(dying, cg.alpha, 0f, 0.18f)
            .setOnUpdate((float f) => { var c = dying.GetComponent<CanvasGroup>(); if (c != null) c.alpha = f; })
            .setOnComplete(() => Destroy(dying))
            .setIgnoreTimeScale(true);
    }

    // ───────────────────────────────── state ─────────────────────────────────

    Part LivePart(MechaTreeSO.Node n)
    {
        if (n.mech == null || MechaSuit.m == null) return null;
        return MechaSuit.m.parts.FirstOrDefault(p => p != null && p.name == n.mech.name);
    }

    bool Unlocked(int id)
    {
        var n = tree.ById(id);
        return n != null && LivePart(n) != null;
    }

    int DispLevel(Part p) => Mathf.Max(1, p.level);

    NodeState StateOf(MechaTreeSO.Node n)
    {
        Part live = LivePart(n);
        if (live != null) return DispLevel(live) >= n.maxLevel ? NodeState.Maxed : NodeState.Unlocked;
        if (!tree.Satisfied(n.id, Unlocked)) return NodeState.Locked;
        if (n.mech.p.taip == Part.PartType.Ability && MechaSuit.abilitiesLeft <= 0) return NodeState.CapBlocked;
        return NodeState.Available;
    }

    // Mirrors CD.SetColour: green for active parts, blue for motion/core, yellow otherwise.
    static Color Accent(Part.PartType t)
    {
        if (t is Part.PartType.Weapon or Part.PartType.Ability) return new Color(0.5f, 0.95f, 0.55f);
        if (Part.Ring(t) == Part.RingClassifier.Core || t == Part.PartType.Melee) return new Color(0.45f, 0.75f, 1f);
        return new Color(1f, 0.85f, 0.4f);
    }

    // ───────────────────────────────── build ─────────────────────────────────

    void Build()
    {
        root = new GameObject("MechaTree", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        var rootRT = (RectTransform)root.transform;
        rootRT.SetParent(UIManager.i.canvas, false);
        rootRT.SetAsLastSibling();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;
        cg = root.GetComponent<CanvasGroup>();
        var bg = root.GetComponent<Image>();
        bg.color = new Color(0.02f, 0.02f, 0.05f, 0.9f);

        Rect canvasRect = UIManager.i.canvas.rect;

        var title = MakeText(rootRT, "MECHA SUIT", 46f, new Vector2(0f, canvasRect.height * 0.5f - 55f));
        title.color = Color.Lerp(GS.ColFromEra(), Color.white, 0.45f);
        title.fontStyle = FontStyles.Bold;

        slotsText = MakeText(rootRT, "", 24f, new Vector2(0f, canvasRect.height * 0.5f - 95f));
        slotsText.color = new Color(1f, 1f, 1f, 0.55f);

        var hint = MakeText(rootRT, "Click to unlock  •  click again to upgrade  •  N / Esc to close", 22f, new Vector2(0f, -canvasRect.height * 0.5f + 40f));
        hint.color = new Color(1f, 1f, 1f, 0.35f);

        content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(rootRT, false);
        content.anchoredPosition = new Vector2(0f, -14f);

        // graph → screen mapping: fit the authored layout into the middle of the canvas, y flipped
        var live = tree.nodes.Where(n => MechaTreeSO.AllowedInTree(n.mech)).ToList();
        if (live.Count == 0) return;
        Vector2 min = live[0].pos, max = live[0].pos;
        foreach (var n in live) { min = Vector2.Min(min, n.pos); max = Vector2.Max(max, n.pos); }
        Vector2 mid = (min + max) * 0.5f;
        float scale = Mathf.Min(
            (canvasRect.width - 260f) / Mathf.Max(max.x - min.x, 1f),
            (canvasRect.height - 320f) / Mathf.Max(max.y - min.y, 1f));
        scale = Mathf.Clamp(scale, 0.15f, 1.4f);
        Vector2 Map(Vector2 g) => new Vector2((g.x - mid.x) * scale, -(g.y - mid.y) * scale);

        foreach (var dep in tree.deps)
        {
            var a = tree.ById(dep.from);
            var b = tree.ById(dep.to);
            if (a == null || b == null) continue;
            if (!live.Contains(a) || !live.Contains(b)) continue;
            edges.Add(MakeEdge(dep, Map(a.pos), Map(b.pos)));
        }
        foreach (var n in live) widgets[n.id] = MakeNode(n, Map(n.pos));

        Refresh();
    }

    TextMeshProUGUI MakeText(RectTransform parent, string s, float size, Vector2 pos)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1400f, size * 1.6f);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = s;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    EdgeWidget MakeEdge(MechaTreeSO.Dep dep, Vector2 a, Vector2 b)
    {
        Vector2 dir = (b - a).normalized;
        a += dir * (NodeRadius + 4f);
        b -= dir * (NodeRadius + 4f);
        float len = Mathf.Max(2f, Vector2.Distance(a, b));

        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(content, false);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = a;
        rt.sizeDelta = new Vector2(len, 7f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        var img = go.GetComponent<Image>();
        img.sprite = LineSprite;
        img.raycastTarget = false;

        var pgo = new GameObject("Pulse", typeof(RectTransform), typeof(Image));
        var prt = (RectTransform)pgo.transform;
        prt.SetParent(rt, false);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
        prt.sizeDelta = new Vector2(34f, 34f);
        var pimg = pgo.GetComponent<Image>();
        pimg.sprite = DotSprite;
        pimg.raycastTarget = false;
        pgo.SetActive(false);

        return new EdgeWidget { dep = dep, rt = rt, line = img, pulse = prt, pulseImg = pimg, len = len };
    }

    NodeWidget MakeNode(MechaTreeSO.Node n, Vector2 pos)
    {
        var go = new GameObject(n.mech.name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(content, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(NodeSize, NodeSize);
        var disc = go.GetComponent<Image>();
        disc.sprite = DiscSprite;

        var w = new NodeWidget { node = n, rt = rt, disc = disc };

        w.ring = Child(rt, "Ring", RingSprite, NodeSize);
        w.ring.type = Image.Type.Filled;
        w.ring.fillMethod = Image.FillMethod.Radial360;
        w.ring.fillOrigin = (int)Image.Origin360.Top;
        w.ring.fillClockwise = true;

        w.icon = Child(rt, "Icon", MechaTreeSO.IconOf(n), NodeSize * 0.58f);
        w.icon.preserveAspect = true;
        w.tint = MechaTreeSO.IconTint(n);
        w.icon.enabled = w.icon.sprite != null;

        w.flash = Child(rt, "Flash", DiscSprite, NodeSize);
        w.flash.color = new Color(1f, 1f, 1f, 0f);

        var tipImg = Child(rt, "Tip", DotSprite, 30f);
        w.tip = tipImg.rectTransform;
        w.tip.gameObject.SetActive(false);

        w.label = MakeText(rt, n.mech.name, 21f, new Vector2(0f, -NodeRadius - 18f));
        w.label.rectTransform.sizeDelta = new Vector2(240f, 30f);
        if (n.maxLevel > 1)
        {
            w.lvl = MakeText(rt, "", 18f, new Vector2(0f, -NodeRadius - 40f));
            w.lvl.rectTransform.sizeDelta = new Vector2(120f, 24f);
        }

        int id = n.id;
        go.GetComponent<Button>().onClick.AddListener(() => OnNodeClicked(id));
        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        return w;
    }

    static Image Child(RectTransform parent, string nam, Sprite s, float size)
    {
        var go = new GameObject(nam, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new Vector2(size, size);
        var img = go.GetComponent<Image>();
        img.sprite = s;
        img.raycastTarget = false;
        return img;
    }

    // ───────────────────────────────── refresh ─────────────────────────────────

    void Refresh()
    {
        if (slotsText != null)
            slotsText.text = "Ability slots left: " + Mathf.Max(0, MechaSuit.abilitiesLeft) + " / 3";

        foreach (var w in widgets.Values)
        {
            NodeState s = StateOf(w.node);
            Part live = LivePart(w.node);
            Color accent = Accent(w.node.mech.p.taip);

            float fill = live != null ? (float)DispLevel(live) / w.node.maxLevel : 1f;
            w.ring.fillAmount = live != null ? fill : 1f;

            switch (s)
            {
                case NodeState.Locked:
                    w.disc.color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
                    w.ring.color = new Color(1f, 1f, 1f, 0.1f);
                    w.icon.color = new Color(0.45f, 0.45f, 0.5f, 0.45f);
                    w.label.color = new Color(1f, 1f, 1f, 0.3f);
                    break;
                case NodeState.CapBlocked:
                    w.disc.color = new Color(0.14f, 0.11f, 0.11f, 0.95f);
                    w.ring.color = new Color(1f, 0.4f, 0.35f, 0.35f);
                    w.icon.color = new Color(0.6f, 0.5f, 0.5f, 0.6f);
                    w.label.color = new Color(1f, 0.7f, 0.65f, 0.5f);
                    break;
                case NodeState.Available:
                    w.disc.color = new Color(0.16f, 0.17f, 0.22f, 1f);
                    w.ring.color = Color.Lerp(accent, Color.white, 0.2f);
                    w.icon.color = Color.white;
                    w.label.color = new Color(1f, 1f, 1f, 0.85f);
                    break;
                case NodeState.Unlocked:
                case NodeState.Maxed:
                    w.disc.color = Color.Lerp(new Color(0.16f, 0.17f, 0.22f, 1f), accent, 0.18f);
                    w.ring.color = accent;
                    w.icon.color = Color.white;
                    w.label.color = new Color(1f, 1f, 1f, 0.95f);
                    break;
            }
            w.icon.color *= w.tint; // the switch re-sets a fresh state colour each pass

            if (w.lvl != null)
            {
                w.lvl.text = live != null ? "Lv " + DispLevel(live) + " / " + w.node.maxLevel : "Lv 0 / " + w.node.maxLevel;
                w.lvl.color = live != null ? Color.Lerp(accent, Color.white, 0.4f) : new Color(1f, 1f, 1f, 0.3f);
            }

            // gentle breathing pulse on reachable nodes only
            if (s == NodeState.Available && w.state != NodeState.Available)
            {
                LeanTween.cancel(w.rt.gameObject);
                w.rt.localScale = Vector3.one;
                LeanTween.scale(w.rt.gameObject, Vector3.one * 1.07f, 0.8f).setLoopPingPong().setEaseInOutSine().setIgnoreTimeScale(true);
            }
            else if (s != NodeState.Available && w.state == NodeState.Available)
            {
                LeanTween.cancel(w.rt.gameObject);
                w.rt.localScale = Vector3.one;
            }
            w.state = s;
        }

        foreach (var e in edges)
        {
            Color c = MechaTreeSO.GroupColor(e.dep.group);
            bool lit = Unlocked(e.dep.from);
            bool intoUnlocked = Unlocked(e.dep.to);
            c.a = lit ? (intoUnlocked ? 0.85f : 0.6f) : 0.14f;
            e.line.color = c;
        }
    }

    // ───────────────────────────────── clicks ─────────────────────────────────

    void OnNodeClicked(int id)
    {
        if (!widgets.TryGetValue(id, out var w)) return;
        switch (StateOf(w.node))
        {
            case NodeState.Locked:
                Shake(w);
                CM.Message(w.node.mech.name + " requires: " + tree.RequirementText(id));
                break;
            case NodeState.CapBlocked:
                Shake(w);
                CM.Message("No ability slots left for " + w.node.mech.name + " (max 3)!");
                break;
            case NodeState.Available:
                UnlockNode(w);
                break;
            case NodeState.Unlocked:
                UpgradeNode(w);
                break;
            case NodeState.Maxed:
                LeanTween.cancel(w.rt.gameObject);
                w.rt.localScale = Vector3.one;
                LeanTween.scale(w.rt.gameObject, Vector3.one * 1.06f, 0.12f).setLoopPingPong(1).setIgnoreTimeScale(true);
                break;
        }
    }

    void UnlockNode(NodeWidget w)
    {
        MechaSuit.m.AddParts(new[] { w.node.mech }, true);
        Part live = LivePart(w.node);
        if (live == null) { Shake(w); return; } // AddParts refused (cap raced etc.)
        if (live.level < 1) live.level = 1;

        // the satisfied AND-group's lines brighten and fire a pulse into the node
        int group = tree.GroupsInto(w.node.id).FirstOrDefault(g =>
            tree.DepsInto(w.node.id).Where(d => d.group == g).All(d => Unlocked(d.from)));
        foreach (var e in edges)
        {
            if (e.dep.to != w.node.id || e.dep.group != group || !Unlocked(e.dep.from)) continue;
            PulseEdge(e);
        }

        // node pop + white flash, slightly delayed so the pulses land first
        LeanTween.cancel(w.rt.gameObject);
        w.rt.localScale = Vector3.one;
        bool hasEdges = tree.DepsInto(w.node.id).Count > 0;
        float delay = hasEdges ? 0.32f : 0f;
        LeanTween.scale(w.rt.gameObject, Vector3.one * 1.22f, 0.22f).setDelay(delay).setEaseOutBack().setIgnoreTimeScale(true)
            .setOnComplete(() => LeanTween.scale(w.rt.gameObject, Vector3.one, 0.3f).setEaseOutCubic().setIgnoreTimeScale(true));
        LeanTween.value(w.flash.gameObject, 0.95f, 0f, 0.75f).setDelay(delay).setEaseOutCubic().setIgnoreTimeScale(true)
            .setOnUpdate((float f) => { if (w.flash != null) w.flash.color = new Color(1f, 1f, 1f, f); });

        // ring sweeps in from nothing
        float target = (float)DispLevel(live) / w.node.maxLevel;
        w.ring.fillAmount = 0f;
        LeanTween.value(w.ring.gameObject, 0f, target, 0.55f).setDelay(delay).setEaseOutCubic().setIgnoreTimeScale(true)
            .setOnUpdate((float f) => { if (w.ring != null) w.ring.fillAmount = f; });

        Refresh();
        w.ring.fillAmount = 0f; // Refresh snaps it; the tween above re-creeps it
    }

    void PulseEdge(EdgeWidget e)
    {
        Color group = MechaTreeSO.GroupColor(e.dep.group);
        e.pulse.gameObject.SetActive(true);
        e.pulse.anchoredPosition = Vector2.zero;
        e.pulseImg.color = Color.Lerp(group, Color.white, 0.5f);
        LeanTween.value(e.rt.gameObject, 0f, 1f, 0.35f).setEaseInQuad().setIgnoreTimeScale(true)
            .setOnUpdate((float f) =>
            {
                if (e.pulse == null) return;
                e.pulse.anchoredPosition = new Vector2(e.len * f, 0f);
                e.pulse.localScale = Vector3.one * (0.7f + 0.6f * Mathf.Sin(f * Mathf.PI));
            })
            .setOnComplete(() => { if (e.pulse != null) e.pulse.gameObject.SetActive(false); });
        // the line itself flares white-hot then settles to its lit colour
        Color hot = Color.Lerp(group, Color.white, 0.75f);
        Color settle = group; settle.a = 0.85f;
        LeanTween.value(e.line.gameObject, 0f, 1f, 0.8f).setIgnoreTimeScale(true)
            .setOnUpdate((float f) => { if (e.line != null) e.line.color = Color.Lerp(hot, settle, f); });
    }

    void UpgradeNode(NodeWidget w)
    {
        Part live = LivePart(w.node);
        if (live == null) { Refresh(); return; }
        if (live.level < 1) live.level = 1;
        int from = live.level;
        live.level++;
        MechaSuit.m.RefreshInteractions();
        CM.Message(w.node.mech.name + " upgraded to level " + live.level + "!", false);

        float f0 = (float)from / w.node.maxLevel;
        float f1 = (float)live.level / w.node.maxLevel;
        Color accent = Accent(w.node.mech.p.taip);

        // bright tip creeps around the outside of the part as the ring fills
        w.tip.gameObject.SetActive(true);
        w.tip.GetComponent<Image>().color = Color.Lerp(accent, Color.white, 0.6f);
        LeanTween.value(w.ring.gameObject, f0, f1, 0.85f).setEaseInOutCubic().setIgnoreTimeScale(true)
            .setOnUpdate((float f) =>
            {
                if (w.ring == null) return;
                w.ring.fillAmount = f;
                float ang = (90f - f * 360f) * Mathf.Deg2Rad;
                w.tip.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * RingRadius;
            })
            .setOnComplete(() =>
            {
                if (w.tip != null) w.tip.gameObject.SetActive(false);
                if (w.flash != null)
                    LeanTween.value(w.flash.gameObject, 0.55f, 0f, 0.45f).setIgnoreTimeScale(true)
                        .setOnUpdate((float f) => { if (w.flash != null) w.flash.color = new Color(1f, 1f, 1f, f); });
                Refresh();
            });

        LeanTween.cancel(w.rt.gameObject);
        w.rt.localScale = Vector3.one;
        LeanTween.scale(w.rt.gameObject, Vector3.one * 1.1f, 0.4f).setEaseInOutSine().setLoopPingPong(1).setIgnoreTimeScale(true);
    }

    void Shake(NodeWidget w)
    {
        LeanTween.cancel(w.rt.gameObject);
        Vector3 basePos = w.rt.anchoredPosition;
        LeanTween.value(w.rt.gameObject, 0f, 1f, 0.35f).setIgnoreTimeScale(true)
            .setOnUpdate((float f) =>
            {
                if (w.rt != null) w.rt.anchoredPosition = basePos + Vector3.right * (Mathf.Sin(f * 30f) * 6f * (1f - f));
            })
            .setOnComplete(() => { if (w.rt != null) w.rt.anchoredPosition = basePos; });
    }

    // ───────────────────────────────── procedural sprites ─────────────────────────────────

    static Sprite _disc, _ring, _line, _dot;
    static Sprite DiscSprite => _disc != null ? _disc : _disc = MakeCircle(64, 0f, 30f, 2f);
    static Sprite RingSprite => _ring != null ? _ring : _ring = MakeCircle(64, 26f, 30f, 1.5f);
    static Sprite DotSprite => _dot != null ? _dot : _dot = MakeGlowDot(32);

    static Sprite LineSprite
    {
        get
        {
            if (_line != null) return _line;
            const int h = 16;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float d = Mathf.Abs(y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return _line = Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    static Sprite MakeCircle(int size, float inner, float outer, float soft)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Vector2 c = new Vector2(size - 1, size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01((outer - d) / soft) * Mathf.Clamp01((d - inner) / soft + (inner <= 0f ? 1f : 0f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeGlowDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Vector2 c = new Vector2(size - 1, size - 1) * 0.5f;
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f)));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
