using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class ShootPotion : Boost
{
    public GameObject bullet;
    public int n;
    public float intermittent;
    [SerializeField]
    private bool local = false;

    public override void Consume(InputAction.CallbackContext c)
    {
        engagement = 1f;
        base.Consume(c);
        StartCoroutine(Shoot());
    }

    IEnumerator Shoot()
    {
        transform.parent = null;
        MechaSuit.m.followers.Remove(this);
        LeanTween.move(gameObject, CharacterScript.CS.transform.position, 0.25f).setEaseInOutCirc();
        
        Vector2 direction = IM.i.MousePosition(CharacterScript.CS.transform.position, true);
        yield return new WaitForSeconds(0.3f);
        Transform t = local ? transform: GS.FindParent(GS.Parent.allyprojectiles);
        for (int i = 0; i < n; i++)
        {
            if (Instantiate(bullet, transform.position, Quaternion.identity, t).TryGetComponent<ProjectileScript>(out var ps))
            {
                ps.SetValues(direction, tag);
            }
            yield return new WaitForSeconds(intermittent);
        }
        yield return new WaitForSeconds(0.5f);
        engagement = 0f;
        yield return new WaitForSeconds(0.5f);
        MechaSuit.UsedFollower(this);
    }
}
