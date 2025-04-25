using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class KnifePotion : Boost
{
    public GameObject knife;
    public int n;

    public override void Consume(InputAction.CallbackContext c)
    {
        engagement = 1f;
        base.Consume(c);
        n = Mathf.RoundToInt(Random.Range(0.8f, 1.25f) * n);
        float dtheta = 180f / n;
        for (float theta = 0; theta < 360; theta += dtheta)
        {
            var ps = Instantiate(knife, transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles)).GetComponent<ProjectileScript>();
            ps.gameObject.SetActive(true);
            ps.SetValues(new Vector2(Mathf.Sin(Mathf.Deg2Rad * theta), Mathf.Cos(Mathf.Deg2Rad * theta)), tag, 5f);
        }
        StartCoroutine(ShootThangs());
    }

    IEnumerator ShootThangs()
    {
        float dtheta = 180f / n;
        for (float theta = 0; theta < 360; theta += dtheta)
        {
            var ps = Instantiate(knife, transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles)).GetComponent<ProjectileScript>();
            ps.SetValues(new Vector2(Mathf.Sin(Mathf.Deg2Rad * Random.Range(0, 360)), Mathf.Cos(Mathf.Deg2Rad * Random.Range(0, 360))), tag, Random.Range(0, 10));
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        engagement = 0f;
        yield return new WaitForSeconds(0.5f);
        MechaSuit.UsedFollower(this);
    }
}
