using UnityEngine;

/// <summary>
/// The hover ring: an era-coloured circle at a building's reach that fades in while the built
/// building is under the cursor (BM.Hovered) and out again — the Collector's ring (2026-09-13)
/// made shared (2026-09-14) for pylons (cable radius), the Cell (loose-chip ring), turrets
/// (Finder radius) and the Expander (span). The circle is the authored child LineRenderer
/// <see cref="ring"/> (Tools/Hover Ring Kit adds it: local space, no caps/corners — the
/// LineRenderer AABB gotcha); this component sits on the building's non-rotating root and
/// redraws the circle whenever the reach changes (an upgrade). Reach = the building's
/// <see cref="Building.HoverRingRadius"/>, else the Finder's radius for turrets.
/// </summary>
public class HoverRing : MonoBehaviour
{
    [Tooltip("The building whose reach this shows (Tools/Hover Ring Kit wires it).")]
    public Building building;
    [Tooltip("Authored child LineRenderer the circle is drawn on.")]
    public LineRenderer ring;
    [Tooltip("Peak alpha of the ring.")]
    [Range(0f, 1f)] public float alpha = 0.35f;
    [Tooltip("Seconds the ring takes to fade in / out.")]
    public float fadeSeconds = 0.3f;

    Finder finder;
    bool finderLooked;
    float a, drawn = -1f;

    /// <summary>The reach to draw right now (0 = nothing to show).</summary>
    public float Radius
    {
        get
        {
            if (building == null) return 0f;
            float r = building.HoverRingRadius;
            if (r > 0f) return r;
            if (!finderLooked)
            {
                finderLooked = true;
                var root = building.hasExtraParent && building.transform.parent != null ? building.transform.parent : building.transform;
                finder = root.GetComponentInChildren<Finder>(true);
            }
            return finder != null ? finder.radius : 0f;
        }
    }

    void LateUpdate()
    {
        if (ring == null || building == null) return;
        float r = Radius;
        bool hover = r > 0.01f && building.builtYet && BM.Hovered == building;
        a = Mathf.MoveTowards(a, hover ? alpha : 0f, Time.deltaTime * alpha / Mathf.Max(0.01f, fadeSeconds));
        Apply(a, r);
    }

    void OnDisable()
    {
        a = 0f;
        if (ring != null && ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
    }

    void Apply(float alphaNow, float r)
    {
        var go = ring.gameObject;
        if (alphaNow <= 0.003f)
        {
            if (go.activeSelf) go.SetActive(false);
            return;
        }
        if (!go.activeSelf) go.SetActive(true);
        if (Mathf.Abs(drawn - r) > 1e-3f) { Collector.DrawRing(ring, r); drawn = r; }
        Color c = GS.ColFromEra();
        c.a = alphaNow * (0.85f + 0.15f * Mathf.Sin(Time.time * 2.4f));   // a gentle breathe while shown
        ring.startColor = ring.endColor = c;
    }
}
