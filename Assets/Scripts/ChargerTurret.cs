using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mine Sprayer. Finds a target, winds up, then ATTACKS for one second — drawing energy from
/// the grid LIVE at a capped rate (3/s base, 6/s upgraded) and spawning a mine each time
/// another <see cref="energyPerMine"/> actually arrives. Nothing is banked: the turret shoots
/// exactly what it charges, as it charges it. A throttled grid means a thinner spray.
///
/// Script-driven (no Animator). The TurretCharger sheet (46 frames) is laid out:
///   base    : charge 0..7,  attack 8..22
///   upgrade : charge 23..30, attack 31..45
/// The charge frames are pure windup (no energy moves); the attack frames are where the
/// draw-and-spray happens.
/// </summary>
public class ChargerTurret : Building
{
    public GameObject p;                         // mine prefab
    [SerializeField] Finder find;
    [SerializeField] private Sprite[] frames;    // TurretCharger 0..45
    [SerializeField] Sprite tileSprite;
    [SerializeField] private Sprite morphSprite;
    [SerializeField] private Sprite upgradeBaseSprite;

    [Header("Charge / attack")]
    [Tooltip("Seconds of windup animation before each attack. Cosmetic — no energy is drawn.")]
    [SerializeField] private float chargeTime = 1f;
    [Tooltip("Seconds the attack spray lasts.")]
    [SerializeField] private float attackTime = 1f;
    [SerializeField] private float cooldown = 5f;
    [SerializeField] private float cooldownUpgrade = 2.8f;
    [SerializeField] private float aimSpeed = 8f;

    [Header("Energy")]
    [Tooltip("Max energy/sec drawn while attacking (no upgrade). The grid may supply less -> fewer mines.")]
    [SerializeField] private float drawRate = 3f;
    [Tooltip("Max energy/sec drawn while attacking once Thick Spreader is bought.")]
    [SerializeField] private float drawRateUpgrade = 6f;
    [Tooltip("Energy per mine: one mine fires each time this much energy arrives during the attack.")]
    [SerializeField] private float energyPerMine = 0.25f;

    // base charge 0..7, attack 8..22 ; upgrade charge 23..30, attack 31..45
    private static readonly (int from, int to) ChargeBase = (0, 7);
    private static readonly (int from, int to) AttackBase = (8, 22);
    private static readonly (int from, int to) ChargeUp = (23, 30);
    private static readonly (int from, int to) AttackUp = (31, 45);

    private int level = 1;   // 1 = base, 2 = Thick Spreader
    private Transform T;
    private Coroutine loop;

    private (int from, int to) ChargeRange => level >= 2 ? ChargeUp : ChargeBase;
    private (int from, int to) AttackRange => level >= 2 ? AttackUp : AttackBase;
    private float MaxDrawRate => level >= 2 ? drawRateUpgrade : drawRate;
    private float Cooldown => level >= 2 ? cooldownUpgrade : cooldown;

    /// <summary>The live rate the attack wants to pull at (3 base, 6 with Thick Spreader) —
    /// nothing is banked, so demand IS the rate.</summary>
    public override float PeakEnergyDemand => MaxDrawRate;

    public override void Start()
    {
        base.Start();
        find.OnFound += GetTarget;
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
        AddUpgradeSlot(new int[] { 100, 0, 5, 0 }, "Thick Spreader", tileSprite, true, LevelUp, 11, true, CallMorph);
        TargetPriority.Attach(this, find);
    }

    private void GetTarget(Transform x) => T = x;

    protected override void BEnable()
    {
        find.engaged = true;
        if (Finder.turretsOn) find.enabled = true;   // search right away if built mid-combat
        if (loop == null) loop = StartCoroutine(Run());
    }

    protected override void BDisable()
    {
        find.engaged = false;
        if (loop != null) { StopCoroutine(loop); loop = null; }
        ClearEnergyStatusImmediate();   // turret off — hide at once, don't linger
    }

    public override void OnDeath()
    {
        transform.parent.GetComponent<SpriteRenderer>().color = new Color(200, 200, 200, 1);
        base.OnDeath();
    }

    public void LevelUp()
    {
        level = 2;
        find.UpdateRadius(8f);
        find.refresh /= 2f;
        transform.parent.GetComponent<SpriteRenderer>().sprite = upgradeBaseSprite;
    }

    public void CallMorph()
    {
        GS.QuickMorphWithOrbs(gameObject, morphSprite, transform.parent);
    }

