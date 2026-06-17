using System.Collections;
using UnityEngine;

/// <summary>
/// Force Field tower. On build it weaves an EnergyWall between two endpoints and then
/// trickle-charges it from the grid at <see cref="chargeRate"/> energy/sec (max 3 energy);
/// each energy is worth <see cref="hpPerCharge"/> wall hp (3 * 10 = the wall's 30 max).
/// The wall IS the charge store: damage to the wall is charge lost, and mid-battle
/// charging only crawls. If the wall falls entirely, the tower re-weaves a fresh one
/// once it has banked 1 energy again.
///
/// Shaping: click-drag from the tower (Connectable, like pylon cables) and release
/// anywhere — the nearest wall endpoint re-weaves to the release point (clamped to
/// reach). Re-weaving takes <see cref="reshapeTime"/> seconds during which the wall
/// is down (collider off, dimmed), so reshaping mid-fight is a real commitment.
/// A plain click still opens the building UI.
/// </summary>
public class ForceField : Building
{
    [Header("Force Field")]
    [SerializeField] private Sprite[] chargeSprs;     // gem fill by stored charge, empty -> full
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private Connectable connectable;
    [SerializeField] private float chargeRate = 0.25f;
    [SerializeField] private float hpPerCharge = 10f;
    [SerializeField] private float maxReach = 3.5f;
    [SerializeField] private float minSeparation = 0.8f;
    [SerializeField] private float maxLength = 6f;
    [SerializeField] private float reshapeTime = 4f;
    [SerializeField] private Vector2 endA = new Vector2(-1.2f, 1.4f); // local offsets from the tower
    [SerializeField] private Vector2 endB = new Vector2(1.2f, 1.4f);

    private EnergyWall wall;
    private Coroutine loop;
    private float rebuildCharge;

    public override void Start()
    {
        base.Start();
        WireConnectable();
    }

    protected override void BEnable()
    {
        if (wall == null)
        {
            SpawnWall(2f); // a thin seed wall comes up with the tower
        }
        if (loop == null) loop = StartCoroutine(Run());
    }

    protected override void BDisable()
    {
        if (loop != null) { StopCoroutine(loop); loop = null; }
        if (wall != null)
        {
            Destroy(wall.gameObject);
            wall = null;
        }
        rebuildCharge = 0f;
        ClearEnergyStatusImmediate();
    }

    private void WireConnectable()
    {
        if (connectable == null) return;
        connectable.Validate = _ => false;                       // never cable-snap; every drag reshapes
        connectable.OnDraggedRelease = ReshapeTo;
        connectable.OnClickWithoutDrag = ToggleUI;
        connectable.OnRejected = _ => { };
    }

    public override void OnClick()
    {
        // Press fires this; defer the UI toggle until release so Connectable can drag.
        if (!enabled) return;
        if (connectable == null || !connectable.BeginDrag())
        {
            base.OnClick();
        }
    }

    private void ToggleUI()
    {
        if (!UIParent.activeInHierarchy && canOpen) OnOpen?.Invoke();
        else OnClose?.Invoke();
    }

    private void SpawnWall(float hp)
    {
        var g = Instantiate(wallPrefab, transform.position, Quaternion.identity, transform);
        wall = g.GetComponent<EnergyWall>();
        wall.Init(World(endA), World(endB), hp);
    }

    private Vector2 World(Vector2 local) => (Vector2)transform.position + local;

    // ---- trickle charge: 0.25 energy/sec -> 2.5 hp/sec; full wall ~12s of supply ----
    private IEnumerator Run()
    {
        while (true)
        {
            if (wall == null)
            {
                // Wall fell: bank charge from zero, re-weave once one full energy is stored.
                float step = DrawStep(chargeRate, 1f - rebuildCharge);
                rebuildCharge += step;
                ReportEnergyDraw(chargeRate);
                SetChargeSprite(rebuildCharge);
                if (rebuildCharge >= 1f - 1e-4f)
                {
                    SpawnWall(rebuildCharge * hpPerCharge);
                    rebuildCharge = 0f;
                }
            }
            else
            {
                float deficit = wall.MaxHp - wall.Hp;
                if (deficit > 0.01f && Power.Energy > 1e-3f)
                {
                    float drawn = DrawStep(chargeRate, deficit / hpPerCharge);
                    if (drawn > 0f) wall.Heal(drawn * hpPerCharge);
                    ReportEnergyDraw(chargeRate);
                }
                else if (deficit > 0.01f)
                {
                    ReportEnergyDraw(chargeRate); // wants charge, grid is dry -> overlay
                }
                else
                {
                    ClearEnergyStatus();
                }
                SetChargeSprite(wall.Hp / hpPerCharge);
            }
            yield return null;
        }
    }

    private void SetChargeSprite(float charge)
    {
        if (chargeSprs == null || chargeSprs.Length == 0 || sr == null) return;
        sr.sprite = GS.PercentParameter(chargeSprs, Mathf.Clamp01(charge / 3f));
    }

    /// <summary>Draws up to maxRate/sec this frame, capped by grid rate, stored energy, and remaining capacity.</summary>
    private float DrawStep(float maxRate, float remaining)
    {
        if (remaining <= 1e-5f) return 0f;
        float step = Mathf.Min(maxRate * Time.deltaTime, Power.DrawRate * Time.deltaTime, Power.Energy, remaining);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }

    // ---- shaping ----
    private void ReshapeTo(Vector3 release)
    {
        Vector2 p = ClampReach(release);
        bool moveA = (p - World(endA)).sqrMagnitude <= (p - World(endB)).sqrMagnitude;
        Vector2 other = moveA ? endB : endA;

        Vector2 local = p - (Vector2)transform.position;
        // keep the endpoints apart and the wall under its max length
        Vector2 sep = local - other;
        if (sep.magnitude < minSeparation)
        {
            local = other + (sep.sqrMagnitude > 1e-4f ? sep.normalized : Vector2.up) * minSeparation;
        }
        else if (sep.magnitude > maxLength)
        {
            local = other + sep.normalized * maxLength;
        }

        if (moveA) endA = local; else endB = local;
        if (wall != null)
        {
            wall.Reweave(World(endA), World(endB), reshapeTime);
        }
    }

    private Vector2 ClampReach(Vector2 world)
    {
        Vector2 d = world - (Vector2)transform.position;
        float m = Mathf.Clamp(d.magnitude, 0.6f, maxReach);
        return (Vector2)transform.position + (d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.up) * m;
    }
}
