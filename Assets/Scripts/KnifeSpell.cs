using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class KnifeSpell : Spell
{
    public GameObject knife;
    float castTime = -1f;
    int n = 0;
    int maxN = 7;
    public GameObject FX;
    float theta2 = 0f;
    float atr = 0f;

    Quaternion rot = Quaternion.identity;

    List<Transform> knives = new List<Transform>();
    bool perform = false;

    private void Update()
    {
        castTime += Time.deltaTime;
        if (perform)
        {
            if (castTime > 0.4f*Mathf.Sqrt(n)/(level*2-1) && n < maxN)
            {
                theta2 = n *  0.5f * Mathf.PI / (maxN-1);
                Vector3 vect = new Vector3(0.35f * Mathf.Sin(theta2 - Mathf.PI/2), 0.7f * Mathf.Cos(theta2 - Mathf.PI/2), 0f);
                castTime = 0f;
                var k = Instantiate(knife, transform.position, Quaternion.identity, transform).transform;
                k.GetComponent<ProjectileScript>().father = CharacterScript.CS.transform;
                k.gameObject.SetActive(true);
                k.localPosition += vect;
                k.GetComponent<ProjectileScript>().timer = 10f;
                knives.Add(k);
                n++;
                if(n < maxN)
                {
                    float opTheta = Mathf.PI - theta2;
                    vect = new Vector3(0.35f * Mathf.Sin(opTheta - Mathf.PI / 2), 0.7f * Mathf.Cos(opTheta - Mathf.PI / 2), 0f);
                    castTime = 0f;
                    var j = Instantiate(knife, transform.position, Quaternion.identity, transform).transform;
                    j.GetComponent<ProjectileScript>().father = CharacterScript.CS.transform;
                    j.gameObject.SetActive(true);
                    j.localPosition += vect;
                    j.GetComponent<ProjectileScript>().timer = 10f;
                    knives.Add(j);
                    n++;
                }
            }
        }
        else
        {
            transform.rotation = rot;
        }
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        rot = transform.rotation;
        FX.SetActive(true);
        castTime = 0f;
        perform = true;
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        engagement = 0f;
        rot = transform.rotation;
        perform = false;
        castTime = 0f;
        FX.SetActive(false);;
        n = 0;
        StartCoroutine(ShootKnives());
    }

    IEnumerator ShootKnives()
    {
        for(int i = 0; i < knives.Count; i ++)
        {
            if(i%2 == 0)
            {
                yield return new WaitForSeconds(0.4f - level * 0.1f);
            }
            if (knives[i] == null)
            {
                continue;
            }
            var t = knives[i];
            t.parent = GS.FindParent(GS.Parent.allyprojectiles);
            var ps = t.GetComponent<ProjectileScript>();
            ps.SetValues(((Vector3)IM.i.MousePosition(t.transform.position,true)), tag,atr);
            ps.timer = 2f;
        }
        knives.Clear();
    }

    public override void LevelUp()
    {
        if (level < 3)
        {
            level += 1;
            maxN += (level + 1) * level;
        }
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(1 + level, 8);
    }
    public override void Intellect(float i)
    {
        atr = i;
    }
}
