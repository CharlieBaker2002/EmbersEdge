using System.Collections;
using UnityEngine;

/// <summary>
/// Push Tower. Finds a target, CHARGES (drawing energy from the grid up to a capped rate while a
/// bar fills), then fires a fan of push projectiles. Script-driven (no Animator): the charge bar's
/// fill frames are walked by code, paced by the energy actually drawn — a throttled grid stretches
/// the charge (and shows the "insufficient" overlay), an empty grid won't charge at all.
///
/// Charge costs <see cref="chargeCost"/> energy over <see cref="chargeTime"/> seconds (2 / 1s),
/// i.e. a desired draw of 2/s; the spread fires once the charge completes.
/// </summary>
public class SpreadTower : Building
{
    [SerializeField] Finder find;
    [Range(1, 3)] float mode;
    int level = 1;
    [SerializeField] GameObject proj;
    float strength = 1;
    float spread;
    [SerializeField] Transform[] sp;
    [SerializeField] Sprite[] spr;             // tower body per (level,mode): (level-1)*3 + mode-1
    [SerializeField] Sprite[] tileSprites;
    [SerializeField] private SpriteRenderer bar;
    [SerializeField] private Sprite[] barFrames;          // charge bar fill, empty -> full (level 1)
    [SerializeField] private Sprite[] barFramesUpgraded;  // charge bar fill, empty -> full (level 2)
    [SerializeField] Sprite morphSprite;
    [SerializeField] private Sprite baseUpgradeSprite;

    [Header("Charge")]
    [Tooltip("Energy drawn to charge one shot.")]
    [SerializeField] private float chargeCost = 2f;
    [Tooltip("Seconds to charge at full supply (the floor; stretches if the grid is slow).")]
    [SerializeField] private float chargeTime = 1f;
    [Tooltip("Seconds the bar takes to drop back to empty after firing.")]
    [SerializeField] private float dischargeTime = 0.2f;
    [SerializeField] private float aimSpeed = 8f;

    private Coroutine loop;
    private float charge;   // energy stored toward chargeCost; held across attempts, only spent on fire

    /// <summary>One shot's charge, and the rate that charges it in chargeTime (2 and 2/s).</summary>
    public override float PeakEnergyDemand => Mathf.Max(chargeCost, chargeCost / chargeTime);

    private Sprite[] BarFrames => level >= 2 ? barFramesUpgraded : barFrames;

    void SetMode(float x)
    {
        mode = x;
        find.refresh = (4 - level) * 0.25f * (1 + mode) + 0.25f;
        strength = level * mode;
        find.radius = (level + 0.5f) * (mode + 0.5f) + 0.5f;
        spread = (((4 - mode) - level + 1) / 3.2f) + 0.0625f;
        if (level == 2) { spread *= 0.66f; spread += 0.1f; }
        sr.sprite = spr[(int)((level - 1) * 3 + mode - 1)];
    }

    void LevelUp()
    {
        level = 2;
        SetMode(mode);
        find.refresh /= 2f;
        transform.parent.GetComponent<SpriteRenderer>().sprite = baseUpgradeSprite;
    }

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
        ClearEnergyStatusImmediate();
    }

    public override void Start()
    {
        base.Start();
        bar.enabled = true;
        SetMode(1);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Change Mode", tileSprites[0], false, SwapMode);
        AddUpgradeSlot(new int[] { 55, 0, 3, 0 }, "Spread Tower Upgrade", tileSprites[1], true, LevelUp, 7, true, StartMorph);
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
    }

    private void StartMorph() => GS.QuickMorphWithOrbs(gameObject, morphSprite, transform.parent);

    public override void OnDeath()
    {
        transform.parent.GetComponent<SpriteRenderer>().color = new Color(200, 200, 200, 1);
        base.OnDeath();
    }

    private void SwapMode()
    {
        mode += 1;
        if (mode == 4) mode = 1;
        SetMode(mode);
    }

    // ---- idle (holding charge) -> charge -> fire spread -> discharge -> cooldown ----
    // Energy drawn while charging is STORED in `charge` and only spent when the spread fires, so a
    // target lost mid-charge never wastes it — the bar just holds until a target returns.
    IEnumerator Run()
    {
        bar.sprite = BarFrames[0];
        while (true)
        {
            Sprite[] bf = BarFrames;

            if (charge >= chargeCost - 1e-4f)
            {
                // Fully charged: fire at a target; otherwise hold the full bar.
                if (find.T == null) { bar.sprite = bf[bf.Length - 1]; ClearEnergyStatus(); yield return null; continue; }
                ClearEnergyStatus();
                bar.sprite = bf[bf.Length - 1];
                Vector2 dir = ((Vector2)(find.T.position - transform.position)).normalized;
                if (dir != Vector2.zero) transform.up = dir;   // point at the current target (a held charge may have aimed elsewhere)
                Shoot();
                charge = 0f;
                for (int k = bf.Length - 1; k >= 0; k--)
                {
                    bar.sprite = bf[k];
                    for (float d = 0f; d < dischargeTime / bf.Length; d += Time.deltaTime) yield return null;
                }
                yield return new WaitForSeconds(find.refresh);
                continue;
            }

            if (find.T != null && Power.Energy > 1e-3f)
            {
                // Charge: aim and draw toward chargeCost, clamped so we never pull more than the
                // shot needs. The rate cap (chargeCost/chargeTime) enforces the ~1s minimum.
                Aim(find.T);
                charge += DrawStep(chargeCost / chargeTime, chargeCost - charge);
                ReportEnergyDraw(chargeCost / chargeTime);
                bar.sprite = bf[BarFrame(bf)];
                yield return null;
                continue;
            }

            // Idle: HOLD the stored charge. Flag "no energy" if a target wants more but the grid's dry.
            bar.sprite = bf[BarFrame(bf)];
            if (find.T != null) ReportEnergyDraw(chargeCost / chargeTime); else ClearEnergyStatus();
            yield return null;
        }
    }

    int BarFrame(Sprite[] bf) => Mathf.Min(bf.Length - 1, Mathf.FloorToInt(Mathf.Clamp01(charge / chargeCost) * bf.Length));

    void Aim(Transform t)
    {
        Vector2 dir = ((Vector2)(t.position - transform.position)).normalized;
        transform.up = Vector2.Lerp(transform.up, dir, aimSpeed * Time.deltaTime);
    }

    /// <summary>Draws up to maxRate/sec this frame, capped by grid rate, stored energy, and remaining capacity.</summary>
    float DrawStep(float maxRate, float remaining)
    {
        if (remaining <= 1e-5f) return 0f;
        // Fair-share cap + demand registration — see ChargerTurret.DrawStep for why this must
        // be MaxDrawThisFrame (not DrawRate*dt, not PeekMaxDraw).
        float step = Mathf.Min(maxRate * Time.deltaTime, Power.MaxDrawThisFrame(Time.deltaTime), Power.Energy, remaining);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }

    public void Shoot()
    {
        int i = 0;
        for (float ang = -60f; ang <= 60f; ang += 20f)
        {
            GS.NewP(proj, sp[i], tag, GS.Rotated(transform.up, -ang * spread), 0.05f, strength);
            i++;
        }
    }
}
