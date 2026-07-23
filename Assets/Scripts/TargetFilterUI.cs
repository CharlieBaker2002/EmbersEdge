using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Custom target-filter builder for <see cref="TargetPriority"/> (Preference → Custom).
/// A code-built paused overlay (MechaTreeUI pattern): every spawnable enemy type appears as a
/// toggleable card — icon read straight off the MarauderSO prefab — and ticked types become the
/// tower's preferred targets. Edits TargetPriority.customKeys in place; Esc or Done closes.
/// </summary>
public class TargetFilterUI : MonoBehaviour
{
    public static TargetFilterUI i;

    TargetPriority owner;
    GameObject root;
    CanvasGroup cg;
    TextMeshProUGUI countText;
    int pauseID;
    bool open;
    Action escHandler;

    class Card
    {
        public string key;
        public RectTransform rt;
        public Image disc, ring, icon;
        public TextMeshProUGUI label;
    }

    readonly List<Card> cards = new List<Card>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (i != null) return;
        var go = new GameObject("TargetFilterUI");
        DontDestroyOnLoad(go);
        i = go.AddComponent<TargetFilterUI>();
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
        // owner demolished / scene torn down while open — don't strand a paused overlay
        if (open && (root == null || owner == null)) Close();
    }

    public static void Open(TargetPriority prio)
    {
        if (i == null) Bootstrap();
        i.DoOpen(prio);
    }

    void DoOpen(TargetPriority prio)
    {
        if (UIManager.i == null || prio == null) return;
        if (open) Close();
        owner = prio;
        open = true;
        pauseID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
        EscapeRouter.i?.Push(escHandler);
        Build();
        cg.alpha = 0f;
        LeanTween.value(root, 0f, 1f, 0.2f).setOnUpdate((float f) => { if (cg != null) cg.alpha = f; }).setIgnoreTimeScale(true);
    }

    void Close()
    {
        if (!open) return;
        open = false;
        owner = null;
        cards.Clear();
        if (SpawnManager.instance != null) SpawnManager.instance.CancelTS(pauseID);
        if (EscapeRouter.i != null) EscapeRouter.i.Remove(escHandler);
        if (root == null) return;
        GameObject dying = root;
        root = null;
        LeanTween.cancel(dying);
        LeanTween.value(dying, cg.alpha, 0f, 0.15f)
            .setOnUpdate((float f) => { var c = dying.GetComponent<CanvasGroup>(); if (c != null) c.alpha = f; })
            .setOnComplete(() => Destroy(dying))
            .setIgnoreTimeScale(true);
    }

    // ─────────────────────────────── roster ───────────────────────────────

    // MarauderSOs are assets — cache survives no-domain-reload plays harmlessly
    static readonly Dictionary<MarauderSO, Sprite> iconCache = new Dictionary<MarauderSO, Sprite>();

    static List<MarauderSO> Roster()
    {
        var seen = new HashSet<string>();
        var list = new List<MarauderSO>();
        void Add(IEnumerable<MarauderSO> sos)
        {
            if (sos == null) return;
            foreach (MarauderSO so in sos)
                if (so != null && so.prefab != null && !so.isBuilding && seen.Add(so.prefab.name))
                    list.Add(so);
        }

        SpawnManager sm = SpawnManager.instance;
        if (sm != null)
        {
            Add(sm.marauders);
            Add(sm.E2SOs);
            Add(sm.E3SOs);
        }
        return list;
    }

    static Sprite IconOf(MarauderSO so)
    {
        if (iconCache.TryGetValue(so, out Sprite s)) return s;
        var sr = so.prefab.GetComponentInChildren<SpriteRenderer>(true);
        s = sr != null ? sr.sprite : null;
        iconCache[so] = s;
        return s;
    }

    // ─────────────────────────────── build ───────────────────────────────

    void Build()
    {
        root = new GameObject("TargetFilter", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        var rootRT = (RectTransform)root.transform;
        rootRT.SetParent(UIManager.i.canvas, false);
        rootRT.SetAsLastSibling();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;
        cg = root.GetComponent<CanvasGroup>();
        root.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.05f, 0.92f);

        Rect canvasRect = UIManager.i.canvas.rect;

        var title = MakeText(rootRT, "CUSTOM TARGET FILTER", 40f, new Vector2(0f, canvasRect.height * 0.5f - 50f));
        title.color = Color.Lerp(GS.ColFromEra(), Color.white, 0.45f);
        title.fontStyle = FontStyles.Bold;

        var sub = MakeText(rootRT, "Tick the enemies this tower hunts first — everything else is only hit when none of them are in range", 20f, new Vector2(0f, canvasRect.height * 0.5f - 86f));
        sub.color = new Color(1f, 1f, 1f, 0.5f);

        countText = MakeText(rootRT, "", 22f, new Vector2(0f, canvasRect.height * 0.5f - 118f));
        countText.color = new Color(1f, 1f, 1f, 0.7f);

        List<MarauderSO> roster = Roster();
        if (roster.Count == 0)
        {
            var none = MakeText(rootRT, "No enemy roster found.", 26f, Vector2.zero);
            none.color = new Color(1f, 0.7f, 0.65f, 0.8f);
        }

        int nCol = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(roster.Count * 1.6f)), 3, 7);
        int nRow = Mathf.Max(1, Mathf.CeilToInt(roster.Count / (float)nCol));
        float cellX = 150f, cellY = 165f;
        float scale = Mathf.Min(1f,
            (canvasRect.width - 160f) / (nCol * cellX),
            (canvasRect.height - 320f) / (nRow * cellY));
        cellX *= scale;
        cellY *= scale;

        var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(rootRT, false);
        content.anchoredPosition = new Vector2(0f, -20f);

        for (int k = 0; k < roster.Count; k++)
        {
            int col = k % nCol, row = k / nCol;
            Vector2 pos = new Vector2((col - (nCol - 1) * 0.5f) * cellX, ((nRow - 1) * 0.5f - row) * cellY);
            cards.Add(MakeCard(content, roster[k], pos, cellX));
        }

        float by = -canvasRect.height * 0.5f + 60f;
        MakeButton(rootRT, "All", new Vector2(-170f, by), () => SetAll(true));
        MakeButton(rootRT, "None", new Vector2(0f, by), () => SetAll(false));
        MakeButton(rootRT, "Done", new Vector2(170f, by), Close, true);

        Repaint();
    }

    Card MakeCard(RectTransform parent, MarauderSO so, Vector2 pos, float cell)
    {
        string key = so.prefab.name;
        var go = new GameObject(key, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        float d = cell * 0.72f;
        rt.sizeDelta = new Vector2(d, d);
        var disc = go.GetComponent<Image>();
        disc.sprite = DiscSprite;

        var card = new Card { key = key, rt = rt, disc = disc };

        card.ring = Child(rt, "Ring", RingSprite, d);
        card.icon = Child(rt, "Icon", IconOf(so), d * 0.62f);
        card.icon.preserveAspect = true;
        card.icon.enabled = card.icon.sprite != null;
        card.label = MakeText(rt, key, Mathf.Max(14f, 18f * (cell / 150f)), new Vector2(0f, -d * 0.5f - 14f));
        card.label.rectTransform.sizeDelta = new Vector2(cell, 24f);

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(() => Toggle(card));
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        return card;
    }

    // ─────────────────────────────── interaction ───────────────────────────────

    void Toggle(Card c)
    {
        if (owner == null) return;
        if (!owner.customKeys.Add(c.key)) owner.customKeys.Remove(c.key);
        Repaint();
        LeanTween.cancel(c.rt.gameObject);
        c.rt.localScale = Vector3.one;
        LeanTween.scale(c.rt.gameObject, Vector3.one * 1.1f, 0.1f).setLoopPingPong(1).setIgnoreTimeScale(true);
    }

    void SetAll(bool on)
    {
        if (owner == null) return;
        owner.customKeys.Clear();
        if (on)
            foreach (Card c in cards)
                owner.customKeys.Add(c.key);
        Repaint();
    }

    void Repaint()
    {
        if (owner == null) return;
        Color accent = Color.Lerp(GS.ColFromEra(), Color.white, 0.25f);
        foreach (Card c in cards)
        {
            bool on = owner.customKeys.Contains(c.key);
            c.disc.color = on ? Color.Lerp(new Color(0.16f, 0.17f, 0.22f, 1f), accent, 0.25f) : new Color(0.10f, 0.10f, 0.13f, 0.95f);
            c.ring.color = on ? accent : new Color(1f, 1f, 1f, 0.12f);
            c.icon.color = on ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.55f);
            c.label.color = on ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
        }

        countText.text = owner.customKeys.Count == 0
            ? "Nothing ticked — the tower just shoots whatever is closest"
            : owner.customKeys.Count + " enemy type" + (owner.customKeys.Count == 1 ? "" : "s") + " preferred";
    }

    // ─────────────────────────────── widgets ───────────────────────────────

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

    void MakeButton(RectTransform parent, string label, Vector2 pos, Action act, bool accent = false)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(140f, 46f);
        var img = go.GetComponent<Image>();
        img.color = accent
            ? Color.Lerp(GS.ColFromEra(), Color.white, 0.35f)
            : new Color(1f, 1f, 1f, 0.12f);
        var t = MakeText(rt, label, 24f, Vector2.zero);
        t.color = accent ? new Color(0.05f, 0.05f, 0.08f, 1f) : new Color(1f, 1f, 1f, 0.85f);
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(() => act());
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
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

    // ─────────────────────────────── procedural sprites ───────────────────────────────

    static Sprite _disc, _ring;
    static Sprite DiscSprite => _disc != null ? _disc : _disc = MakeCircle(64, 0f, 30f, 2f);
    static Sprite RingSprite => _ring != null ? _ring : _ring = MakeCircle(64, 26f, 30f, 1.5f);

    static Sprite MakeCircle(int size, float inner, float outer, float soft)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
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
}
