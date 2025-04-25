using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class DynamicSwordTrail : MonoBehaviour
{
    Rigidbody2D playerRigidbody;

    // Define the speed range. Adjust these values to match your game's mechanics.
    public float minSpeed = 0f;
    public float maxSpeed = 10f;

    // Trail properties that will be interpolated based on speed.
    public float minTrailTime = 0.1f;
    public float maxTrailTime = 0.5f;
    public float minWidthMultiplier = 0.1f;
    public float maxWidthMultiplier = 0.5f;

    private TrailRenderer trail;

    void Awake()
    {
        // Get the TrailRenderer attached to the sword.
        trail = GetComponent<TrailRenderer>();

        // If no player Rigidbody is assigned, try to find one in the parent object.
        if (playerRigidbody == null)
        {
            playerRigidbody = CharacterScript.CS.AS.rb;
        }
    }

    void Update()
    {
        float speed = playerRigidbody.velocity.magnitude;

        float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);

        trail.time = Mathf.Lerp(minTrailTime, maxTrailTime, t);

        // Optionally adjust the width multiplier for a more dramatic effect.
        trail.widthMultiplier = Mathf.Lerp(minWidthMultiplier, maxWidthMultiplier, t);
    }
}