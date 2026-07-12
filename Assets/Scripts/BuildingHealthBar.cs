using UnityEngine;

/// <summary>
/// Hairline health bar hovering above a DAMAGED building — hidden at full hp, while a destroyed
/// ghost awaits drone repair, and mid-build/upgrade (no live physic). Dark backing, fill tinted
/// hurt-red → ally-mint by health (LifeScript's outline damage language), a bright-red "bleed"
/// strip that holds then sweeps down after each hit (delayed-damage trail), and a divider tick
/// at every 10 hp boundary. Entirely code-built; Building.Start attaches one per building.
/// </summary>
public class BuildingHealthBar : MonoBehaviour
{
    // hairline geometry (world units)
    const float Height = 0.05f;
    const float TickW = 0.018f;
    const float YPad = 0.16f;        // gap above the building's top edge
    const float HpPerTick = 10f;

    // bleed pacing: hold the just-lost sliver, then sweep it down to the live edge
    const float BleedHold = 0.35f;
    const float BleedSpeed = 1.1f;   // fill-fractions per second
    const float FadeSpeed = 8f;      // show/hide alpha per second
    const float MaxAlpha = 0.75f;    // overlay translucency — the bar never goes fully opaque

    // palette: fill sweeps red-ramp dark → ally mint; trail is the red-ramp top; backing near-black
    static readonly Color healthyCol = new(0.6f, 1f, 0.8f);
    static readonly Color hurtCol = new(0.682f, 0.02f, 0.184f);       // #ae052f
    static readonly Color bleedCol = new(0.894f, 0.027f, 0.243f);     // #e4073e
    static readonly Color backCol = new(0.047f, 0.047f, 0.086f, 0.8f);// #0c0c16

    // 1×1 white quads (centre / left-edge pivot) shared by every bar
    static Sprite quadMid, quadLeft;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { quadMid = null; quadLeft = null; }   // no-domain-reload: runtime textures die with play mode

    Building building;
    Transform vis;
    SpriteRenderer back, bleedSR, fillSR;
    SpriteRenderer[] ticks = System.Array.Empty<SpriteRenderer>();
    int layerID;
    int baseOrder;
    float width = 0.8f;
    float lastFrac = 1f;     // fill fraction currently shown (held through fade-out)
    float bleedFrac = 1f;    // where the trail's edge sits
    float bleedWait;
    float alpha;
    float ticksForMaxHp = -1f;
    bool trackValid;         // false across death/rebuild so the trail snaps instead of sweeping

    public static void Attach(Building b)
    {
        var hb = b.gameObject.AddComponent<BuildingHealthBar>();
        hb.building = b;
    }

    void LateUpdate()
    {
        LifeScript ls = building != null ? building.physic : null;
        bool live = building != null && building.builtYet && ls != null && !ls.hasDied
                    && ls.maxHp > 0f && ls.gameObject.activeInHierarchy;

        if (live)
        {
            float frac = Mathf.Clamp01(ls.hp / ls.maxHp);
            if (!trackValid) { lastFrac = frac; bleedFrac = frac; bleedWait = 0f; trackValid = true; }
            if (frac < lastFrac - 1e-4f) bleedWait = BleedHold;   // fresh damage — restart the hold
            lastFrac = frac;
            if (bleedFrac > frac)
            {
                if (bleedWait > 0f) bleedWait -= Time.deltaTime;
                else bleedFrac = Mathf.MoveTowards(bleedFrac, frac, BleedSpeed * Time.deltaTime);
            }
            else bleedFrac = frac;   // heals catch the trail up instantly
        }
        else trackValid = false;

        bool show = live && lastFrac < 0.999f;
        if (vis == null)
        {
            if (!show) return;
            BuildVisuals();
        }

        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, FadeSpeed * Time.deltaTime);
        if (alpha <= 0f)
        {
            if (vis.gameObject.activeSelf) vis.gameObject.SetActive(false);
            return;
        }
        if (!vis.gameObject.activeSelf) vis.gameObject.SetActive(true);

        if (live && !Mathf.Approximately(ls.maxHp, ticksForMaxHp)) LayoutTicks(ls.maxHp);

        float a = alpha * MaxAlpha;
        fillSR.transform.localScale = new Vector3(width * lastFrac, Height, 1f);
        bleedSR.transform.localScale = new Vector3(width * bleedFrac, Height, 1f);
        Color fc = Color.Lerp(hurtCol, healthyCol, lastFrac);
        fc.a = a;
        fillSR.color = fc;
        bleedSR.color = new Color(bleedCol.r, bleedCol.g, bleedCol.b, a);
        back.color = new Color(backCol.r, backCol.g, backCol.b, backCol.a * a);
        foreach (SpriteRenderer t in ticks)
        {
            if (t != null) t.color = new Color(backCol.r, backCol.g, backCol.b, 0.95f * a);
        }
    }

    void BuildVisuals()
    {
        width = Mathf.Max(0.55f, building.size.x * 0.8f);
        // Anchor to the non-rotating root: hasExtraParent buildings keep this script on an
        // aiming child (same rule as the energy status icon).
        Transform anchor = (building.hasExtraParent && building.transform.parent != null)
            ? building.transform.parent : building.transform;
        var root = new GameObject("HealthBar");
        root.transform.SetParent(anchor, false);
        root.transform.localPosition = new Vector3(0f, building.size.y * 0.5f + YPad, 0f);
        vis = root.transform;

        // Reads as UI: the topmost sorting layer, above every world sprite and "Power Ups" VFX
        // (still under screen-space HUD canvases, which render after the world regardless).
        layerID = SortingLayer.NameToID("UI");
        baseOrder = 40;

        back = MakeStrip("Back", Quad(ref quadMid, new Vector2(0.5f, 0.5f)), baseOrder);
        back.transform.localScale = new Vector3(width, Height, 1f);
        bleedSR = MakeStrip("Bleed", Quad(ref quadLeft, new Vector2(0f, 0.5f)), baseOrder + 1);
        bleedSR.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
        fillSR = MakeStrip("Fill", Quad(ref quadLeft, new Vector2(0f, 0.5f)), baseOrder + 2);
        fillSR.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0f);

        alpha = 0f;
        vis.gameObject.SetActive(false);
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

    // A divider at every 10 hp boundary strictly inside the bar (30 hp → ticks at 10 and 20).
    void LayoutTicks(float maxHp)
    {
        ticksForMaxHp = maxHp;
        foreach (SpriteRenderer t in ticks)
        {
            if (t != null) Destroy(t.gameObject);
        }
        int n = Mathf.Max(0, Mathf.FloorToInt((maxHp - 0.01f) / HpPerTick));
        ticks = new SpriteRenderer[n];
        for (int i = 0; i < n; i++)
        {
            SpriteRenderer t = MakeStrip("Tick", Quad(ref quadMid, new Vector2(0.5f, 0.5f)), baseOrder + 3);
            t.transform.localPosition = new Vector3(-width * 0.5f + width * (HpPerTick * (i + 1) / maxHp), 0f, 0f);
            t.transform.localScale = new Vector3(TickW, Height, 1f);
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
