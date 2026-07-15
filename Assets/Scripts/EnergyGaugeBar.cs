using UnityEngine;

/// <summary>
/// Vertical energy gauge on a building's RIGHT edge, visual sibling of
/// <see cref="BuildingHealthBar"/> (same code-built quad strips and palette). NO easing —
/// visibility flips instantly and the fill is drawn raw from this frame's sample.
/// The bar's length is proportional to its FRACTIONAL energy span (a 0.6-demand Pelter gets a
/// 0.6-unit bar, so a fully covered building always shows a FULL bar), capped at the building's
/// height, with a divider tick at every whole-energy boundary. Geometry NEVER changes
/// frame-to-frame — it re-lays-out only when the span genuinely changes (an upgrade, a battery
/// added to a capacitor). Fill and colour move every frame.
/// Two concrete flavours below: ThrottleBar (consumers) and SurgeBar (grid nodes).
/// </summary>
public abstract class EnergyGaugeBar : MonoBehaviour
{
    // geometry (world units) — deliberately chunkier than the hairline health bar so the
    // bar's extent (and an EMPTY bar) reads at a glance
    const float Width = 0.06f;       // bar thickness
    const float TickH = 0.02f;       // divider tick height
    const float XPad = 0.16f;        // gap right of the building's right edge
    const float UnitH = 0.3f;        // bar length per 1 energy of span, before the height cap
    const float MinH = 0.18f;        // readability floor for tiny spans (e.g. the 0.25/s Force Field)
    const float FrameW = 0.012f;     // hairline slot edge — makes an empty bar visible

    const float MaxAlpha = 0.75f;    // overlay translucency — never fully opaque

    // palette — identical language to the health bar: starved red → healthy mint
    static readonly Color healthyCol = new(0.6f, 1f, 0.8f);
    static readonly Color hurtCol = new(0.682f, 0.02f, 0.184f);        // #ae052f
    static readonly Color backCol = new(0.047f, 0.047f, 0.086f, 0.9f); // #0c0c16
    static readonly Color frameCol = new(0.13f, 0.14f, 0.22f, 0.85f);  // dark slot edge, a shade above the back

    // 1×1 white quads (centre / bottom-edge pivot) shared by every gauge
    static Sprite quadMid, quadBottom;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { quadMid = null; quadBottom = null; }   // no-domain-reload: runtime textures die with play mode

    protected Building building;
    Transform vis;
    SpriteRenderer frame, back, fillSR;
    SpriteRenderer[] ticks = System.Array.Empty<SpriteRenderer>();
    int layerID;
    int baseOrder;
    float barH;
    float spanShown = -1f;

    /// <summary>Sample the gauge: the bar's total energy span (fractional — sets its length) and
    /// the covered fraction 0..1 (drives both fill height and the red→mint colour ramp; 1 = full
    /// bar, fully covered). Return false to hide.</summary>
    protected abstract bool Sample(out float span, out float frac);

    bool Live()
    {
        return building != null && building.builtYet
            && building.physic != null && !building.physic.hasDied
            && building.physic.gameObject.activeInHierarchy;
    }

    void LateUpdate()
    {
        float span = spanShown, frac = 0f;
        bool show = Live() && Sample(out span, out frac);

        if (vis == null)
        {
            if (!show) return;
            BuildVisuals();
        }

        // No easing: visibility flips instantly, the fill is exactly this frame's sample.
        if (vis.gameObject.activeSelf != show) vis.gameObject.SetActive(show);
        if (!show) return;

        if (Mathf.Abs(span - spanShown) > 0.01f) LayoutBar(span);

        fillSR.transform.localScale = new Vector3(Width, barH * Mathf.Clamp01(frac), 1f);
        Color fc = Color.Lerp(hurtCol, healthyCol, Mathf.Clamp01(frac));
        fc.a = MaxAlpha;
        fillSR.color = fc;
        back.color = new Color(backCol.r, backCol.g, backCol.b, backCol.a * MaxAlpha);
        // The outline reddens as the bar empties, so a drained slot shouts instead of fading away.
        Color fr = Color.Lerp(hurtCol, frameCol, Mathf.Clamp01(frac));
        fr.a = frameCol.a * MaxAlpha + 0.2f * (1f - Mathf.Clamp01(frac));
        frame.color = fr;
        foreach (SpriteRenderer t in ticks)
        {
            if (t != null) t.color = new Color(backCol.r, backCol.g, backCol.b, 0.95f * MaxAlpha);
        }
    }

