using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class BlipSpell : Spell
{
    bool perform = false;
    private float timer;
    [SerializeField] Animator anim;
    private float damage = 2f;
    private List<int> cols = new List<int>();
    private Vector3 init;
    private Transform cs;
    private float maxTime = 2;
    public GameObject FX;
    private float distance = 2f;
    private GameObject fxbuffer;
    public GameObject shield;
    float dmgcoef = 1f;

    [SerializeField] Transform trans;

    //load for 1 second, start buffer then if timer runs out or release, stop buffer, start attack.
    
    private void Start()
    {

        cs = GS.CS();
        PortalScript.i.onTeleport += TeleportFix;
    }

    private void TeleportFix(bool toDungeon)
    {
        if (toDungeon)
        {
            init = DM.i.activeRoom.safeSpawn.position;
        }
        else
        {
            init = new Vector3(0, 0, init.z);
        }
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        if (level > 1)
        {
            var a = Instantiate(shield, transform.position, Quaternion.identity, transform);
            a.GetComponent<SpriteRenderer>().enabled = false;
        }
        engagement = 1f;
        Attack();
        perform = true;
        timer = maxTime;
        trans.localPosition = new Vector3(0, 0f, 0f);
        trans.localRotation = Quaternion.identity;
        init = cs.position;
        fxbuffer = Instantiate(FX, init, Quaternion.identity,null);
        Vector3 p;
        if((IM.i.MousePosition(Vector2.zero,true) - (Vector2)GS.CS().position).sqrMagnitude > distance * distance)
        {
            p = (IM.i.MousePosition(Vector2.zero, true) - (Vector2)GS.CS().position).normalized * distance;
        }
        else
        {
            p = (IM.i.MousePosition(Vector2.zero, true) - (Vector2)GS.CS().position);
        }
        cs.position += p;
    }

    private void Update()
    {
        if (perform)
        {
            timer -= Time.deltaTime;
            if (timer < 0f)
            {
                perform = false;
                Return();
            }
        }
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        if (perform)
        {
            timer = Mathf.Min(timer,1f);
        }
    }

    private void Return()
    {
        if (level == 3)
        {
            var a = Instantiate(shield, transform.position, Quaternion.identity, transform);
            a.GetComponent<SpriteRenderer>().enabled = false;
        }
        Attack();
        CharacterScript.CS.transform.position = init;
        Destroy(fxbuffer);
        
    }

    public override void LevelUp()
    {
        level++;
        maxTime += 1.5f;
        damage *= 2f;
        trans.localScale *= 1.5f;
        distance += 2f;
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(2 + level, 8);
    }

    private void OnTriggerEnter2D(Collider2D c)
    {
        if (cols.Contains(c.GetInstanceID()))
        {
            return;
        }
        cols.Add(c.GetInstanceID());
        if (c.CompareTag("Enemies"))
        {
            if (c.GetComponentInParent<LifeScript>() != null)
            {
                c.GetComponentInParent<LifeScript>().Change(-damage * dmgcoef, 1);
            }
        }
    }

    private void Attack()
    {
        if (anim.GetBool("Attack")== false)
        {
            anim.SetBool("Attack", true);
        }
        else
        {
            StartCoroutine(WaitForFalse());
        }
    }

    IEnumerator WaitForFalse()
    {
        while (anim.GetCurrentAnimatorStateInfo(0).IsName("Grow"))
        {
            yield return null;
        }
        anim.SetBool("Attack", true);
    }


    public void SetFalseEvent()
    {
        anim.SetBool("Attack", false);
    }

    public override void Intellect(float i)
    {
        dmgcoef = 1 + 0.1f * i;
    }
}
