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
    [SerializeField] protected Collider2D col;
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

    [Header("Durability / Drill")]
    [SerializeField] private float maxDurability = 100f;
    [SerializeField] private float durabilityPerEnemyHit = 4f;
    [SerializeField] private float durabilityPerChip = 1f;
    [Tooltip("Half-angle of the drill cone around the aim direction.")]
    [SerializeField] private float drillHalfAngle = 32f;
    [Tooltip("Resting distance the drill hovers in front of the player, toward the mouse.")]
    [SerializeField] private float idleDistance = 0.55f;
    [Tooltip("Fan spread between multiple drills when several are equipped.")]
    [SerializeField] private float idleFanDegrees = 24f;
    [Tooltip("Reach of passive (no-button) contact mining from the drill tip.")]
    [SerializeField] private float passiveMineRange = 0.4f;
    [Tooltip("Seconds between passive contact-mining ticks while grinding ore.")]
    [SerializeField] private float passiveMineInterval = 0.1f;
    [Tooltip("How far the drill bobs backwards-and-forwards on each collision (a chip or an enemy hit).")]
    [SerializeField] private float mineBobAmp = 0.15f;
    private float passiveMineTimer;
    private float bobT = 1f;   // 0..1 = one bob cycle in flight; >=1 = at rest

    [Header("Passive contact (enemies)")]
    [Tooltip("If true, the melee is physical — it rams enemies with body-collision knockback (and the " +
             "wielder recoils). If false, it's just a trigger that deals damage with no knockback.")]
    [SerializeField] private bool physical = true;
    public bool Physical => physical;
    [Tooltip("Damage dealt to an enemy each time the drill passively collides with it.")]
    [SerializeField] private float passiveHitDamage = 1f;
    [Tooltip("Minimum time before the same enemy can be passively hit again (seconds).")]
    [SerializeField] private float minRecollideTime = 0.5f;

    [Header("Drill follow (mouse)")]
    [Tooltip("Max angular speed the drill swings toward the cursor (deg/s).")]
    [SerializeField] private float followAngularMaxVel = 720f;
    [Tooltip("Angular acceleration toward the cursor (deg/s²).")]
    [SerializeField] private float followAngularAccel = 4800f;
    [Tooltip("Angular deceleration as the drill settles on the cursor (deg/s²).")]
    [SerializeField] private float followAngularDecel = 4800f;
    private float followAngle;
    private float followAngVel;
    private bool followInit;

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [Tooltip("Stamina regenerated per second (recharges even while on cooldown).")]
    [SerializeField] private float staminaRegen = 25f;
    [Tooltip("Stamina spent each time the drill hits an enemy.")]
    [SerializeField] private float staminaPerHit = 50f;
    private float stamina;
    private bool staminaCooldown;

    private float durability;
    private bool broken;
    private bool attacking;
    private bool ghosted;   // sprite faded while the wielder (and so the carried drill) is immaterial

    // "Ready" governs COMBAT — passive enemy damage, mouse-tracking, and activation: blocked mid-attack,
    // while cooling down, broken, or stamina-drained. Mining/drilling is governed by HEALTH only (see
    // FixedUpdate): durability, never stamina or the activation cooldown.
    private bool Ready => canUse && !broken && !staminaCooldown;
    public float MaxDurability => maxDurability;
    public float Durability => durability;
    public bool Broken => broken;
    public float MaxStamina => maxStamina;
    public float Stamina => stamina;
    public bool OnStaminaCooldown => staminaCooldown;
 

    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        extendTimePerSlash = swingAngle / swingOmega;
        trail.emitting = false;
        melees.Add(this);
        // The drill damages enemies on contact even without an attack. The collider is live whenever
        // the drill is READY (not mid-attack, not cooling down, not broken) — see LateUpdate; an
        // active thrust manages the collider itself. DamageBoundary dedups each target per its refresh
        // window, i.e. once per slash. Passive contact never starts a cooldown; only activating does,
        // and both the activation and its cooldown suspend the passive effect.
        col.enabled = true;
        engagement = 1f;
        durability = maxDurability;
        broken = false;
        stamina = maxStamina;
        staminaCooldown = false;
        // Every melee is a drill: thrust straight at the cursor rather than spinning around the player.
        attackType = AttackType.Thrust;
        if (col != null)
        {
            DamageBoundary db = col.GetComponent<DamageBoundary>();
            if (db != null)
            {
                db.meleeOwner = this;
                db.damage = passiveHitDamage;            // damage per passive collision with an enemy
                db.refreshCollideTimer = minRecollideTime; // min time before re-hitting the same enemy
            }
        }
        if (DurabilitySlider.i != null) DurabilitySlider.i.InitMelee(this);
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
            if (weapon.Ready)
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
            weapon.durability = weapon.maxDurability;
            weapon.broken = false;
            weapon.stamina = weapon.maxStamina;
            weapon.staminaCooldown = false;
        }
        if (DurabilitySlider.i != null) DurabilitySlider.i.Refresh();
    }

    /// <summary>Refill drill durability — called on each dive to the dungeon (PortalScript).</summary>
    public static void RefreshDurability()
    {
        ResetWeapons();
    }

    // At rest the drill hovers in front of the player, pointing at the mouse (no orbiting). Several
    // drills fan out around the aim. While attacking, the AttackSequence owns the transform instead.
    private void LateUpdate()
    {
        // The carried drill is immaterial while its wielder is (its hits become ghost hits — see
        // DamageBoundary/NotifyEnemyHit): fade the sprite while phased, restore on return.
        bool ghost = GS.AS != null && GS.AS.immaterial;
        if (ghost != ghosted && sr != null)
        {
            ghosted = ghost;
            Color c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, ghost ? 0.45f : 1f);
        }

        // Passive enemy damage: the collider is live only while the drill is ready. During an active
        // thrust the AttackSequence owns the collider, so don't touch it here (and re-sync the follow
        // angle on resume so the drill swings smoothly from wherever the thrust left it).
        if (attacking) { followInit = false; return; }
        bool live = Ready;
        if (col != null && col.enabled != live) col.enabled = live;

        if (CharacterScript.CS == null) return;

        if (!followInit)
        {
            followAngle = Mathf.Atan2(transform.up.y, transform.up.x) * Mathf.Rad2Deg;
            followAngVel = 0f;
            followInit = true;
        }

        // Track the cursor only while ready. On cooldown the drill holds its angle (stays in place),
        // still riding in front of the player but no longer chasing the mouse.
        if (live)
        {
            Vector2 aim = CharacterScript.aim;
            if (aim.sqrMagnitude > 0.0001f)
            {
                int idx = melees.IndexOf(this);
                int n = melees.Count;
                float spread = n > 1 ? (idx - (n - 1) * 0.5f) * idleFanDegrees : 0f;
                Vector2 dir = GS.QTV(Quaternion.Euler(0f, 0f, spread) * CharacterScript.aimQ);
                float target = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                StepFollow(target, Time.deltaTime);
            }
        }
        else
        {
            followAngVel = 0f; // freeze angular motion during cooldown
        }

        float rad = followAngle * Mathf.Deg2Rad;
        Vector2 cur = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector3 player = CharacterScript.CS.transform.position;
        // Bob on collision: each chip / enemy hit kicks one back-and-forward cycle along the aim.
        float bob = 0f;
        if (bobT < 1f)
        {
            bobT = Mathf.Min(1f, bobT + Time.deltaTime / Mathf.Max(0.05f, passiveMineInterval * 0.9f));
            bob = -Mathf.Sin(bobT * Mathf.PI) * mineBobAmp;
        }
        transform.position = player + (Vector3)(cur * (idleDistance + bob));
        transform.up = cur;
    }

    // Trapezoidal angular motion toward the cursor: accelerate up to a max speed, then brake within
    // stopping distance so the drill eases onto the aim without snapping or overshooting.
    private void StepFollow(float target, float dt)
    {
        float error = Mathf.DeltaAngle(followAngle, target);
        float decel = Mathf.Max(0.0001f, followAngularDecel);
        float stopDist = followAngVel * followAngVel / (2f * decel);
        bool towards = followAngVel * error > 0f;

        if (towards && Mathf.Abs(error) <= stopDist)
            followAngVel = Mathf.MoveTowards(followAngVel, 0f, decel * dt);
        else
            followAngVel = Mathf.MoveTowards(followAngVel, Mathf.Sign(error) * followAngularMaxVel, followAngularAccel * dt);

        // Anti-oscillation damping: never cross the target. If this step would reach or pass it, settle
        // exactly on it and stop — so the drill eases in and holds instead of ringing back and forth.
        float next = followAngle + followAngVel * dt;
        if (Mathf.DeltaAngle(next, target) * error <= 0f)
        {
            followAngle = target;
            followAngVel = 0f;
        }
        else
        {
            followAngle = next;
        }
    }

    // Passive tile damage: while the drill is ready, continuously chip exposed ore it's touching,
    // draining durability. Mining/contact never sets a cooldown; only activating (Attack) does.
    private void FixedUpdate()
    {
        // Stamina always recharges — including while on cooldown, which is how the drill recovers and
        // clears the cooldown once it's back to full.
        if (stamina < maxStamina)
        {
            stamina = Mathf.Min(maxStamina, stamina + staminaRegen * Time.fixedDeltaTime);
            if (staminaCooldown && stamina >= maxStamina)
            {
                staminaCooldown = false;
                engagement = 0.5f;   // grow the drill back to its idle size now that it's ready
            }
        }

        // Drilling is governed by HEALTH (durability) only — stamina and the activation cooldown never
        // stop you mining. Only being broken or mid-attack suspends it.
        if (broken || attacking || MineField.i == null || CharacterScript.CS == null) return;

        // The weapon has NO wall collision of its own (that was buggy) — only the BODY's circle collider
        // collides with the ore (MineField.Depenetrate), and mining triggers off that contact: press
        // your body against the rock while aiming at it and the drill chews.
        if (!MineField.i.PlayerTouchingOre) return;

        passiveMineTimer -= Time.fixedDeltaTime;
        if (passiveMineTimer > 0f) return;
        passiveMineTimer = passiveMineInterval;
        // Sample the drill cone from the PLAYER out past the drill tip — the drill's collider spans its
        // whole length (so enemies are hit), but the pivot at idleDistance sits in open tunnel, so a
        // tip-local range never reaches the wall. Reach = idleDistance + passiveMineRange.
        Vector2 origin = CharacterScript.CS.transform.position;
        float range = idleDistance + passiveMineRange;
        if (MineField.i.TryMineArc(origin, transform.rotation, range, drillHalfAngle,
                passiveMineInterval, out int chipped, out _) && chipped > 0)
        {
            DrainDurability(durabilityPerChip * chipped);
            bobT = 0f;   // visual: the drill bobs back-and-forward on each bite
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
        attacking = true;
        engagement = 1f;
        // Activation does NOT mine — tiles are only damaged by passive contact (FixedUpdate). The
        // thrust is purely a combat strike.
        StartCoroutine(AttackSequence());
    }

    /// <summary>An enemy hit registered by the drill's DamageBoundary. `ghostHit` marks an IMMATERIAL
    /// drill (wielder phased) striking a MATERIAL enemy: it still wears the drill (durability) but a
    /// hit that phases through doesn't tire it (no stamina drain).</summary>
    public void NotifyEnemyHit(bool ghostHit = false)
    {
        DrainDurability(durabilityPerEnemyHit);
        if (!ghostHit) DrainStamina(staminaPerHit);
        bobT = 0f;   // same collision bob as a mining bite
    }

    // Each enemy hit spends stamina; draining it to empty latches a cooldown (drill unusable + passive
    // off) until stamina recharges to full. The per-melee cd ring shows the recharge sweep.
    private void DrainStamina(float amount)
    {
        if (staminaCooldown) return;
        stamina -= amount;
        if (stamina <= 0f)
        {
            stamina = 0f;
            staminaCooldown = true;
            engagement = 0f;   // drop activation to 0 while drained
            if (cd != null) cd.SetValue(maxStamina / Mathf.Max(0.0001f, staminaRegen));
        }
    }

    public void DrainDurability(float amount)
    {
        if (broken) return;
        durability -= amount;
        if (durability <= 0f)
        {
            durability = 0f;
            broken = true;
            canUse = false;
            LoseSelf();   // the drill is spent — lose the part entirely (byebye dissolve)
        }
        if (DurabilitySlider.i != null) DurabilitySlider.i.Refresh();
    }

    // Running durability to zero destroys the drill: pull it from the mecha with the byebye dissolve
    // and re-layout the remaining parts. (StopPart — called by MurkPart — removes it from `melees`.)
    private void LoseSelf()
    {
        if (MechaSuit.m != null)
        {
            int idx = MechaSuit.m.parts.IndexOf(this);
            if (idx >= 0)
            {
                MechaSuit.m.MurkPart(this, idx, true);
                MechaSuit.m.ArrangePartsInRings();
                return;
            }
        }
        StopPart(MechaSuit.m);
        Destroy(gameObject);
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
        if (animateSprites != null && animateSprites.Length > 0) sr.LeanAnimate(animateSprites, 0.25f);
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
        if (animateSprites != null && animateSprites.Length > 0) sr.LeanAnimate(animateSprites, 0.5f, false, true);
        yield return StartCoroutine(Return(returnTime));
        attacking = false;   // hand the transform back to the idle mouse-facing behaviour
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
            if (swingVFX != null) swingVFX.Play();
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
            if (swingVFX != null) swingVFX.Stop();
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
        if (swingVFX != null) swingVFX.Play();
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
        if (swingVFX != null) swingVFX.Stop();
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
    if (swingVFX != null) swingVFX.Play();
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
    if (swingVFX != null) swingVFX.Stop();
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