using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Code-built resource readout replacing the four "12 / 40" texts: one row per element plus an
/// ember row. Each row is ONE fused slot — a thin on-person strip riding directly on top of the
/// thicker home-storage bar, sharing a crisp near-black frame so they read as a single element.
/// Fills are gradient-shaded (bright crown, dark base) with a bright 1px leading edge; opaque
/// hairline ticks mark absolute amounts (one tick every 50 white / 10 green / 4 blue / 1 red
/// orbs, 10 ember). The ember row's thin strip is the run haul (EmberStore.pending — only banked
/// if you make it home) over the ember sitting in the base's stores, tinted to the CURRENT ERA's
/// colour (re-sampled every frame so era transitions just work).
/// Built at runtime under the existing "Resources" panel (scene YAML stays untouched — the
/// editor clobbers hand-edited refs); the old TMP counters are disabled, and a compact numeric
/// label per row keeps exact counts.
/// </summary>
public class ResourceBarsUI : MonoBehaviour
{
    // geometry (canvas px, panel is 300 wide anchored to the screen's right edge)
    const float BarW = 265f;
    const float ThickH = 22f;
    const float ThinH = 9f;
    const float Divide = 2f;      // hairline between the two strips of a slot
    const float Border = 2f;      // frame thickness around the slot
    const float RowSpacing = 86f;
    const float TickW = 2f;
    const float RightPad = 20f;
    const float LabelH = 30f;

    static readonly Color frameCol = new(0.016f, 0.016f, 0.04f, 1f);    // near-black, fully opaque — the crisp edge
    static readonly Color backCol = new(0.059f, 0.059f, 0.11f, 1f);     // slot interior, a shade above the frame

    /// <summary>Orbs (or ember) per tick — white, green, blue, red, ember.</summary>
    static readonly int[] tickStep = { 50, 10, 4, 1, 10 };

    // shared 1px-wide vertical-gradient sprite (bright crown → dark base), tinted per fill
    static Sprite gradSprite;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { gradSprite = null; }   // no-domain-reload: runtime textures die with play mode

    ResourceManager rm;
    Row[] rows;

    class Row
    {
        public Image thickFill, thinFill, thickCap, thinCap;
        public TextMeshProUGUI label;
        public Transform thickTicks, thinTicks;
        public int thickTicksShown = -1, thinTicksShown = -1;
    }

    public static void Attach(ResourceManager rm)
    {
        RectTransform panel = rm.resourceUIs[0].rectTransform.parent as RectTransform;
        var ui = new GameObject("ResourceBars", typeof(RectTransform)).AddComponent<ResourceBarsUI>();
        ui.rm = rm;
        var rt = (RectTransform)ui.transform;
        rt.SetParent(panel, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-RightPad, -30f);
        rt.sizeDelta = new Vector2(BarW, RowSpacing * 5f);

        // the bars ARE the readout now — the old text counters retire
        foreach (TextMeshProUGUI t in rm.resourceUIs) t.gameObject.SetActive(false);

        TMP_FontAsset font = rm.resourceUIs[0].font;
        ui.rows = new Row[5];
        for (int i = 0; i < 5; i++) ui.rows[i] = ui.BuildRow(i, font);
    }

    static Color ElementCol(int i)
    {
        if (i == 4) return GS.ColFromEra();   // ember wears the era's colour
        ColourSO so = UIManager.i.colSO;
        return i switch { 0 => so.StandardWhite, 1 => so.StandardGreen, 2 => so.StandardBlue, _ => so.StandardRed };
    }

    Row BuildRow(int i, TMP_FontAsset font)
    {
        var row = new Row();
        RectTransform root = MakeRect($"Row{i}", (RectTransform)transform, BarW, RowSpacing);
        root.anchoredPosition = new Vector2(0f, -RowSpacing * i - (i == 4 ? 14f : 0f));

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

        row.thickFill = MakeFill(thick, ThickH, out row.thickCap);
        row.thinFill = MakeFill(thin, ThinH, out row.thinCap);
        row.thickTicks = MakeRect("StoreTicks", thick, BarW, ThickH);
        row.thinTicks = MakeRect("HeldTicks", thin, BarW, ThinH);

        var lgo = new GameObject("Label", typeof(RectTransform));
        row.label = lgo.AddComponent<TextMeshProUGUI>();
        row.label.font = font;
        row.label.fontSize = 24f;
        row.label.fontStyle = FontStyles.Bold;
        row.label.alignment = TextAlignmentOptions.BottomRight;
        row.label.characterSpacing = 2f;
        row.label.raycastTarget = false;
        var lrt = (RectTransform)lgo.transform;
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(1f, 0f);
        lrt.anchoredPosition = new Vector2(0f, innerH + 2f * Border + 3f);
        lrt.sizeDelta = new Vector2(BarW, LabelH);
        return row;
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

    // Fill = gradient-shaded bar growing from the left + a 1px bright cap at its leading edge.
    static Image MakeFill(RectTransform strip, float h, out Image cap)
    {
        var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
        fill.sprite = Grad();
        fill.raycastTarget = false;
        var rt = fill.rectTransform;
        rt.SetParent(strip, false);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 0f);

        cap = new GameObject("Cap", typeof(RectTransform)).AddComponent<Image>();
        cap.raycastTarget = false;
        var crt = cap.rectTransform;
        crt.SetParent(strip, false);
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(2f, 0f);
        return fill;
    }

