using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for the era-1 "floor tile" MINE-POCKET extras — flat ground glyphs the PLAYER walks over to receive
/// an effect (stun / slow / speed / root / launch / heal / orb). Authored in the Mine Forge as per-pocket
/// Object extras (MineAuthoringSO.otherObjectsPalette → PocketTemplate.extras) and spawned by
/// Pocket.SpawnExtras the instant the pocket is breached — at the EXACT painted cell, with the per-placement
/// PlacedObject.chance as the ONLY spawn-chance source (nothing self-scatters; the tile never rolls its own).
///
/// Built on <see cref="WaveExtra"/> purely for its code-baked SDF art + element halo + particle bursts (and
/// the pop-in / Despawn shrink-out). They are NOT wave-pipeline extras: they don't sit in SpawnManager.alives,
/// so they gate nothing. Cleanup is tied to the pocket — each tile re-parents under its Pocket on spawn, so it
/// is torn down with the pocket (ClearPockets on era regen); the wave maxLifetime watchdog is disabled while
/// parented (kept only as a safety net if a tile is ever spawned outside a pocket).
///
/// Each tile script only describes its element, glyph and one effect; this base owns:
///   • build-safe player detection — a squared-distance test vs CharacterScript.CS (NO colliders, so it works
///     in builds where Physics2D AutoSyncTransforms is off), like EmberSnare's poll model;
///   • three trigger modes —
///       Discrete   : fires on step-on, then dormant + RE-ARMS after `rearm`s ("refreshes every N sec"). stun/root/launch.
///       Continuous : re-applies on a short cadence while stood on, lingering `effectDuration`s after. slow/speed.
///       OneShot    : fires once then spends itself (shrink-out). heal/orb pickups.
///   • a dormant↔armed glow pulse via the halo so the player can read the tile's state.
/// Art is a baked SDF glyph on the dark floor (Buildings layer, under units), tinted to ONE elemental ramp.
/// </summary>
public abstract class FloorTile : WaveExtra, IFloorTile
{
    /// <summary>World size of one mine cell — every floor tile is baked + triggered as exactly this 1×1 cell
    /// (matches <see cref="MineField.cellSize"/>). Keep in sync if the grid cell size ever changes.</summary>
    public const float CellWorld = 0.5f;
    /// <summary>Square world span the glyph is baked into (one cell).</summary>
    protected const float TileWorld = CellWorld;

    [Header("Floor tile")]
    [Tooltip("Effect/FX reach in world units (shockwave + particle sizes). The tile GRAPHIC is always one " +
             "0.5×0.5 cell regardless — this only scales the cast effects, not the footprint.")]
    public float radius = 1f;
    [Tooltip("Effect time in seconds: CC duration (stun/root) or the linger after stepping off (slow/speed).")]
    public float effectDuration = 1.5f;
    [Tooltip("Effect magnitude — meaning depends on the tile (slow keep-fraction, speed multiplier, heal " +
             "fraction of max HP, launch speed, orb base count).")]
    public float magnitude = 0f;
    [Tooltip("Seconds before a Discrete tile re-arms after firing ('refreshes every N sec').")]
    public float rearm = 3f;

    protected enum Mode { Discrete, Continuous, OneShot }
    protected abstract Mode TriggerMode { get; }
    /// <summary>The tile's single elemental ramp (bright end), e.g. R4 / B4 / G4 / W4.</summary>
    protected abstract Color Element { get; }
    /// <summary>Bake the tile's glyph into one cell (<see cref="TileWorld"/>).</summary>
    protected abstract Sprite BakeBody();
    /// <summary>Apply the effect to a unit standing on the tile (player, enemy or ally). Discrete/Continuous
    /// status tiles call this on EVERY unit on them; OneShot pickups call it once, on the player only.</summary>
    protected abstract void Fire(Unit u);

    /// <summary>Whether this tile affects ALL units (status hazards) or just the player (OneShot pickups).
    /// Heal/Orb are player-only pickups; everything else is a collective hazard.</summary>
    protected virtual bool AffectsAllUnits => TriggerMode != Mode.OneShot;

    protected SpriteRenderer sr;
    protected ParticleSystem burst;
    protected float phase;
    float reapplyTimer;
    const float ReapplyCadence = 0.28f; // continuous tiles re-stamp the status this often while stood on

    // ---- collective anti-lock gate -----------------------------------------------------------
    // Hard-CC status tiles (Discrete: stun / root / launch) share ONE per-unit cooldown across ALL tiles.
    // After any such tile fires on a unit, no Discrete tile fires on that unit again until its gate expires —
    // so a FIELD of tiles can never chain-lock a unit (player, enemy or ally) in a forever loop. Each unit
    // is gated independently, so one unit's hit never shields another.
    static readonly Dictionary<Unit, float> ccGate = new Dictionary<Unit, float>();
    const float CCGrace = 0.85f;   // guaranteed free movement window granted after each hard-CC hit

