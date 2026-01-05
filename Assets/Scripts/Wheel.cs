using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class Wheel : Part
{
     Rigidbody2D rb;
    [SerializeField] Sprite[] sprites;
    [SerializeField] Sprite nomoveSprite;
    //[SerializeField] Material[] mats;
    float ang;
    public static List<Wheel> wheels;

    private void Start()
    {
        rb = CharacterScript.CS.AS.rb;
    }

    public override void StartPart(MechaSuit mecha)
    {
        //if (power == 0) { enabled = false; return; }
        //sr.material = mats[Mathf.Min(2,(int)power - 1)];

        if (!wheels.Contains(this)) wheels.Add(this);
        enabled = true;
    }

    public override void StopPart(MechaSuit m)
    {
        enabled = false;
        sr.sprite = nomoveSprite;
        if (wheels.Contains(this)) wheels.Remove(this);
    }

    private void Update()
    {
        if (CharacterScript.moving)
        {
            ang = GS.VTA(rb.linearVelocity.Rotated(-transform.rotation.eulerAngles.z));
            sr.sprite = GS.PercentParameter(sprites, ang / 360f);
            engagement = 1f;
        }
        else
        {
            engagement = 0f;
            sr.sprite = nomoveSprite;
        }
    }

}