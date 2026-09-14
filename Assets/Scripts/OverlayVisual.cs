using UnityEngine;

/// <summary>
/// Marker on the root of a code-built overlay (health bar, energy gauges) that hangs under a
/// building's transform for lifetime only. Anything that measures or restyles a building's own
/// sprites (the blueprint print in GhostIntake) skips renderers under one of these — a gauge
/// hanging off the far edge of a Tube shape must not drag the print's ring centre with it.
/// </summary>
public sealed class OverlayVisual : MonoBehaviour
{
    /// <summary>True when the renderer belongs to an overlay, not to the building's art.</summary>
    public static bool Owns(Component c) => c != null && c.GetComponentInParent<OverlayVisual>() != null;
}
