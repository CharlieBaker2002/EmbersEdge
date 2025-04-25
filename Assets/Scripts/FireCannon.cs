using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class FireCannon : Spell
{
    [SerializeField] int lvl = 1;
    [SerializeField] Fireball fireball;
    private Fireball fb;

    public override void LevelUp()
    {
        lvl++;
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        fb = Instantiate(fireball,transform.position,transform.rotation,transform);
        fb.level = lvl;
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        if (fb == null) return;
        fb.Release((float)ctx.duration, transform.up);
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(3f + level, 8f);
    }

    public override void StopPart(MechaSuit m)
    {
        base.StopPart(m);
        if (fb != null) Destroy(fb.gameObject);
    }

    public override void Intellect(float intellect)
    {
        throw new System.NotImplementedException();
    }
}
