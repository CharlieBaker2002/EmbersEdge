using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class ResourcePotion : Boost
{
    public float health;
    public float mana;
    public float flux;
    public float duration;
    bool started = false;
    public GameObject FX;

    void Start()
    {
        if(duration == 0f)
        { 
            duration = 0.01f;
        }
    }

   IEnumerator AddResource()
   {
        engagement = 1f;
        ResourceManager.instance.AddCores((int)flux);
        GS.Stat(CharacterScript.CS.GetComponent<Unit>(), "weak heal", duration, health);
        float initialMana = mana;
        float change;
        while(mana > 0f)
        {
            change = initialMana * Time.deltaTime / duration;
            if (mana - change > 0)
            {
                ResourceManager.instance.ChangeFuels(change);
                mana -= change;
            }
            else
            {
                ResourceManager.instance.ChangeFuels(mana);
                mana = 0;
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        engagement = 0f;
        yield return new WaitForSeconds(0.5f);
        MechaSuit.UsedFollower(this);
        
   }

    public override void Consume(InputAction.CallbackContext c)
    {
        base.Consume(c);
        if (!started)
        {
            started = true;
            StartCoroutine(AddResource());
            if (FX != null)
            {
                Instantiate(FX, transform.position, Quaternion.identity, transform);
            }
        }
    }
}
