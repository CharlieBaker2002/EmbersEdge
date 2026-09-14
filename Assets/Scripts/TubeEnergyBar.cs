using UnityEngine;

/// <summary>
/// The Tube's energy need, as ONE gauge per connected shape: it hangs off the right edge
/// of the whole shape (the host box carries it; every other box's copy stays hidden), its length
/// is the shelf's BILL at the next clear cycle (upkeepPerOre × chips — 1 stripe = 1 energy, 40
/// chips at the default 0.025), and its fill is how much of that bill the distinct sources the
/// shape touches could pay inside the bill's window (the lesser of their banked energy and their
/// rate-limited delivery over upkeepTick × chips — fair-share peek). Empty shelf — hidden.
/// </summary>
public class TubeEnergyBar : EnergyGaugeBar
{
    public static void Attach(Tube s)
    {
        var bar = s.gameObject.AddComponent<TubeEnergyBar>();
        bar.building = s;
    }

    Tube Store => building as Tube;
    TubeCluster Cluster => Store != null && Store.cluster != null && Store.cluster.host == Store ? Store.cluster : null;

    protected override bool Sample(out float span, out float frac)
    {
        span = 0f;
        frac = 0f;
        var cl = Cluster;
        if (cl == null || cl.chips.Count == 0) return false;
        span = cl.Bill;
        if (span <= 0f) return false;
        float window = Mathf.Max(0.05f, cl.Window);
        frac = Mathf.Min(Mathf.Min(cl.Energy(), cl.PeekOffer(window)), span) / span;
        return true;
    }

    protected override Vector3 BarOffset()
    {
        var cl = Cluster;
        if (cl == null) return base.BarOffset();
        Vector3 p = building.transform.position;
        return new Vector3(cl.boundsMax.x + 0.16f - p.x, cl.boundsMin.y - p.y, 0f);
    }

    protected override float BarMaxHeight()
    {
        var cl = Cluster;
        return cl != null ? cl.boundsMax.y - cl.boundsMin.y : base.BarMaxHeight();
    }
}
