using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class PushSpell :Spell
{
    public GameObject prefab;
    private GameObject obj;
    public GameObject FX;
    private ProjectileScript ps;
    bool perform = false;
    float atr = 0f;

    [SerializeField] SpriteRenderer indicator;
    [SerializeField] Sprite[] indicatorSprites;

    private void Awake()
    {
        obj = Instantiate(prefab,transform.position,transform.rotation,transform);
        obj.tag = tag;
        ps = obj.GetComponent<ProjectileScript>();
        obj.SetActive(false);
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        perform = true;
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        if (perform)
        {
            Vector2 vect = IM.i.MousePosition(transform.position,true);
            float angle = Vector2.SignedAngle(Vector2.up, vect);
            FX.SetActive(false);
            perform = false;
            var obj1 = Instantiate(obj,transform.position,Quaternion.Euler(0,0,angle),GS.FindParent(GS.Parent.allyprojectiles));
            obj1.SetActive(true);
            obj1.GetComponent<ProjectileScript>().SetValues(vect, tag, atr);
            if(level > 1)
            {
                var obj2 = Instantiate(obj, transform.position, Quaternion.Euler(0, 0, angle + 180), GS.FindParent(GS.Parent.allyprojectiles));
                obj2.SetActive(true);
                obj2.GetComponent<ProjectileScript>().SetValues(-vect, tag, atr);
            }
            if (level == 3)
            {
                var obj3 = Instantiate(obj, transform.position, Quaternion.Euler(0, 0, angle + 90), GS.FindParent(GS.Parent.allyprojectiles));
                obj3.SetActive(true);
                var vector = Quaternion.Euler(0, 0, 90) * vect;
                obj3.GetComponent<ProjectileScript>().SetValues(vector, tag, atr);
                var obj4 = Instantiate(obj, transform.position, Quaternion.Euler(0, 0,angle -90), GS.FindParent(GS.Parent.allyprojectiles));
                obj4.SetActive(true);
                obj4.GetComponent<ProjectileScript>().SetValues(-vector, tag,atr);
            }
        }
    }

    public override void LevelUp()
    {
        if (level < 3)
        {
            level++;
            ps.timer += 0.5f;
            ps.speed += 0.25f;
            ps.push = 8 * (1 + 0.1f * atr) * (1+(level-1)*0.25f);
            obj.transform.localScale += new Vector3(0.25f, 0.25f, 0);
            obj.SetActive(false);
            indicator.sprite = indicatorSprites[level-2];
            indicator.transform.localPosition = new Vector3();
        }
        
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(2 + level, 6 - level);
    }
    public override void Intellect(float a)
    {
        atr = a;
        ps.push = 8 * (1 + 0.1f * atr) * (1 + (level - 1) * 0.25f);
    }
}
