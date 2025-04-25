using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class TimeSlashSpell : Spell
{
    bool perform = false;
    private float timer;
    public float castTime = 1.5f;
    [SerializeField] Animator anim;
    [SerializeField] private DamageBoundary damage;
    int ID = -1;
    float atr = 0f;
   public Transform trans;

    //load for 1 second, start buffer then if timer runs out or release, stop buffer, start attack.


    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        perform = true;
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
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        RefreshManager.i.QA(() => {engagement = 0f;
            ar.enabled = true;}, 2.5f);
        if (perform)
        {
            if (ctx.duration > 1f)
            {
                Attack();
            }
            if (perform && ctx.duration < 1f)
            {
                anim.SetBool("Reset", true);
            }
        }
        perform = false;
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
        SpawnManager.instance.CancelTS(ID);
        StopAllCoroutines();
        perform = false;
        anim.SetBool("Buffer", false);
        anim.SetBool("Reset", true);
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
