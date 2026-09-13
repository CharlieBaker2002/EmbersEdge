using UnityEngine;

/// <summary>
/// RETIRED (2026-09-11). The time-driven random pixel decompression this used to run on freshly
/// placed buildings is replaced by <see cref="GhostIntake"/>, which prints a building's art in
/// as ORE ARRIVES (the BlueprintFill2D shader — no readable textures, no Sprite.Create churn).
/// The class stays because 17 prefabs/scenes still reference the component; it does nothing.
/// </summary>
public class FastSpriteDecompressor : MonoBehaviour
{
    [SerializeField] float decompressionTime = 10f;   // legacy serialised value, unused

    void Start()
    {
        enabled = false;
    }
}
