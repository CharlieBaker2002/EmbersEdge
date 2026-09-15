using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debris scattered by every broken dungeon wall (OreChips_0..3 small / 4..7 big / 8..11 large —
/// the class is the slice's ember/emission pixel count, 1/2/3; Tools/Ore Chip Kit re-sorts the strip,
/// element-lit when the wall carried ore). Physical litter: each chip bursts out of the break
/// spinning, skids to rest, and is plowed aside by anything that walks through it (AllUnits
/// layer — units shove chips, chips never touch each other, walls or projectiles). Loose chips
/// are EPHEMERAL: every clear cycle (the teleport home, a wave clearing — see ChipClearCycle)
/// fades them out unless a consumer's intake ring already owns them or a powered Tube
/// holds them (a stored chip leaves <see cref="all"/> and physics entirely; the store cluster
/// drives it — see <see cref="stored"/>).
/// </summary>
public class OreChip : MonoBehaviour
{
    public static readonly List<OreChip> all = new List<OreChip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    [HideInInspector] public int sizeClass;    // 0 small, 1 big, 2 large
    [HideInInspector] public int element = -1; // 0 = ore (ONE kind — the era's); -1 = legacy plain rock (no longer spawns)
    [HideInInspector] public Drone claimedBy;  // a bag drone en route (avoids double pickup)
    /// <summary>Came out of a Refiner split — the refinery won't take it again (see IChipConsumer.RefusesRefined).</summary>
    [HideInInspector] public bool refined;
    /// <summary>Fleet-wide cooldown stamped by a bag drone that gave up reaching this chip —
    /// collect scans skip it until then (it may free itself, or the route may open).</summary>
    [HideInInspector] public float unreachableUntil;
    /// <summary>Stamped every physics tick the player's Hoover is pulling this chip — building
    /// intakes (the Collector) leave a chip under the player's pull alone.</summary>
    [HideInInspector] public float playerPullStamp = float.NegativeInfinity;
    public bool PulledByPlayer => Time.time - playerPullStamp < 0.25f;
    /// <summary>Stamped every physics tick a Collector is dragging this chip to its pile — the
    /// tube rings and belt tiles it crosses on the way leave it alone (a chip snatched mid-pull
    /// stopped simulating and stayed in the Collector's pull list for good, 2026-09-14).</summary>
    [HideInInspector] public float collectorPullStamp = float.NegativeInfinity;
    public bool PulledByCollector => Time.time - collectorPullStamp < 0.25f;
    /// <summary>What holds this chip off the ground (null = loose): a Tube cluster (TubeCluster)
    /// or a belt line (BeltLine). A stored chip is OUT of <see cref="all"/> and physics-free —
    /// the holder owns its position; it comes back to the world through <see cref="Unstore"/>
    /// (dropped, set down at a belt's end) or a drone's <see cref="AbsorbInto"/> (fetched off a
    /// shelf for a real consumer — never off a belt).</summary>
    [System.NonSerialized] public object stored;
    public bool Stored => stored != null;
    /// <summary>Scale the pop-in / steady scale settles at (a store packs its chips smaller).</summary>
    [HideInInspector] public float scaleMul = 1f;
    bool fading;
    /// <summary>Mid clear-cycle fade: already off the registry, about to be destroyed.</summary>
    public bool Fading => fading;
    public SpriteRenderer sr;
    [HideInInspector] public Rigidbody2D rb;

    /// <summary>Bag space this chip occupies (small 1 / big 2 / large 4).</summary>
    public int SpaceCost => SpaceFor(sizeClass);

    /// <summary>Bag space by size class, for callers with no chip in hand.</summary>
    public static int SpaceFor(int sizeClass) => sizeClass == 2 ? 4 : sizeClass + 1;

    /// <summary>Juice yielded per unit of bag space for a size class (small 0.1 / big 0.25 /
    /// large 0.3125) — the honest exchange rate for demand heuristics that plan hauls in
    /// space units against a juice want.</summary>
    public static float JuicePerSpace(int sizeClass) => JuiceFor(sizeClass) / SpaceFor(sizeClass);

    /// <summary>Energy the grinder credits when this chip is ground down
    /// (small 0.2 / big 0.5 / large 1.25). Set by size, not by bag space.</summary>
    public float JuiceValue => JuiceFor(sizeClass);

    /// <summary>Juice by size class, for callers that only know the class (Refiner's intake
    /// gate runs before any chip object is in hand).</summary>
    public static float JuiceFor(int sizeClass) => sizeClass == 0 ? 0.1f : sizeClass == 1 ? 0.5f : 1.25f;

    /// <summary>Bag drones must not vacuum a chip mid-pop-in — debris the player never SEES
    /// reads as debris that never spawned. Chips are claimable only after this long.</summary>
    public const float SettleSeconds = 1.5f;

    float born;
    public float Age => Time.time - born;

    // Half-extent of the chip's SPRITE (7px @ 32ppu, scaled by size) — wall tests pad by this
    // so the visible chip stays inside the cavity, not just its centre point.
    public float WallPad => 0.06f + 0.035f * sizeClass;

    void OnEnable()
    {
        all.Add(this);
        born = Time.time;
        transform.localScale = Vector3.zero;
    }

    void OnDisable() => all.Remove(this);

    // ---- absorb-into-drone (the visible pickup) ----
    Transform mouth;      // collecting drone; its work face hangs at local -Y (FaceDir points it here)
    float mouthOffset = 0.26f;   // how far down the mouth's -Y the swallow point sits (0 = the transform itself)
    float absorbT;
    Vector3 absorbScale;
    public bool Absorbing => mouth != null;

    /// <summary>The visible pickup: the chip latches onto the drone's front and ease-out shrinks
    /// into it. Cargo is banked by the drone at touch — this is pure presentation, ending in
    /// Destroy. The chip leaves the loot registry immediately so nothing re-targets it.
    /// <paramref name="mouthOffset"/> is how far down the mouth's -Y the swallow point sits —
    /// a drone's work face hangs 0.26 below it; a building or the player's Hoover nozzle
    /// swallows at the transform itself (0).</summary>
    public void AbsorbInto(Transform drone, float mouthOffset = 0.26f)
    {
        mouth = drone;
        this.mouthOffset = mouthOffset;
        absorbT = 0f;
        absorbScale = transform.localScale;
        claimedBy = null;
        stored = null;   // a store's cluster prunes the entry itself (Absorbing)
        all.Remove(this);
        if (rb != null) rb.simulated = false;   // stop skidding/being plowed mid-swallow
    }

    /// <summary>Into a Tube or onto a Belt: off the loose registry, physics parked (body off so
    /// nothing scatters the shelf), claims dropped. The holder moves it from here on.</summary>
    public void Store(object holder)
    {
        stored = holder;
        claimedBy = null;
        all.Remove(this);
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }
        var body = GetComponent<Collider2D>();
        if (body != null) body.enabled = false;
    }

    /// <summary>Back out of a store as an ordinary loose chip (a box died under it, or the
    /// shelf lost its room): registry, body and full scale return.</summary>
    public void Unstore()
    {
        stored = null;
        scaleMul = 1f;
        if (!all.Contains(this)) all.Add(this);
        if (rb != null) rb.simulated = true;
        var body = GetComponent<Collider2D>();
        if (body != null) body.enabled = true;
    }

    /// <summary>The clear-cycle vanish: a tiny minimal effect — the chip lifts a hair, flashes
    /// toward white and shrinks away over <paramref name="seconds"/> (ease-in, so it lingers
    /// then pops), then destroys itself. Off the registry at once so nothing targets it.</summary>
    public void FadeOut(float seconds = 0.45f, float delay = 0f)
    {
        if (fading || Absorbing) return;
        fading = true;
        claimedBy = null;
        stored = null;
        all.Remove(this);
        if (rb != null) rb.simulated = false;
        var body = GetComponent<Collider2D>();
        if (body != null) body.enabled = false;
        StartCoroutine(FadeCo(seconds, delay));
    }

    IEnumerator FadeCo(float seconds, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Vector3 s0 = transform.localScale;
        Vector3 p0 = transform.position;
        Color c0 = sr != null ? sr.color : Color.white;
        Color flash = Color.Lerp(c0, Color.white, 0.7f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, seconds);
            float k = Mathf.Clamp01(t);
            float e = k * k;                                   // ease-in: holds, then goes
            transform.localScale = s0 * (1f - e);
            transform.position = p0 + Vector3.up * (0.07f * k);
            if (sr != null)
            {
                Color c = Color.Lerp(c0, flash, Mathf.Sin(k * Mathf.PI));
                c.a = c0.a * (1f - e);
                sr.color = c;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>Kick the chip out of the break point: outward skid + spin, damped to a stop.
    /// Also bolts on the physics body (runtime-added so pre-physics prefabs keep working).</summary>
    /// <summary>Give the chip its body and send it skidding away from <paramref name="burstFrom"/>.
    /// <paramref name="kick"/> scales the launch speed (1 = the loose scrap puff; a drone's
    /// careful deposit uses a fraction so the chip settles where it was put).</summary>
    public void Tumble(Vector2 burstFrom, float kick = 1f)
    {
        if (rb == null)
        {
            gameObject.layer = LayerMask.NameToLayer("AllUnits");
            var col = GetComponent<CircleCollider2D>();
            if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.05f + 0.035f * sizeClass;
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.mass = 0.02f + 0.02f * sizeClass;   // feather-light: units plow through unbothered
            rb.linearDamping = 2.5f;
            rb.angularDamping = 1.5f;
        }
        Vector2 dir = (Vector2)transform.position - burstFrom;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Random.insideUnitCircle.normalized;
        // big chunks lumber, small flakes zip
        rb.linearVelocity = dir * (kick * Random.Range(1.6f, 3.2f) / (1f + 0.45f * sizeClass));
        rb.angularVelocity = Mathf.Lerp(60f, 1f, 1f - Mathf.Clamp01(kick)) * Random.Range(3f, 9f) * (Random.value < 0.5f ? -1f : 1f);
    }

    /// <summary>Dungeon walls are hard geometry to a skidding chip: reflect off the mine grid
    /// like a projectile, losing 40% speed per bounce. Tests are padded by the sprite's
    /// half-extent so the VISIBLE chip never overlaps a wall tile, not just its centre.
    /// Axis-separated tile reflection (a corner hit flips both); base-side positions are out
    /// of the grid's bounds so this no-ops there.</summary>
    // Gentle boundary return (base side only): extra clearance kept inside the map edge, the
    // inward acceleration, and the inward speed it stops adding at (linearDamping 2.5 keeps it soft).
    const float BoundaryClearance = 0.25f;
    const float BoundaryReturnAccel = 4f;
    const float BoundaryReturnSpeed = 1.5f;

    void FixedUpdate()
    {
        if (rb == null || !rb.simulated) return;
        Vector2 p = rb.position;
        float wallPad = WallPad;
        // Base side: a chip that skids out past the map boundary is unreachable (drones would
        // chase it off the edge forever) — ease it back inside with a gentle, damped-terminal
        // pull toward the origin instead of a hard clamp. Sleeping bodies wake on the write.
        if (PathZone.AtBase(p) && !MapManager.InsideBoundsWithClearance(p, wallPad + BoundaryClearance))
        {
            Vector2 inward = (-p).normalized;
            Vector2 bv = rb.linearVelocity;
            float along = Vector2.Dot(bv, inward);
            if (along < BoundaryReturnSpeed)
                bv += inward * Mathf.Min(BoundaryReturnAccel * Time.fixedDeltaTime, BoundaryReturnSpeed - along);
            rb.linearVelocity = bv;
        }
        var mf = MineField.i;
        if (mf == null) return;
        // Squeeze rescue: the contact solver corrects POSITIONS — a body plowing a chip against
        // rock (the miner stands right where chips burst out) buries it regardless of velocity,
        // and the reflection below only steers, it can never un-bury. Lift the footprint back
        // into the cavity here; no-ops (and costs a handful of array reads) when clear or at base.
        Vector2 lift = mf.SeparationFor(p, wallPad);
        if (lift != Vector2.zero)
        {
            p += lift;
            rb.position = p;
        }
        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude < 1e-4f) return;
        Vector2 next = p + v * Time.fixedDeltaTime;
        float ex = next.x + Mathf.Sign(v.x) * wallPad;   // leading sprite edge, per axis
        float ey = next.y + Mathf.Sign(v.y) * wallPad;
        bool hitX = v.x != 0f && mf.IsSolidWorld(new Vector2(ex, p.y));
        bool hitY = v.y != 0f && mf.IsSolidWorld(new Vector2(p.x, ey));
        if (!hitX && !hitY)
        {
            if (!mf.IsSolidWorld(new Vector2(ex, ey))) return;   // corner clip
            hitX = true; hitY = true;
        }
        if (hitX) v.x = -v.x;
        if (hitY) v.y = -v.y;
        rb.linearVelocity = v * 0.6f;
        rb.angularVelocity = -rb.angularVelocity * 0.6f;
    }

    void Update()
    {
        if (fading) return;   // FadeCo owns the transform now
        if (Absorbing)
        {
            if (mouth == null) { Destroy(gameObject); return; }   // drone died mid-swallow
            absorbT += Time.deltaTime / 0.35f;
            if (absorbT >= 1f) { Destroy(gameObject); return; }
            float e = 1f - (1f - absorbT) * (1f - absorbT) * (1f - absorbT);   // ease-out cubic
            Vector3 front = mouth.position - mouth.up * mouthOffset;   // the swallow point
            transform.position = Vector3.Lerp(transform.position, front,
                Mathf.Clamp01(10f * Time.deltaTime + 0.35f * e));   // sticks even while the drone moves
            transform.localScale = absorbScale * (1f - e);
            return;
        }

        // pop-in without a tween (LeanTween pool pressure — chips can number in the hundreds);
        // easeOutBack overshoot so fresh chips read as bouncing out of the wall
        float t = (Time.time - born) * 4f;
        if (t < 1f)
        {
            float u = t - 1f;
            transform.localScale = Vector3.one * (scaleMul * (1f + 2.70158f * u * u * u + 1.70158f * u * u));
        }
        else if (transform.localScale.x != scaleMul) transform.localScale = Vector3.one * scaleMul;
    }
}
