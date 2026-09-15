using UnityEngine;

/// <summary>
/// A belt line's energy need, as ONE gauge per line: it hangs off the right edge of the whole
/// run (the host tile carries it; every other tile's copy stays hidden), its length is the bill
/// of the next step (energyPerStep × chips that will move — 1 stripe = 1 energy = 10 chips at
/// the default 0.1) and its fill is how much of that the sources touching the line could pay inside
/// one step (the lesser of their banked energy and their rate-limited offer). Empty or fully
/// stacked-up belt — hidden.
/// </summary>
public class BeltEnergyBar : EnergyGaugeBar
{
    public static void Attach(Belt b)
    {
        var bar = b.gameObject.AddComponent<BeltEnergyBar>();
        bar.building = b;
    }

    Belt Tile => building as Belt;
    BeltLine Line => Tile != null && Tile.line != null && Tile.line.host == Tile ? Tile.line : null;

    protected override bool Sample(out float span, out float frac)
    {
        span = 0f;
        frac = 0f;
        var l = Line;
        if (l == null || l.PendingMovers == 0 || l.power == null) return false;
        span = Mathf.Max(0f, Tile.energyPerStep) * l.PendingMovers;   // stacked chips don't move, don't bill
        if (span <= 0f) return false;
        float window = Mathf.Max(0.05f, Tile.stepSeconds);
        frac = Mathf.Min(Mathf.Min(l.power.Energy(), l.power.PeekOffer(window)), span) / span;
        return true;
    }

    protected override Vector3 BarOffset()
    {
        var l = Line;
        if (l == null) return base.BarOffset();
        Vector3 p = building.transform.position;
        return new Vector3(l.boundsMax.x + 0.16f - p.x, l.boundsMin.y - p.y, 0f);
    }

    protected override float BarMaxHeight()
    {
        var l = Line;
        return l != null ? l.boundsMax.y - l.boundsMin.y : base.BarMaxHeight();
    }
}
