using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns an existing ally unit (ClawBot, A0_1 fighter ship) into a DORMANT HULL that only runs
/// while crewed by drones. Buying from the factory now just spawns the vehicle; it activates when
/// its full crew of energised drones is aboard.
///
/// Mechanism: Awake (which runs before any Start) disables the brain component, so the brain's
/// Start/coroutines never launch until first activation. After that, the brain polls
/// <see cref="Active"/> at the top of its loop (2-line gate in ClawBot/A0_1) so deactivation
/// between waves is cheap and reversible. While dormant: rigidbody unsimulated (no collision, no
/// targeting), grey tint, and exempt from base enemy seeding.
///
/// Wave cycle (driven by DroneManager): PreAttack → pilots board (drones park inside, deactivated);
/// wave complete → pilots pop out, heal the hull, then fly home to recharge. Hull destroyed →
/// pilots pop out UNSCATHED (they were parked, untouched) and take evasive action.
/// </summary>
public class PilotedVehicle : MonoBehaviour, IOnDeath
{
    public static readonly List<PilotedVehicle> all = new List<PilotedVehicle>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    [Tooltip("Drones needed aboard before the vehicle wakes (fighter ship 1, clawbot 2).")]
    public int pilotsRequired = 1;
    public Color dormantTint = new Color(0.45f, 0.45f, 0.5f, 0.9f);

    /// <summary>Assigned pilots (aboard or en route). Boarded ones are also in <see cref="aboard"/>.</summary>
    public readonly List<Drone> crew = new List<Drone>();
    readonly List<Drone> aboard = new List<Drone>();

    public bool Active { get; private set; }
    public LifeScript Ls { get; private set; }

    Behaviour brain;
    Rigidbody2D rb;
    SpriteRenderer[] srs;
    Color[] srColors;

    void Awake()
    {
        brain = GetComponent<AllyAI>();          // ClawBot / A0_1 — disabled BEFORE its Start runs
        if (brain != null) brain.enabled = false;
        Ls = GetComponent<LifeScript>();
        if (Ls == null) Ls = GetComponentInChildren<LifeScript>();
        if (Ls != null && !Ls.onDeaths.Contains(this)) Ls.onDeaths.Add(this);
        rb = GetComponent<Rigidbody2D>();
        srs = GetComponentsInChildren<SpriteRenderer>(true);
        srColors = new Color[srs.Length];
        for (int k = 0; k < srs.Length; k++) srColors[k] = srs[k].color;
        SetDormantPresentation(true);
        BasePathManager.UntargetableAllies.Add(transform);
    }

    void OnEnable() => all.Add(this);

    void OnDisable() => all.Remove(this);

    void OnDestroy() => BasePathManager.UntargetableAllies.Remove(transform);

    public bool NeedsPilots => crew.Count < pilotsRequired;

    public void AssignPilot(Drone d)
    {
        if (d == null || crew.Contains(d)) return;
        crew.Add(d);
        d.pilotOf = this;
        d.SetEquipment(DroneEquipment.Pilot);
    }

    public void RemovePilot(Drone d)
    {
        crew.Remove(d);
        aboard.Remove(d);
        if (d != null && d.pilotOf == this) d.pilotOf = null;
    }

    /// <summary>A pilot touched the hull: park it inside (deactivated, untouchable). Wakes the
    /// vehicle once the full crew is aboard.</summary>
    public void Board(Drone d)
    {
        if (d == null || !crew.Contains(d) || aboard.Contains(d)) return;
        aboard.Add(d);
        d.gameObject.SetActive(false);
        d.transform.position = transform.position;
        if (aboard.Count >= pilotsRequired) Activate();
    }

    void Activate()
    {
        if (Active) return;
        Active = true;
        SetDormantPresentation(false);
        BasePathManager.UntargetableAllies.Remove(transform);
        if (brain != null) brain.enabled = true;   // first time: Unity now runs its Start
    }

    /// <summary>Between waves: idle the brain and pop the pilots out beside the hull so they can
    /// heal it and fly home to recharge.</summary>
    public void WaveEnded()
    {
        if (!Active)
        {
            // dormant hull with pilots stuck en route — send them to heal/recharge anyway
            ReleasePilotsForDowntime();
            return;
        }
        Active = false;   // brains gate on this at the top of their loops
        SetDormantPresentation(true, keepSimulated: true);
        BasePathManager.UntargetableAllies.Add(transform);
        ReleasePilotsForDowntime();
    }

    void ReleasePilotsForDowntime()
    {
        for (int k = aboard.Count - 1; k >= 0; k--) PopOut(aboard[k], Drone.State.HealVehicle);
        for (int k = crew.Count - 1; k >= 0; k--)
        {
            var d = crew[k];
            if (d != null && d.gameObject.activeInHierarchy && d.state == Drone.State.BoardingVehicle)
                d.state = Drone.State.HealVehicle;
        }
    }

    /// <summary>Wave incoming: everyone back in.</summary>
    public void WaveStarting()
    {
        for (int k = crew.Count - 1; k >= 0; k--)
        {
            var d = crew[k];
            if (d == null) { crew.RemoveAt(k); continue; }
            if (!aboard.Contains(d) && d.gameObject.activeInHierarchy)
                d.state = Drone.State.BoardingVehicle;
        }
    }

    void PopOut(Drone d, Drone.State next)
    {
        aboard.Remove(d);
        if (d == null) return;
        d.transform.position = transform.position + GS.RandCircle(0.4f, 0.8f);
        d.gameObject.SetActive(true);
        d.OnThaw();   // SetActive(false) killed the brain coroutine; relaunch it
        d.state = next;
    }

    /// <summary>Hull destroyed: pilots spawn again, unscathed (they were parked and untouchable),
    /// and take evasive action — empty-handed drones attract nothing.</summary>
    public void OnDeath()
    {
        for (int k = aboard.Count - 1; k >= 0; k--) PopOut(aboard[k], Drone.State.Fleeing);
        for (int k = crew.Count - 1; k >= 0; k--)
        {
            var d = crew[k];
            if (d != null)
            {
                d.pilotOf = null;
                if (d.equipment == DroneEquipment.Pilot) d.SetEquipment(DroneEquipment.None);
            }
        }
        crew.Clear();
        BasePathManager.UntargetableAllies.Remove(transform);
    }

    void SetDormantPresentation(bool dormant, bool keepSimulated = false)
    {
        if (rb != null && !keepSimulated) rb.simulated = !dormant;
        else if (rb != null && dormant) rb.linearVelocity = Vector2.zero;
        for (int k = 0; k < srs.Length; k++)
        {
            if (srs[k] == null) continue;
            srs[k].color = dormant ? dormantTint * srColors[k] : srColors[k];
        }
    }
}