    // ---- State machine: idle -> charge (windup) -> attack (draw + spray) -> cooldown ---------

    IEnumerator Run()
    {
        if (frames == null || frames.Length < 46)
        {
            Debug.LogError($"{name}: ChargerTurret needs all 46 TurretCharger frames; got {(frames == null ? 0 : frames.Length)}.");
            yield break;
        }

        sr.sprite = frames[ChargeRange.from];

        while (true)
        {
            // 1) IDLE — wait for a target and a grid that can pay for at least one mine
            // (nothing is banked, so a bone-dry grid means the volley would spray nothing).
            while (T == null || Power.Energy < energyPerMine)
            {
                sr.sprite = frames[ChargeRange.from];
                if (T != null) ReportEnergyDraw(MaxDrawRate); else ClearEnergyStatus();
                yield return null;
            }

            // 2) CHARGE — pure windup: aim and spin up, no energy moves yet.
            var cr = ChargeRange;
            float maxRate = MaxDrawRate;
            int cFrames = cr.to - cr.from + 1;
            float cFrameTime = chargeTime / cFrames;
            bool aborted = false;
            for (int k = 0; k < cFrames && !aborted; k++)
            {
                sr.sprite = frames[cr.from + k];
                for (float t = 0f; t < cFrameTime; t += Time.deltaTime)
                {
                    if (T == null) { aborted = true; break; }
                    Aim(T);
                    yield return null;
                }
            }
            if (aborted) continue;     // target lost mid-windup: back to idle, nothing spent

            // 3) ATTACK — charge as it shoots, shoot what it charges: draw from the grid live
            // and pop a mine every time another energyPerMine lands. A throttled grid thins the
            // spray in real time; the sub-mine remainder at the end is dropped, not banked.
            var ar = AttackRange;
            int aFrames = ar.to - ar.from + 1;
            float frameTime = attackTime / aFrames;
            // Nominal volley size at full supply — anchors each mine's spread pattern slot.
            int volley = Mathf.Max(1, Mathf.RoundToInt(maxRate * attackTime / Mathf.Max(1e-4f, energyPerMine)));
            float bucket = 0f;
            int spawned = 0;
            for (int k = 0; k < aFrames; k++)
            {
                sr.sprite = frames[ar.from + k];
                for (float t = 0f; t < frameTime; t += Time.deltaTime)
                {
                    if (T != null) Aim(T);
                    bucket += DrawStep(maxRate);
                    ReportEnergyDraw(maxRate);
                    while (bucket >= energyPerMine) { SpawnMine(spawned, volley); spawned++; bucket -= energyPerMine; }
                    yield return null;
                }
            }
            ClearEnergyStatus();

            // 4) COOLDOWN
            yield return new WaitForSeconds(Cooldown);
        }
    }

    void SpawnMine(int i, int total)
    {
        float frac = total > 0 ? (float)i / total : 0f;
        GameObject g = level >= 2
            ? SpawnManager.instance.NewP(p, transform, tag, 2.4f * (1.2f - frac), 24f * (1f - frac))
            : SpawnManager.instance.NewP(p, transform, tag, 1.8f * (1.2f - frac), 7f * (1f - frac));
        if (g != null)
            g.transform.localScale = Vector3.one * (level >= 2 ? Random.Range(1f, 1.3334f) : Random.Range(0.85f, 1.15f));
    }

    void Aim(Transform tgt)
    {
        Vector2 dir = ((Vector2)(tgt.position - transform.position)).normalized;
        transform.up = Vector2.Lerp(transform.up, dir, aimSpeed * Time.deltaTime);
    }

    /// <summary>Draws up to maxRate/sec this frame, capped by the grid's fair-share offer and stored energy.</summary>
    float DrawStep(float maxRate)
    {
        // Cap by the fair-share offer — MaxDrawThisFrame, NOT DrawRate*dt (raw rate overshoots
        // the per-consumer slice under contention and Power.Use rejects atomically) and NOT
        // PeekMaxDraw (a starved consumer whose offer is 0 would never REGISTER as a demander,
        // so the stores would keep offering first-in-update-order 100% forever). This call is
        // the consumer's once-per-frame demand registration; Use's internal caps peek.
        float step = Mathf.Min(maxRate * Time.deltaTime, Power.MaxDrawThisFrame(Time.deltaTime), Power.Energy);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }
}
