using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debris scattered by every broken dungeon wall (OreChips_0..3 small / 4..7 big / 8..11 large,
/// element-lit when the wall carried ore). Pure scatter: no collider, no value yet — bag drones
/// haul them home as scrap; anything left despawns when the player returns to base.
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

    /// <summary>Bag space this chip occupies (small 1 / big 2 / large 3).</summary>
    public int SpaceCost => sizeClass + 1;

    float born;

    void OnEnable()
    {
        all.Add(this);
        born = Time.time;
        transform.localScale = Vector3.zero;
    }

    void OnDisable() => all.Remove(this);

    void Update()
    {
        // pop-in without a tween (LeanTween pool pressure — chips can number in the hundreds)
        float t = (Time.time - born) * 4f;
        if (t < 1f) transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, t);
        else if (transform.localScale.x < 1f) transform.localScale = Vector3.one;
    }
}
