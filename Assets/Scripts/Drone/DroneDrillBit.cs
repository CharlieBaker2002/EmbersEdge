using UnityEngine;

/// <summary>
/// The drill hanging under a drone (child sprite, DroneDrill_0..8 frames, script-driven — no
/// Animator). Spins and VIBRATES while grinding rock or fighting; also delivers the rally-fight
/// contact damage-over-time in coarse ticks. The drone body rotates to aim, so the bit only
/// jitters in local space (Melee's bob idiom, miniaturised).
/// </summary>
public class DroneDrillBit : MonoBehaviour
{
    public SpriteRenderer sr;
    public float frameRate = 18f;
    [Tooltip("Local-space vibration amplitude while grinding.")]
    public float jitterAmp = 0.03f;
    [Tooltip("Contact damage per second while rally-fighting.")]
    public float dps = 2f;
    [Tooltip("Reach of the contact damage from the drill tip.")]
    public float hitRadius = 0.55f;

    Drone owner;
    Sprite[] frames;
    Vector3 restPos;
    float frameT;
    int frameIndex;
    bool active;
    float dotTimer;

    void Awake()
    {
        owner = GetComponentInParent<Drone>();
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        frames = DroneManager.LoadStripNumeric("DroneDrill");
        restPos = transform.localPosition;
        if (sr != null && frames.Length > 0 && sr.sprite == null) sr.sprite = frames[0];
    }

    /// <summary>Grinding/fighting on = spin + vibrate; off = settle back to rest.</summary>
    public void SetActiveDrilling(bool on)
    {
        if (active == on) return;
        active = on;
        if (!on) transform.localPosition = restPos;
    }

    void Update()
    {
        if (!active || sr == null || frames == null || frames.Length == 0) return;
        float rate = owner != null ? owner.ActRateVisible : 1f;
        frameT += Time.deltaTime * frameRate * DroneManager.Haste * rate;
        if (frameT >= 1f)
        {
            frameT -= 1f;
            frameIndex = (frameIndex + 1) % frames.Length;
            sr.sprite = frames[frameIndex];
        }
        transform.localPosition = restPos + (Vector3)(jitterAmp * Random.insideUnitCircle);
    }

    /// <summary>Rally-fight damage tick — call every physics frame while engaged; damage lands in
    /// 0.4s bites so the FX/number spam stays sane. Returns true when something was hit.</summary>
    public bool CombatTick()
    {
        if (owner == null) return false;
        dotTimer -= Time.fixedDeltaTime;
        if (dotTimer > 0f) return false;
        float tick = 0.4f;
        dotTimer = tick / Mathf.Max(0.2f, DroneManager.Haste);
        Vector3 tip = transform.position - transform.up * 0.15f;
        var hits = GS.FindEnemies(owner.tag, tip, hitRadius, false, false);
        bool any = false;
        foreach (Transform t in hits)
        {
            var ls = t.GetComponentInParent<LifeScript>();
            if (ls == null || ls.hasDied) continue;
            ls.Change(-dps * tick, 2);
            any = true;
        }
        return any;
    }
}
