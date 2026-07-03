using UnityEngine;

/// <summary>
/// HUD readout for melee/drill durability, modelled on AmmoSlider (both extend CoreSlider).
/// Tracks the first equipped melee (most loadouts run one). Safe if no melee is equipped.
/// Wire its CoreSlider fields (fill/bas/txt) on a prefab in the scene, like the ammo slider.
/// </summary>
public class DurabilitySlider : CoreSlider
{
    public static DurabilitySlider i;

    private void Awake()
    {
        i = this;
    }

    public void InitMelee(Melee m)
    {
        if (m == null) return;
        UpdateMax(Mathf.Max(0.01f, m.MaxDurability));
        UpdateSlider(Mathf.Max(0f, m.Durability));
    }

    /// <summary>Re-read the tracked melee's durability (called on drain and on round reset).</summary>
    public void Refresh()
    {
        if (Melee.melees.Count == 0)
        {
            UpdateSlider(0f);
            return;
        }
        InitMelee(Melee.melees[0]);
    }
}
