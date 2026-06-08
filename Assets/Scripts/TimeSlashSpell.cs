using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class TimeSlashSpell : Spell
{
    bool perform = false;
    bool attacking = false;      // a swing is live: from Attack() until the damage collider switches off
    bool sawColliderOn = false;  // latch: the swing's collider window has been entered (so we can detect its end)
    private float timer;
    public float castTime = 1.5f;
    [SerializeField] Animator anim;
    [SerializeField] private DamageBoundary damage;
    private Collider2D damageCol; // the swing collider the attack animation enables/disables
    int ID = -1;
    float atr = 0f;
   public Transform trans;

    //load for 1 second, start buffer then if timer runs out or release, stop buffer, start attack.


    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        perform = true;
        attacking = false;
        sawColliderOn = false;
        if (damageCol == null && damage != null) damageCol = damage.GetComponent<Collider2D>();
        anim.SetBool("Reset", false);
        trans.localPosition = new Vector3(0, 0.5f, 0f);
        trans.localRotation = Quaternion.identity;
    }

    private void Update()
    {
        if (perform)
        {
            if(timer < 0f)
            {
                Attack();
            }
        }

        // Track the swing's collider window so we know when the attack is genuinely over: once the
        // collider has switched on and then off again, the swing is finished and no longer deployable.
        if (attacking && damageCol != null)
        {
            if (damageCol.enabled) sawColliderOn = true;
            else if (sawColliderOn) attacking = false;
        }
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        if (perform)
        {
            if (ctx.duration > 1f)
            {
                Attack();   // fires the swing (stays on the character)
            }
            else
            {
                anim.SetBool("Reset", true);   // released too early -> cancel, no attack
            }
        }
        perform = false;

        // Deploy is a release-only flourish: nudge the slash an inch along the current mouse aim. Only do
        // it while a swing is genuinely live (this release fired it, or an auto-fired one is still going);
        // never once the animation/collider is finished. attacking covers both cases.
        bool deployNow = attacking;

        base.Performed(ctx); // sets the cooldown (also flips engagement/ar)

        if (deployNow)
        {
            attacking = false;   // consumed by the deploy
            // Keep the slash locked to its aimed direction (Deploy nulls the parent so AntiRotate can't
            // snap it to identity); push it the extra inch, then restore ar/engagement when it returns.
            ar.enabled = false;
            engagement = 1f;
            Deploy(transform.position + transform.up * 0.5f, 0.15f, castTime, 0.5f, () =>
            {
                engagement = 0f;
                ar.enabled = true;
            });
        }
    }

    public void StartBuffer() //triggered in animation load
    {
        if (perform)
        {
            timer = level;
            anim.SetBool("Buffer", true);
            StartCoroutine(Buffer());
        }
    }

    private void Attack()
    {
        if (attacking) return;   // already swinging; don't restart (e.g. manual release after an auto-fire)
        SpawnManager.instance.CancelTS(ID);
        StopAllCoroutines();
        perform = false;
        anim.SetBool("Buffer", false);
        anim.SetBool("Reset", true);

        // The swing stays attached to the character (ar is already disabled from Started, so it tracks the
        // aim). It only detaches/nudges if the player releases mid-swing (see Performed). Mark it live;
        // Update clears this once the collider window ends.
        attacking = true;
        sawColliderOn = false;
    }

    IEnumerator Buffer()
    {
        ResourceManager.instance.AddCores(Random.Range(0, level + 1));
        ID = SpawnManager.instance.NewTS(0.475f - 0.075f * level, castTime);
        yield return new WaitForSecondsRealtime(castTime);
        Attack();
    }

    public override void LevelUp()
    {
        level++;
        castTime += 0.75f;
        damage.damage = 1.5f + 2.5f * Mathf.Pow(2, level - 1) * (1+0.1f*atr);
        trans.localScale *= 1.5f;
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2( level, 8);
    }
    

    public override void Intellect(float i)
    {
        damage.damage = 1.5f + 2.5f * Mathf.Pow(2, level - 1) * (1 + 0.1f * atr);
    }
}
