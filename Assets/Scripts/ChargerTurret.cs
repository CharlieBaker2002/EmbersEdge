using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mine Sprayer. Finds a target, then CHARGES — drawing energy from the grid at a capped rate
/// (3/s base, 6/s upgraded) — and ATTACKS for one second, spraying a number of mines proportional
/// to how much energy it managed to draw. A throttled grid means a smaller draw and fewer mines.
///
/// Script-driven (no Animator). The TurretCharger sheet (46 frames) is laid out:
///   base    : charge 0..7,  attack 8..22
///   upgrade : charge 23..30, attack 31..45
/// Energy is spent during the charge, not the attack — you must find a target before charging.
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
    [Tooltip("Seconds spent charging (and drawing energy) before each attack.")]
    [SerializeField] private float chargeTime = 1f;
    [Tooltip("Seconds the attack spray lasts.")]
    [SerializeField] private float attackTime = 1f;
    [SerializeField] private float cooldown = 5f;
    [SerializeField] private float cooldownUpgrade = 2.8f;
    [SerializeField] private float aimSpeed = 8f;

    [Header("Energy")]
    [Tooltip("Max energy/sec drawn while charging (no upgrade). The grid may supply less -> fewer mines.")]
    [SerializeField] private float drawRate = 3f;
    [Tooltip("Max energy/sec drawn while charging once Thick Spreader is bought.")]
    [SerializeField] private float drawRateUpgrade = 6f;
    [Tooltip("Energy per mine: mines fired = energy drawn this charge / this.")]
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
    private float MaxCharge => MaxDrawRate * chargeTime;   // a full charge's worth of energy
    private float charge;   // energy stored toward the next volley; held across attempts, spent on fire

    public override void Start()
    {
        base.Start();
        find.OnFound += GetTarget;
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
        AddUpgradeSlot(new int[] { 100, 0, 5, 0 }, "Thick Spreader", tileSprite, true, LevelUp, 11, true, CallMorph);
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

    // ---- State machine: idle -> charge (draw) -> attack (spray) -> cooldown ------------------

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
            // 1) IDLE — wait for a target. Idle only when there's nothing stored AND nothing to
            // draw; a held charge fires the moment a target appears (even on a dry grid).
            while (T == null || (Power.Energy <= 1e-3f && charge <= 1e-3f))
            {
                sr.sprite = frames[ChargeRange.from];
                if (T != null && charge <= 1e-3f) ReportEnergyDraw(MaxDrawRate); else ClearEnergyStatus();
                yield return null;
            }

            // 2) CHARGE — windup while drawing toward a full charge. Draw RESUMES from whatever is
            // stored and is capped at MaxCharge, so nothing is wasted or over-pulled; if the target
            // is lost mid-charge we keep what we've stored rather than discarding it.
            var cr = ChargeRange;
            float maxRate = MaxDrawRate;
            float maxCharge = MaxCharge;
            int cFrames = cr.to - cr.from + 1;
            float cFrameTime = chargeTime / cFrames;
            float drawn = charge;
            bool aborted = false;
            for (int k = 0; k < cFrames && !aborted; k++)
            {
                sr.sprite = frames[cr.from + k];
                for (float t = 0f; t < cFrameTime; t += Time.deltaTime)
                {
                    if (T == null) { aborted = true; break; }
                    Aim(T);
                    drawn += DrawStep(maxRate, maxCharge - drawn);
                    if (drawn < maxCharge - 1e-4f) ReportEnergyDraw(maxRate); else ClearEnergyStatus();
                    yield return null;
                }
            }
            ClearEnergyStatus();
            charge = drawn;            // hold whatever we've drawn — never wasted
            if (aborted) continue;     // target lost: keep the charge for next time

            // 3) ATTACK — mines proportional to the energy stored, then the charge is spent.
            int mines = Mathf.FloorToInt(charge / Mathf.Max(1e-4f, energyPerMine));
            charge = 0f;
            yield return AttackSpray(AttackRange, mines, attackTime);

            // 4) COOLDOWN
            yield return new WaitForSeconds(Cooldown);
        }
    }

    IEnumerator AttackSpray((int from, int to) range, int mineCount, float duration)
    {
        int aFrames = range.to - range.from + 1;
        float frameTime = duration / aFrames;
        int spawned = 0;
        for (int k = 0; k < aFrames; k++)
        {
            sr.sprite = frames[range.from + k];
            // Spread the spawns evenly across the attack frames.
            int wantedSoFar = Mathf.RoundToInt((float)(k + 1) / aFrames * mineCount);
            while (spawned < wantedSoFar) { SpawnMine(spawned, mineCount); spawned++; }
            for (float t = 0f; t < frameTime; t += Time.deltaTime)
            {
                if (T != null) Aim(T);
                yield return null;
            }
        }
        while (spawned < mineCount) { SpawnMine(spawned, mineCount); spawned++; }
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

    /// <summary>Draws up to maxRate/sec this frame, capped by grid rate, stored energy, and remaining capacity.</summary>
    float DrawStep(float maxRate, float remaining)
    {
        if (remaining <= 1e-5f) return 0f;
        float step = Mathf.Min(maxRate * Time.deltaTime, Power.DrawRate * Time.deltaTime, Power.Energy, remaining);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }
}
