using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Melee : Part
{
    public static List<Melee> melees = new List<Melee>();
    private bool canUse = true;
    [Header("General")]
    [SerializeField] private TrailRenderer trail;
    [SerializeField] ParticleSystem swingVFX;
    [SerializeField] Sprite[] animateSprites;
    [SerializeField] private Collider2D col;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float returnTime = 2f;
    private float origMag;
    public enum AttackType {Spin, Thrust, Slash}
    [SerializeField] AttackType attackType = AttackType.Spin;
    [Header("Spin / Slash Attack")]
    [SerializeField] private float accelerateOmega = 480f;
    [SerializeField] private float swingAngle = 120f;
    [SerializeField] private float swingOmega = 180f;
    [SerializeField] private int slashes = 1;
    [Header("Thrust / Slash")]
    [SerializeField] float extraWait;
    private float extendTimePerSlash;
 

    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        extendTimePerSlash = swingAngle / swingOmega;
        trail.emitting = false;
        melees.Add(this);
        col.enabled = false;
        engagement = 1f;
    }

    public override void StopPart(MechaSuit mecha)
    {
        base.StopPart(mecha);
        melees.Remove(this);
        col.enabled = false;
    }

    public static bool TryAttack()
    {
        if (melees.Count == 0) return false;

        foreach (Melee weapon in melees)
        {
            if (weapon.canUse)
            {
                weapon.Attack();
                return true;
            }
        }

        return false;
    }

    public static void ResetWeapons()
    {
        foreach (Melee weapon in melees)
        {
            weapon.canUse = true;
            weapon.engagement = 0.5f;
        }
    }

    private void Attack()
    {
        if (CharacterScript.CS.dashTimer <= 0f)
        {
            CharacterScript.CS.dashTimer = CharacterScript.CS.maxDashTimer;
        }
        cd.SetValue(CharacterScript.CS.dashTimer);
        canUse = false;
        engagement = 1f;
        StartCoroutine(AttackSequence());
    }

    private IEnumerator AttackSequence()
    {
        //Because of the rotate around in a ring general motion, the melee could be in any position around the player. 
        //First the melee needs to leave the ring to prevent kinematic motion being unpredicatable & begin the animation.
        //Secondarily, the melee needs to accelerate omega massively until reaching -degSlash / 2 from the necessary rotation that is the aim direction & and begin the animation.
        //Thirdly, the melee needs to turn on the collider, activate dynamic renderer, the particle system
        //Fourthly, the melee needs to rotate through the degSlash.
        //Fifthly, the melee needs to turn off the collider, & the particle system.
        //Sixthly, the melee needs to return to the ring, & make the dynamic trail renderer width reduce back to zero, and play the animation in reverse.
        //1
        Coroutine dep = Deploy(transform.position, 0f, Mathf.Infinity, 0f);
        sr.LeanAnimate(animateSprites, 0.25f);
        //2 
        yield return null;
        transform.parent = Instantiate(Resources.Load<GameObject>("Empty"), CharacterScript.CS.transform.position, CharacterScript.CS.transform.rotation, GS.FindParent(GS.Parent.misc)).transform;
        if (attackType != AttackType.Slash)
        {
            transform.parent.gameObject.AddComponent<FollowCharacter>();
        }

        // Get the desired aim direction
        Quaternion goalAngle = CharacterScript.aimQ;
        Quaternion currentAngleQ = GS.VTQ(transform.position - transform.parent.position);

        if (attackType == AttackType.Spin)
        {
            yield return StartCoroutine(SpinAttack(goalAngle, currentAngleQ));
        }
        else if(attackType == AttackType.Thrust)
        {
            yield return StartCoroutine(ThrustAttack());
        }
        else if(attackType == AttackType.Slash)
        {
            yield return StartCoroutine(SlashAttack(goalAngle, currentAngleQ));
        }
        
        StopCoroutine(dep);
        engagement = 0f;
        sr.LeanAnimate(animateSprites, 0.5f, false, true);
        yield return StartCoroutine(Return(returnTime));
    }
    
    private IEnumerator SlashAttack(Quaternion goalAngle, Quaternion currentAngleQ)
    {
        float angleToRotate = Vector2.SignedAngle(GS.QTV(currentAngleQ), GS.QTV(goalAngle));
        Vector3 forwardsOrBackwards = angleToRotate < 0f ? -Vector3.forward : Vector3.forward;
        angleToRotate = Mathf.Abs(angleToRotate);
        angleToRotate -= swingAngle/2f;
        if (angleToRotate < 0f)
        {
            angleToRotate = 0f;
        }
        origMag = transform.position.normalized.magnitude;

        // Rotate to starting position
        for (float rotated = 0f; rotated < angleToRotate; rotated += accelerateOmega * Time.deltaTime)
        {
            transform.parent.Rotate(forwardsOrBackwards, accelerateOmega * Time.deltaTime);
            yield return null;
        }
        
        // Multiple slashes in alternating directions
        for (int i = 0; i < slashes; i++)
        {
            StartCoroutine(ExtendSlash(i));
            // Switch direction for each slash
            Vector3 slashDirection = (i % 2 == 0) ? forwardsOrBackwards : -forwardsOrBackwards;
            
            // Start effects
            swingVFX.Play();
            col.enabled = true;
            trail.emitting = true;
            
            // Execute the slash
            for (float swingRotation = 0f; swingRotation < swingAngle; swingRotation += Time.deltaTime * swingOmega)
            {
                transform.parent.Rotate(slashDirection, swingOmega * Time.deltaTime);
                transform.up = transform.position - transform.parent.position;
                yield return null;
            }
            
            // Stop effects
            swingVFX.Stop();
            col.enabled = false;
            
            // Rest between slashes (if not the last slash)
            if (i < slashes - 1)
            {
                for (float t = 0f; t < extraWait; t += Time.deltaTime / extraWait)
                {
                    float swingBuf = 0.5f * Mathf.Lerp(swingOmega, 0f, Mathf.Sqrt(t));
                    //trail.time = Mathf.Lerp(prev, 0f, t);
                    transform.parent.Rotate(slashDirection, swingBuf * Time.deltaTime);
                    transform.up = transform.position - transform.parent.position;
                    yield return null;
                }
            }
            else
            {
                // End of slash trail fading
                float prev = trail.time;
                for (float t = 0f; t < 1f; t += Time.deltaTime * 2f)
                {
                    float swingBuf = 0.5f * Mathf.Lerp(swingOmega, 0f, Mathf.Sqrt(t));
                    trail.time = Mathf.Lerp(prev, 0f, t);
                    transform.parent.Rotate(slashDirection, swingBuf * Time.deltaTime);
                    transform.up = transform.position - transform.parent.position;
                    yield return null;
                }

                trail.emitting = false;
                trail.Clear();
                trail.time = prev;
                yield return null;
            }
        }
        
        // Cleanup
        GameObject p = transform.parent.gameObject;
        transform.parent = GS.FindParent(GS.Parent.misc);
        Destroy(p);
        yield break;
    }

    IEnumerator ExtendSlash(int n = 0)
    {
        float targetAmount = (n + 1f) / slashes;
        float startAmount = (float)n / slashes;
        for(float t = startAmount; t < targetAmount; t += Time.deltaTime / extendTimePerSlash)
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition.normalized * origMag, transform.localPosition.normalized * (origMag + attackRange), t);
            yield return null;
        }
    }
    
    IEnumerator SpinAttack(Quaternion goalAngle, Quaternion currentAngleQ)
    {
        float angleToRotate = Vector2.SignedAngle(GS.QTV(currentAngleQ), GS.QTV(goalAngle));
        Vector3 forwardsOrBackwards = angleToRotate < 0f ? -Vector3.forward : Vector3.forward;
        angleToRotate = Mathf.Abs(angleToRotate);
        angleToRotate -= swingAngle/2f;
        if (angleToRotate < 0f)
        {
            angleToRotate = 0f;
        }
        
        origMag = transform.position.normalized.magnitude;

        for (float rotated = 0f; rotated < angleToRotate; rotated += accelerateOmega * Time.deltaTime)
        {
            RotateStart();
            yield return null;
        }
        
        //3
        swingVFX.Play();
        col.enabled = true;
        trail.emitting = true;
        
        //4 - Do the actual swing through swingAngle degrees
        for (float swingRotation = 0f; swingRotation < swingAngle; swingRotation += Time.deltaTime * swingOmega)
        {
            transform.parent.Rotate(forwardsOrBackwards, swingOmega * Time.deltaTime);
            transform.up = transform.position - transform.parent.position;
            yield return null;
        }
        
        // Rest of the method remains the same
        //5
        swingVFX.Stop();
        col.enabled = false;
        //6
        float swingBuf;
        float prev = trail.time;
        for (float t = 0f; t < 1f; t += Time.deltaTime * 1f / extraWait)
        {
            swingBuf = 0.5f * Mathf.Lerp(swingOmega, 0f, Mathf.Sqrt(t));
            trail.time = Mathf.Lerp(prev, 0f, t);
            transform.parent.Rotate(forwardsOrBackwards, swingBuf * Time.deltaTime);
            transform.up = transform.position - transform.parent.position;
            yield return null;
        }
        trail.Clear();
        trail.emitting = false;
        trail.time = prev;
        
        GameObject p = transform.parent.gameObject;
        transform.parent = GS.FindParent(GS.Parent.misc);
        Destroy(p);
        yield break;
        
        void RotateStart()
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition.normalized, transform.localPosition.normalized * (origMag + attackRange), attackRange * Time.deltaTime * 2f);
            transform.parent.Rotate(forwardsOrBackwards, accelerateOmega * Time.deltaTime);
        }
    }

   private IEnumerator ThrustAttack()
{
    // Get direction toward mouse position
    Vector2 targetPosition = IM.i.MousePosition();
    Vector2 thrustDirection = ((Vector2)targetPosition - (Vector2)CharacterScript.CS.transform.position).normalized;
    Vector3 startRotation = transform.eulerAngles;
    
    // Calculate target rotation to face mouse position
    float targetRotation = Mathf.Atan2(thrustDirection.y, thrustDirection.x) * Mathf.Rad2Deg - 90f;
    
    // Store original position relative to character
    origMag = Vector2.Distance(transform.position, CharacterScript.CS.transform.position);
    Vector3 originalLocalPosition = transform.position - CharacterScript.CS.transform.position;
    
    // Rotate toward mouse position
    float currentTime = 0f;
    
    while (currentTime < extraWait)
    {
        float t = currentTime / extraWait;
        transform.rotation = Quaternion.Lerp(Quaternion.Euler(startRotation), Quaternion.Euler(0, 0, targetRotation), t);
        
        // Update position based on current character position
        transform.position = CharacterScript.CS.transform.position + originalLocalPosition.normalized * origMag;
        
        currentTime += Time.deltaTime;
        yield return null;
    }
    
    // Ensure final rotation is exact
    transform.rotation = Quaternion.Euler(0, 0, targetRotation);
    
    // Prepare for thrust
    swingVFX.Play();
    col.enabled = true;
    trail.emitting = true;
    
    // Thrust forward - now using the character's CURRENT position each frame
    float thrustDuration = 0.15f;
    currentTime = 0f;
    
    // Get starting position relative to character NOW
    Vector3 thrustStartLocalPos = transform.position - CharacterScript.CS.transform.position;
    Vector3 thrustEndLocalPos = thrustDirection * (origMag + attackRange);
    
    while (currentTime < thrustDuration)
    {
        float t = currentTime / thrustDuration;
        // Update position based on current character position
        transform.position = CharacterScript.CS.transform.position + Vector3.Lerp(thrustStartLocalPos, thrustEndLocalPos, t);
        currentTime += Time.deltaTime;
        yield return null;
    }
    
    // Short pause at extended position
    float pauseTime = 0f;
    while (pauseTime < 0.1f)
    {
        // Keep updating position based on character movement
        transform.position = CharacterScript.CS.transform.position + thrustEndLocalPos;
        pauseTime += Time.deltaTime;
        yield return null;
    }
    
    // Turn off collider after hit
    col.enabled = false;
    swingVFX.Stop();
    trail.emitting = false;
    
    GameObject p = transform.parent.gameObject;
    transform.parent = GS.FindParent(GS.Parent.misc);
    Destroy(p);
    yield break;
}

    public override bool CanAddThisPart()
    {
        return melees.Count < MechaSuit.level;
    }
    
 }