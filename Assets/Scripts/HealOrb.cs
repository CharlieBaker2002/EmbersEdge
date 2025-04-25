using UnityEngine;

/// <summary>
/// HealOrb that orbits around the HealingPlatform. When chasing a target, it moves
/// in a homing fashion using LeanTween, updating its position each frame
/// with an easing curve (not purely linear).
/// </summary>
public class HealOrb : MonoBehaviour
{
    [HideInInspector] public HealingPlatform hp;
    [HideInInspector] public float rad = 1f;

    [Header("Components")]
    public SpriteRenderer sr;
    public Animator anim;
    public ParticleSystem ps;

    // Internal orbiting
    private float theta;
    private float orbitSpeed;
    private bool isSpinning = true;

    // Tweakable chase settings
    [Tooltip("How long (seconds) this orb tries to chase before finishing.")]
    public float chaseDuration = 2.0f;

    [Tooltip("Speed factor for homing while chasing.")]
    public float chaseSpeed = 8.0f;

    private void Start()
    {
        // Random initial orbit angle & speed
        theta = Random.Range(0f, 360f);
        orbitSpeed = Random.Range(45f, 75f) / rad;

        // Start with alpha = 0 -> fade in to 0.75
        sr.color = new Color(1f, 1f, 1f, 0f);
        LeanTween.value(gameObject, 0f, 0.75f, 1f).setOnUpdate((float alpha) =>
        {
            sr.color = new Color(1f, 1f, 1f, alpha);
        });
    }

    private void Update()
    {
        if (isSpinning && hp != null)
        {
            // Simple orbit motion around the platform
            theta += orbitSpeed * Time.deltaTime * Mathf.Deg2Rad;
            float x = Mathf.Cos(theta) * rad;
            float y = -Mathf.Sin(theta) * rad;
            transform.localPosition = new Vector3(x, y, 0f);
        }
    }

    /// <summary>
    /// Called by the platform to chase a unit. We do a homing tween that
    /// continually updates the orb's position toward the unit's current position.
    /// </summary>
    public void StartChase(Transform target, LifeScript ls)
    {
        isSpinning = false;
        anim.SetBool("On", true);
        ps.Play();

        // Cancel any orbit fade tween
        LeanTween.cancel(gameObject);

        // Homing tween: We'll tween a "timer" from 0..1, with an easing, 
        // and on each update move closer to the target's *current* position.
        LeanTween.value(gameObject, 0f, 1f, chaseDuration)
            .setEase(LeanTweenType.easeOutCubic)
            .setOnUpdate((float t) =>
            {
                if (target == null)
                {
                    // Target is gone; stop chasing
                    LeanTween.cancel(gameObject);
                    ReturnToPlatform();
                    return;
                }
                // Move a small step each frame, 
                // decreasing step size over time for a nice "ease-out" feel.
                float step = chaseSpeed * Time.deltaTime * (1f - t);
                transform.position = Vector3.MoveTowards(transform.position, target.position, step);
            })
            .setOnComplete(() =>
            {
                OnChaseComplete(target, ls);
            });
    }

    /// <summary>
    /// Called after chase tween finishes. Heal if possible, else return to platform.
    /// </summary>
    private void OnChaseComplete(Transform target, LifeScript ls)
    {
        // If missing references or the unit is fully healed, return
        if (target == null || ls == null || ls.hp >= ls.maxHp)
        {
            ReturnToPlatform();
        }
        else
        {
            // Apply a random heal 2..3
            ls.Change(Random.Range(2f, 3f), 0);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// If we can't heal, or the target vanished, go back to orbit around the platform.
    /// </summary>
    private void ReturnToPlatform()
    {
        LeanTween.cancel(gameObject);
        anim.SetBool("On", false);
        ps.Stop();

        if (hp == null)
        {
            // If platform is destroyed, just self-destruct
            Destroy(gameObject);
            return;
        }

        // Move home with a short tween, then re-add ourselves to the platform list
        float distance = Vector3.Distance(transform.position, hp.transform.position);
        float returnTime = distance / chaseSpeed; // Or some constant

        LeanTween.move(gameObject, hp.transform.position, returnTime)
            .setEase(LeanTweenType.easeOutCubic)
            .setOnComplete(() =>
            {
                // Place orb at a random orbit position
                Vector3 offset = GS.RandCircle(0.5f, 1.25f);
                transform.position = hp.transform.position + offset;

                // Re-add ourselves to the platform’s list
                hp.healOrbs.Add(this);
                hp.UpdateSprite();

                // Resume spinning
                isSpinning = true;
            });
    }
}