    void BuildVisuals()
    {
        // Anchor to the non-rotating root (same rule as the health bar / energy icon), sitting
        // just right of the building's right edge with the bar's base at the bottom edge.
        Transform anchor = (building.hasExtraParent && building.transform.parent != null)
            ? building.transform.parent : building.transform;
        var root = new GameObject(GetType().Name);
        root.transform.SetParent(anchor, false);
        root.transform.localPosition = new Vector3(building.size.x * 0.5f + XPad, -building.size.y * 0.5f, 0f);
        vis = root.transform;

        layerID = SortingLayer.NameToID("UI");
        baseOrder = 44;   // above the health bar's 40-43 block

        frame = MakeStrip("Frame", Quad(ref quadBottom, new Vector2(0.5f, 0f)), baseOrder);
        back = MakeStrip("Back", Quad(ref quadBottom, new Vector2(0.5f, 0f)), baseOrder + 1);
        fillSR = MakeStrip("Fill", Quad(ref quadBottom, new Vector2(0.5f, 0f)), baseOrder + 2);
    }

    SpriteRenderer MakeStrip(string nam, Sprite spr, int order)
    {
        var go = new GameObject(nam);
        go.transform.SetParent(vis, false);
        var s = go.AddComponent<SpriteRenderer>();
        s.sprite = spr;
        s.sortingLayerID = layerID;
        s.sortingOrder = order;
        return s;
    }

    // Bar length ∝ the fractional energy span (UnitH per energy), clamped between a readability
    // floor and the building's own height. A divider tick at every whole-energy boundary
    // strictly inside the bar (span 2.5 → ticks at 1 and 2).
    void LayoutBar(float span)
    {
        spanShown = span;
        barH = Mathf.Clamp(span * UnitH, MinH, building.size.y);
        back.transform.localScale = new Vector3(Width, barH, 1f);
        frame.transform.localScale = new Vector3(Width + 2f * FrameW, barH + 2f * FrameW, 1f);
        frame.transform.localPosition = new Vector3(0f, -FrameW, 0f);

        foreach (SpriteRenderer t in ticks)
        {
            if (t != null) Destroy(t.gameObject);
        }
        int n = Mathf.Max(0, Mathf.CeilToInt(span - 0.01f) - 1);
        ticks = new SpriteRenderer[n];
        for (int i = 0; i < n; i++)
        {
            SpriteRenderer t = MakeStrip("Tick", Quad(ref quadMid, new Vector2(0.5f, 0.5f)), baseOrder + 3);
            t.transform.localPosition = new Vector3(0f, barH * (i + 1) / span, 0f);
            t.transform.localScale = new Vector3(Width, TickH, 1f);
            ticks[i] = t;
        }
    }

    static Sprite Quad(ref Sprite cache, Vector2 pivot)
    {
        if (cache != null) return cache;
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        cache = Sprite.Create(t, new Rect(0, 0, 1, 1), pivot, 1f);
        return cache;
    }

    void OnDestroy()
    {
        if (GS.qutting) return;
        if (vis != null) Destroy(vis.gameObject);
    }
}

/// <summary>
/// Consumer gauge: ALWAYS visible on a built energy consumer. Bar length =
/// <see cref="Building.PeakEnergyDemand"/> (fractional); fill = how much of that demand the
/// grid can actually deliver to THIS building within a second — the contention-aware
/// fair-share offer (surge credit + this building's slice of the rate), NOT the raw grid
/// rate, so a starved building genuinely reads low. Full mint bar = fully covered.
/// </summary>
public class ThrottleBar : EnergyGaugeBar
{
    public static void Attach(Building b)
    {
        var bar = b.gameObject.AddComponent<ThrottleBar>();
        bar.building = b;
    }

    protected override bool Sample(out float span, out float frac)
    {
        float demand = building.PeakEnergyDemand;
        span = demand;
        if (demand <= 0f) { frac = 0f; return false; }

        // PeekMaxDraw(1s) = energy this building could obtain over a second at CURRENT
        // contention: its fair-share slice of the sustained rate plus available surge credit.
        // Side-effect-free peek — polling MaxDrawThisFrame would register as a consumer and
        // shrink everyone's fair-share slices.
        float avail = building.Power.PeekMaxDraw(1f);
        frac = Mathf.Min(avail, demand) / demand;
        return true;
    }
}

/// <summary>
/// Grid-node gauge (pylons, pads, hubs): strictly the instabuffer, not the rate. For a pylon
/// the pool is the AGGREGATE of every surge source it can reach (its own pool + attached
/// capacitor nodes); for pads/hubs it's the slotted batteries' pools. Always visible while
/// the node has any surge capacity — full mint bar = pool fully charged.
/// (CapacitorNode shows no bar: its battery's own art animates the insta state.)
/// </summary>
public class SurgeBar : EnergyGaugeBar
{
    public static void Attach(Building b)
    {
        var bar = b.gameObject.AddComponent<SurgeBar>();
        bar.building = b;
    }

    protected override bool Sample(out float span, out float frac)
    {
        float now, max;
        if (building is EnergyPylon pylon) pylon.GetSurgeState(out now, out max);
        else if (building is EnergyPad pad) { now = pad.SurgeNow; max = pad.SurgeMax; }
        else { span = 1f; frac = 0f; return false; }

        span = max;
        if (max <= 0f) { frac = 0f; return false; }

        frac = now / max;
        return true;
    }
}
