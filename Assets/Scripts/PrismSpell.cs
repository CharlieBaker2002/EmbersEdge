using UnityEngine;
using UnityEngine.InputSystem;

// Prism: deploys a crystal at the mouse position that catches enemy projectiles,
// converts them to ally projectiles and refracts them at the nearest enemy.
// Level 3 splits each caught projectile in two. See PrismScript for the crystal.
public class PrismSpell : Spell
{
    public GameObject prismPrefab;
    [SerializeField] private float maxRange = 4f;
    private float atr = 0f;

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        Vector2 vect = IM.i.MousePosition(transform.position, true);
        if (vect.magnitude > maxRange)
        {
            vect = vect.normalized * maxRange;
        }
        var obj = Instantiate(prismPrefab, CharacterScript.CS.transform.position + (Vector3)vect, Quaternion.identity, GS.FindParent(GS.Parent.allies));
        obj.GetComponent<PrismScript>().Set(level, atr);
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(1 + level, 11 - 2 * level);
    }

    public override void LevelUp()
    {
        if (level < 3)
        {
            level++;
        }
    }

    public override void Intellect(float a)
    {
        atr = a;
    }
}