    // Vertical sheen baked into a tiny texture: bright crown, true colour, darker base —
    // tinting the Image gives every element the same shaded, non-flat read.
    static Sprite Grad()
    {
        if (gradSprite != null) return gradSprite;
        var t = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        t.SetPixel(0, 3, new Color(1.35f, 1.35f, 1.35f, 1f));
        t.SetPixel(0, 2, Color.white);
        t.SetPixel(0, 1, new Color(0.85f, 0.85f, 0.85f, 1f));
        t.SetPixel(0, 0, new Color(0.6f, 0.6f, 0.6f, 1f));
        t.Apply();
        gradSprite = Sprite.Create(t, new Rect(0, 0, 1, 4), new Vector2(0.5f, 0.5f), 4f);
        return gradSprite;
    }

    // Opaque hairlines through the strip at every whole-step boundary strictly inside the bar.
    static void LayoutTicks(Transform holder, ref int shown, int max, int step)
    {
        int n = max > 0 && step > 0 ? Mathf.Max(0, Mathf.CeilToInt(max / (float)step) - 1) : 0;
        if (n == shown) return;
        shown = n;
        for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
        for (int i = 1; i <= n; i++)
        {
            var t = new GameObject("Tick", typeof(RectTransform)).AddComponent<Image>();
            t.color = frameCol;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.SetParent(holder, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(BarW * (i * step) / max, 0f);
            rt.sizeDelta = new Vector2(TickW, 0f);
        }
    }

    void SetStrip(Row row, bool thin, int now, int max, int step, Color col)
    {
        Image fill = thin ? row.thinFill : row.thickFill;
        Image cap = thin ? row.thinCap : row.thickCap;
        float frac = max > 0 ? Mathf.Clamp01(now / (float)max) : 0f;
        float w = BarW * frac;
        fill.rectTransform.sizeDelta = new Vector2(w, 0f);
        fill.color = col;
        cap.enabled = frac > 0.002f && frac < 0.998f;
        cap.rectTransform.anchoredPosition = new Vector2(w, 0f);
        cap.color = Color.Lerp(col, Color.white, 0.65f);
        if (thin) LayoutTicks(row.thinTicks, ref row.thinTicksShown, max, step);
        else LayoutTicks(row.thickTicks, ref row.thickTicksShown, max, step);
    }

    void LateUpdate()
    {
        if (rm == null) return;
        for (int i = 0; i < 5; i++)
        {
            Row row = rows[i];
            Color col = ElementCol(i);
            Color thinCol = Color.Lerp(col, Color.white, 0.3f);
            int step = tickStep[i];

            int held, maxHeld, stored, cap;
            if (i < 4)
            {
                held = rm.held[i]; maxHeld = rm.MaxHeldToday(i);
                stored = rm.orbs[i]; cap = rm.orbCaps[i];
            }
            else
            {
                // ember: run haul rides on top of what's actually sitting in the base's stores
                held = EmberStore.pending;
                stored = 0; cap = 0;
                if (EnergyManager.i != null)
                {
                    foreach (EmberStoreBuilding s in EnergyManager.i.emberStores)
                    {
                        if (s == null || s.connect == null) continue;
                        stored += Mathf.Max(0, s.connect.ember);
                        cap += s.connect.maxEmber;
                    }
                }
                maxHeld = Mathf.Max(cap, 1);
            }

            SetStrip(row, true, held, maxHeld, step, thinCol);
            SetStrip(row, false, stored, cap, step, col);
            row.label.text = held > 0 ? $"{held}  +  {stored}/{cap}" : $"{stored}/{cap}";
            row.label.color = Color.Lerp(col, Color.white, 0.45f);
        }
    }
}
