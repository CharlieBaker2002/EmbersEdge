using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debris scattered by every broken dungeon wall (OreChips_0..3 small / 4..7 big / 8..11 large,
/// element-lit when the wall carried ore). Physical litter: each chip bursts out of the break
/// spinning, skids to rest, and is plowed aside by anything that walks through it (AllUnits
/// layer — units shove chips, chips never touch each other, walls or projectiles). No value
/// yet — bag drones haul them home as scrap; anything left despawns when the player returns
/// to base.
/// </summary>
public class OreChip : MonoBehaviour
{
    public static readonly List<OreChip> all = new List<OreChip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    [HideInInspector] public int sizeClass;    // 0 small, 1 big, 2 large
    [HideInInspector] public int element = -1; // -1 plain rock, else 0..3 white/green/blue/red
    [HideInInspector] public Drone claimedBy;  // a bag drone en route (avoids double pickup)
    public SpriteRenderer sr;
    [HideInInspector] public Rigidbody2D rb;

    /// <summary>Bag space this chip occupies (small 1 / big 2 / large 3).</summary>
    public int SpaceCost => sizeClass + 1;

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
    float absorbT;
    Vector3 absorbScale;
    public bool Absorbing => mouth != null;

    /// <summary>The visible pickup: the chip latches onto the drone's front and ease-out shrinks
    /// into it. Cargo is banked by the drone at touch — this is pure presentation, ending in
    /// Destroy. The chip leaves the loot registry immediately so nothing re-targets it.</summary>
    public void AbsorbInto(Transform drone)
    {
        mouth = drone;
        absorbT = 0f;
        absorbScale = transform.localScale;
        claimedBy = null;
        all.Remove(this);
        if (rb != null) rb.simulated = false;   // stop skidding/being plowed mid-swallow
    }

    /// <summary>Kick the chip out of the break point: outward skid + spin, damped to a stop.
    /// Also bolts on the physics body (runtime-added so pre-physics prefabs keep working).</summary>
    public void Tumble(Vector2 burstFrom)
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
        rb.linearVelocity = dir * (Random.Range(1.6f, 3.2f) / (1f + 0.45f * sizeClass));
        rb.angularVelocity = Random.Range(180f, 540f) * (Random.value < 0.5f ? -1f : 1f);
    }

    /// <summary>Dungeon walls are hard geometry to a skidding chip: reflect off the mine grid
    /// like a projectile, losing 40% speed per bounce. Tests are padded by the sprite's
    /// half-extent so the VISIBLE chip never overlaps a wall tile, not just its centre.
    /// Axis-separated tile reflection (a corner hit flips both); base-side positions are out
    /// of the grid's bounds so this no-ops there.</summary>
    void FixedUpdate()
    {
        if (rb == null || !rb.simulated) return;
        var mf = MineField.i;
        if (mf == null) return;
        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude < 1e-4f) return;
        Vector2 p = rb.position;
        Vector2 next = p + v * Time.fixedDeltaTime;
        float pad = WallPad;
        float ex = next.x + Mathf.Sign(v.x) * pad;   // leading sprite edge, per axis
        float ey = next.y + Mathf.Sign(v.y) * pad;
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
        if (Absorbing)
        {
            if (mouth == null) { Destroy(gameObject); return; }   // drone died mid-swallow
            absorbT += Time.deltaTime / 0.35f;
            if (absorbT >= 1f) { Destroy(gameObject); return; }
            float e = 1f - (1f - absorbT) * (1f - absorbT) * (1f - absorbT);   // ease-out cubic
            Vector3 front = mouth.position - mouth.up * 0.26f;   // the drone's mouth point
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
            transform.localScale = Vector3.one * (1f + 2.70158f * u * u * u + 1.70158f * u * u);
        }
        else if (transform.localScale.x != 1f) transform.localScale = Vector3.one;
    }
}
