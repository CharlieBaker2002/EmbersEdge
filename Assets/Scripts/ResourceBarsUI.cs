using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ember readout: ONE fused slot — a thin on-person strip (EmberStore.pending, the run haul,
/// only banked if you make it home) riding on the thicker bar of ember sitting in the base's
/// stores, tinted to the CURRENT ERA's colour (re-sampled every frame so era transitions just
/// work), with opaque hairline ticks every <see cref="tickStep"/> ember and a compact numeric label.
///
/// AUTHORED, NOT BUILT: every piece is a scene child you can see and tune in the inspector —
/// run Tools/Resource Bar Kit to (re)create and wire it under the UI/Resources panel. This script
/// only DRIVES what's authored: fill widths, the leading-edge cap, tick count/positions, label
/// text and colour. Bar width is read from the authored strips, so resizing them in the inspector
/// just works. Ticks are the one thing that can't be authored (the count follows the live ember
/// capacity) — they're cloned from the inactive Tick template child, so their look stays authored.
/// </summary>
public class ResourceBarsUI : MonoBehaviour
{
    [Header("Authored refs — Tools/Resource Bar Kit wires these")]
    [Tooltip("Thick bar: ember banked in the base's stores.")]
    [SerializeField] Image storeFill;
    [SerializeField] Image storeCap;
    [SerializeField] RectTransform storeTicks;
    [Tooltip("Thin strip: the run haul riding on the player (EmberStore.pending).")]
    [SerializeField] Image heldFill;
    [SerializeField] Image heldCap;
    [SerializeField] RectTransform heldTicks;
    [Tooltip("Inactive hairline cloned per tick — tune its colour/width here.")]
    [SerializeField] Image tickTemplate;
    [SerializeField] TextMeshProUGUI label;

    [Header("Tuning")]
    [Tooltip("Ember per hairline tick.")]
    [SerializeField] int tickStep = 10;
    [Tooltip("How far the thin (haul) strip is lightened from the era colour.")]
    [SerializeField, Range(0f, 1f)] float heldTint = 0.3f;
    [Tooltip("How far the bright leading-edge cap is lightened.")]
    [SerializeField, Range(0f, 1f)] float capTint = 0.65f;
    [Tooltip("How far the numeric label is lightened.")]
    [SerializeField, Range(0f, 1f)] float labelTint = 0.45f;

    readonly List<RectTransform> storeTickPool = new();
    readonly List<RectTransform> heldTickPool = new();
    int storeTicksShown = -1, heldTicksShown = -1;

    void Awake()
    {
        if (storeFill == null || storeCap == null || heldFill == null || heldCap == null || label == null)
        {
            Debug.LogError($"[ResourceBarsUI] '{name}' is missing authored refs — run Tools/Resource Bar Kit. Disabling.", this);
            enabled = false;
        }
    }

    void LateUpdate()
    {
        Color col = GS.ColFromEra();   // ember wears the era's colour
        Color thinCol = Color.Lerp(col, Color.white, heldTint);

        // ember: run haul rides on top of what's actually sitting in the base's stores
        int held = EmberStore.pending;
        int stored = 0, cap = 0;
        if (EnergyManager.i != null)
        {
            foreach (EmberStoreBuilding s in EnergyManager.i.emberStores)
            {
                if (s == null || s.connect == null) continue;
                stored += Mathf.Max(0, s.connect.ember);
                cap += s.connect.maxEmber;
            }
        }
        int maxHeld = Mathf.Max(cap, 1);

        SetStrip(heldFill, heldCap, heldTicks, heldTickPool, ref heldTicksShown, held, maxHeld, thinCol);
        SetStrip(storeFill, storeCap, storeTicks, storeTickPool, ref storeTicksShown, stored, cap, col);
        label.text = held > 0 ? $"{held}  +  {stored}/{cap}" : $"{stored}/{cap}";
        label.color = Color.Lerp(col, Color.white, labelTint);
    }

    /// <summary>Bar width comes from the authored strip the fill lives in, so the whole readout
    /// follows whatever size the panel is given in the inspector.</summary>
    static float StripWidth(Image fill)
    {
        RectTransform strip = fill.rectTransform.parent as RectTransform;
        return strip != null ? strip.rect.width : fill.rectTransform.rect.width;
    }

    void SetStrip(Image fill, Image cap, RectTransform ticks, List<RectTransform> pool, ref int shown,
                  int now, int max, Color col)
    {
        float barW = StripWidth(fill);
        float frac = max > 0 ? Mathf.Clamp01(now / (float)max) : 0f;
        float w = barW * frac;
        fill.rectTransform.sizeDelta = new Vector2(w, 0f);
        fill.color = col;
        cap.enabled = frac > 0.002f && frac < 0.998f;
        cap.rectTransform.anchoredPosition = new Vector2(w, 0f);
        cap.color = Color.Lerp(col, Color.white, capTint);
        LayoutTicks(ticks, pool, ref shown, max, barW);
    }

    // Opaque hairlines at every whole-step boundary strictly inside the bar. The count follows the
    // live capacity, so clones come from the authored template; positions refresh every frame
    // (cheap, a handful of ticks) so an inspector resize is picked up immediately.
    void LayoutTicks(RectTransform holder, List<RectTransform> pool, ref int shown, int max, float barW)
    {
        if (holder == null || tickTemplate == null) return;
        int n = max > 0 && tickStep > 0 ? Mathf.Max(0, Mathf.CeilToInt(max / (float)tickStep) - 1) : 0;
        if (n != shown)
        {
            shown = n;
            while (pool.Count > n)
            {
                RectTransform last = pool[^1];
                pool.RemoveAt(pool.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }
            while (pool.Count < n)
            {
                Image t = Instantiate(tickTemplate, holder);
                t.name = $"Tick{pool.Count + 1}";
                t.gameObject.SetActive(true);
                pool.Add(t.rectTransform);
            }
        }
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            pool[i].anchoredPosition = new Vector2(barW * ((i + 1) * tickStep) / max, 0f);
        }
    }
}