    static bool CCReady(Unit u) => u == null || !ccGate.TryGetValue(u, out float t) || Time.time >= t;
    static void StampCC(Unit u, float lockSeconds)
    {
        if (u == null) return;
        ccGate[u] = Time.time + lockSeconds;
        if (ccGate.Count > 48) PruneGate();   // keep the shared map from leaking destroyed units
    }
    static void PruneGate()
    {
        var dead = new List<Unit>();
        foreach (var kv in ccGate) if (kv.Key == null || Time.time >= kv.Value) dead.Add(kv.Key);
        foreach (var k in dead) ccGate.Remove(k);
    }

    // Reused scratch buffers (no per-frame allocation).
    static readonly Collider2D[] _overlap = new Collider2D[24];
    readonly List<Unit> hits = new List<Unit>(8);

    // Mobile units only (player + ally/enemy units, not static buildings), matching GS.FindEnemies' overlap style.
    static ContactFilter2D _unitFilter;
    static bool _unitFilterReady;
    static ContactFilter2D UnitFilter()
    {
        if (!_unitFilterReady)
        {
            _unitFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = LayerMask.GetMask("Character", "Ally Units", "Enemy Units")
            };
            _unitFilterReady = true;
        }
        return _unitFilter;
    }

    protected virtual void Awake()
    {
        // Tie our lifetime to the pocket we were painted into: re-parent under it (Pocket.SpawnExtras drops us
        // under GS.Parent.misc) so we're destroyed when the pocket is (ClearPockets on era regen), and drop the
        // wave-only maxLifetime watchdog. If we somehow spawn outside a pocket, keep the watchdog as a safety net.
        var mdm = MineDungeonManager.i;
        if (mdm != null && mdm.activePocket != null)
        {
            transform.SetParent(mdm.activePocket.transform, true);
            maxLifetime = 0f;
        }

        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = BakeBody();
        sr.sortingLayerName = "Buildings"; // a ground decal: under units / the player, above the floor
        sr.sortingOrder = 0;
        var stock = Resources.Load<Material>("Sprite-Unlit-Default");
        if (stock != null) sr.sharedMaterial = stock;
        MakeHalo(Element, CellWorld * 1.5f);   // glow scaled to the single cell, not the FX radius
        burst = MakeBurst(transform, "Power Ups", 6);
    }

    /// <summary>Is a world point within this tile's 1×1 cell (+ a little body slack)?</summary>
    bool InCell(Vector2 p)
    {
        Vector2 d = p - (Vector2)transform.position;
        const float half = CellWorld * 0.5f + 0.1f;
        return Mathf.Abs(d.x) <= half && Mathf.Abs(d.y) <= half;
    }

    /// <summary>Build-safe player test (no physics) — the known singleton vs the cell box. Used by OneShot pickups.</summary>
    protected bool PlayerOn(out CharacterScript cs)
    {
        cs = CharacterScript.CS;
        return cs != null && InCell(cs.transform.position);
    }

    /// <summary>All units (player + enemies + allies) currently standing on this cell. The player comes from
    /// the reliable singleton box test; the rest from a cheap proximity overlap (as the AoE abilities do).</summary>
    void GatherOnTile()
    {
        hits.Clear();
        var cs = CharacterScript.CS;
        if (cs != null && InCell(cs.transform.position)) hits.Add(cs);

        int n = Physics2D.OverlapCircle(transform.position, CellWorld * 0.75f, UnitFilter(), _overlap);
        for (int i = 0; i < n; i++)
        {
            var col = _overlap[i];
            if (col == null) continue;
            var u = col.GetComponentInParent<Unit>();
            if (u == null || u == cs || u.ls == null) continue;   // need a living Unit; skip dupes/player
            if (!hits.Contains(u) && InCell(u.transform.position)) hits.Add(u);
        }
    }

    protected virtual void Update()
    {
        if (consumed) return;
        phase += Time.deltaTime;

        // OneShot pickups stay PLAYER-ONLY (heal/orb shouldn't help enemies); the status hazards hit everyone.
        if (!AffectsAllUnits)
        {
            if (PlayerOn(out var pcs)) { Fire(pcs); OnConsumeFX(); }
            if (consumed) return;        // let Despawn's shrink-out own the scale
            DriveGlow(PlayerOn(out _));
            return;
        }

        GatherOnTile();
        bool any = hits.Count > 0;
        bool playerOn = false;

        switch (TriggerMode)
        {
            case Mode.Discrete:
            {
                bool firedAny = false;
                for (int i = 0; i < hits.Count; i++)
                {
                    var u = hits[i];
                    if (u == CharacterScript.CS) playerOn = true;
                    if (CCReady(u))            // collective per-unit gate guarantees each unit a free window
                    {
                        Fire(u);
                        StampCC(u, Mathf.Max(rearm, effectDuration + CCGrace));
                        firedAny = true;
                    }
                }
                if (firedAny) OnFireFX();
                break;
            }

            case Mode.Continuous:
            {
                reapplyTimer -= Time.deltaTime;
                bool tick = any && reapplyTimer <= 0f;
                for (int i = 0; i < hits.Count; i++)
                {
                    if (hits[i] == CharacterScript.CS) playerOn = true;
                    if (tick) Fire(hits[i]);     // soft buffs/debuffs — no loop risk, no gate
                }
                if (tick) { reapplyTimer = ReapplyCadence; OnTickFX(); }
                else if (!any) reapplyTimer = 0f;
                break;
            }
        }

        DriveGlow(playerOn || any);
    }

    // ---- shared visuals ----------------------------------------------------------------------
    // Armed/active tiles breathe brightly; a Discrete tile that just hit the PLAYER dims and visibly
    // re-charges over their personal cooldown, so the player can time their crossings.
    protected virtual void DriveGlow(bool on)
    {
        float breath = 0.5f + 0.5f * Mathf.Sin(phase * 2.4f);
        float charge = 1f;
        if (TriggerMode == Mode.Discrete)
        {
            var cs = CharacterScript.CS;
            if (cs != null && ccGate.TryGetValue(cs, out float t) && Time.time < t)
            {
                float lockLen = Mathf.Max(0.1f, Mathf.Max(rearm, effectDuration + CCGrace));
                charge = Mathf.Lerp(0.18f, 0.9f, 1f - Mathf.Clamp01((t - Time.time) / lockLen));
            }
        }
        float live = on ? 1.35f : 1f;
        SetGlow((0.45f + 0.85f * breath) * charge * live);
        if (sr != null)
        {
            float a = Mathf.Lerp(0.62f, 1f, charge) * (0.78f + 0.22f * breath);
            sr.color = new Color(1f, 1f, 1f, a);
        }
        // popK (driven by WaveExtra.Start's pop-in) folds the lively spawn scale in for free.
        transform.localScale = baseScale * Mathf.Max(popK, 0.001f) * (1f + 0.05f * breath * charge);
    }

    // A soft outward ember ring when a Discrete tile springs.
    protected virtual void OnFireFX()
    {
        EmitBurst(burst, transform.position, 16, Ramp(), 1.4f, 3.8f, 0.3f, 0.6f, 0.12f * radius, 0.26f * radius);
    }

    // A light shimmer while a Continuous tile is doing its thing (kept cheap — throttled by cadence).
    protected virtual void OnTickFX()
    {
        EmitBurst(burst, transform.position, 3, Ramp(), 0.5f, 1.6f, 0.3f, 0.6f, 0.1f * radius, 0.2f * radius);
    }

    // A full pop when a OneShot pickup is spent.
    protected virtual void OnConsumeFX()
    {
        EmitBurst(burst, transform.position, 22, Ramp(), 2.2f, 5.5f, 0.4f, 0.8f, 0.14f * radius, 0.3f * radius);
        Despawn();
    }

    /// <summary>The two brightest steps of this tile's ramp, for burst tinting.</summary>
    protected abstract Color[] Ramp();

    // ---- shared baked-art substrate (glyphs layer on top of this) -----------------------------
    protected const float AA = 2f / 128f;

    /// <summary>The square pad every floor-tile glyph sits on: a soft dark-purple cell fill + a bright elemental
    /// rim ring hugging the cell border. Uses Chebyshev distance so the tile reads as a clean 1×1 square.</summary>
    protected static Color Pad(float nx, float ny, Color rim)
    {
        float m = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));   // square boundary (Chebyshev)
        Color c = new Color(0f, 0f, 0f, 0f);
        float pad = Fill(m - 0.97f, AA);                     // fill nearly the whole cell
        if (pad > 0f) c = Over(c, A(D1a, pad * 0.5f));
        float ring = Mathf.Clamp01(1f - Mathf.Abs(m - 0.9f) / 0.06f);
        if (ring > 0f) c = Over(c, A(rim, ring));
        return c;
    }
}

/// <summary>Marker so the builder/forge can recognise floor-tile prefabs without a hard type list.</summary>
public interface IFloorTile { }
