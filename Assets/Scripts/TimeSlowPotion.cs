using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class TimeSlowPotion : Boost
{
    public override void Consume(InputAction.CallbackContext c)
    {
        base.Consume(c);
        engagement = 2f;
        StartCoroutine(SuperHot());
    }

    IEnumerator SuperHot()
    {
        CharacterScript.CS.AS.Stop();
        IM.i.pi.Player.Movement.Disable();
        yield return new WaitForSeconds(0.01f);
        IM.i.pi.Player.Movement.Enable();
        float a = Time.fixedUnscaledTime;
        while (a + 7.5f > Time.fixedUnscaledTime)
        {
            engagement = (a + 7.5f - Time.fixedUnscaledTime)/3.75f;
            int ID = SpawnManager.instance.NewTS(0.25f, 7.5f);
            while (IM.i.pi.Player.Movement.ReadValue<Vector2>().magnitude == 0)
            {
                yield return null;
                if(a + 7.5f < Time.fixedUnscaledTime)
                {
                    break;
                }
            }
            SpawnManager.instance.CancelTS(ID);
            yield return null;
        }
        MechaSuit.UsedFollower(this);
    }
}
