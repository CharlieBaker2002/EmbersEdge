using UnityEngine;

/// <summary>
/// A self-contained energy battery for a building source (generators). Has finite capacity it
/// charges via <see cref="Add"/>, a sustained <see cref="drawRate"/>, and an <b>instabuffer</b>
/// — a burst pool on top of the rate that refills at the rate when idle. This is the same
/// rate+insta model <see cref="Battery"/> and <see cref="PylonCable"/> use, and it's what lets a
/// consumer pull a whole shot's cost in a single frame instead of being capped to rate*dt (a
/// trickle) — without it, one-shot <c>Use(cost)</c> draws fail and the battery looks "missing".
///
/// Plain C# (not a MonoBehaviour) so a generator can own one and forward its IEnergyAccumulator
/// members to it. <see cref="Tick"/> must be called once per frame from the owner's Update to
/// reconcile the instabuffer.
/// </summary>
public class EnergyStore
{
    public float capacity;
    public float drawRate;
    public float instaBufferMax;

    private float energy;
    private float instaBuffer;     // current burst credit available
    private float drawnThisFrame;  // accumulated draws in the current frame

    // Fair-share throttling. Every drawing consumer's budget check lands here as one
    // MaxDrawThisFrame call per frame (via BuildingPower.Use / DrawEnergy, through any depth of
    // cables), so last frame's query count ≈ how many consumers are competing. Each caller is
    // offered an equal slice of the frame pool instead of whatever earlier callers left —
    // otherwise the first consumer in draw order takes everything and the rest starve. Slices
    // nobody claims roll into the instabuffer at Tick, so a light drinker's leftovers still
    // reach the hungry ones (throughput is unchanged, delivery just alternates fairly).
    private int queriesLastFrame, queriesThisFrame;

    public EnergyStore(float capacity, float drawRate, float instaBufferMax)
    {
        Configure(capacity, drawRate, instaBufferMax);
    }

    /// <summary>(Re)size the battery. Tops the instabuffer back up; preserves stored energy (clamped to the new capacity).</summary>
    public void Configure(float capacity, float drawRate, float instaBufferMax)
    {
        this.capacity = Mathf.Max(0f, capacity);
        this.drawRate = Mathf.Max(0f, drawRate);
        this.instaBufferMax = Mathf.Max(0f, instaBufferMax);
        instaBuffer = this.instaBufferMax;
        energy = Mathf.Min(energy, this.capacity);
    }

    public float Energy => energy;
    public float MaxEnergy => capacity;
    public float DrawRate => energy > 0f ? drawRate : 0f;

    /// <summary>Most this battery can deliver this frame: rate*dt plus whatever instabuffer is left, capped by stored energy.</summary>
    public float MaxDrawThisFrame(float dt)
    {
        queriesThisFrame++;
        if (energy <= 0f) return 0f;
        float pool = drawRate * dt + instaBuffer;
        float budget = pool - drawnThisFrame;
        if (queriesLastFrame > 1) budget = Mathf.Min(budget, pool / queriesLastFrame);
        return Mathf.Min(energy, Mathf.Max(0f, budget));
    }

    /// <summary>MaxDrawThisFrame without the fair-share query registration (UI/diagnostic reads).</summary>
    public float PeekMaxDraw(float dt)
    {
        if (energy <= 0f) return 0f;
        float pool = drawRate * dt + instaBuffer;
        float budget = pool - drawnThisFrame;
        if (queriesLastFrame > 1) budget = Mathf.Min(budget, pool / queriesLastFrame);
        return Mathf.Min(energy, Mathf.Max(0f, budget));
    }

    /// <summary>Draw cost. Returns false (changes nothing) if there isn't enough stored energy.</summary>
    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (energy < cost) return false;
        energy -= cost;
        drawnThisFrame += cost;
        return true;
    }

    /// <summary>Charge the battery. Excess over capacity is dropped.</summary>
    public void Add(float amount)
    {
        if (amount <= 0f) return;
        energy = Mathf.Min(capacity, energy + amount);
    }

    /// <summary>
    /// Per-frame instabuffer reconcile (call once per frame from the owner's Update). Refill by
    /// what we could have given at rate (drawRate*dt) minus what was actually drawn, clamped to
    /// [0, max]: heavy bursts drain it, idle frames let it climb back toward full.
    /// </summary>
    public void Tick(float dt)
    {
        queriesLastFrame = queriesThisFrame;
        queriesThisFrame = 0;
        instaBuffer = Mathf.Clamp(instaBuffer + drawRate * dt - drawnThisFrame, 0f, instaBufferMax);
        drawnThisFrame = 0f;
    }
}
