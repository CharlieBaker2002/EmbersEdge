using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class D3_0 : Room, IRoom
{
    public Animator anim;
    [SerializeField] private Sprite stopSprite;
    [SerializeField] private SpriteRenderer stopSr;
    public override void OnDefeat()
    {
        base.OnDefeat();
        anim.enabled = false;
    }

    public override void ResetRoom()
    {
        base.ResetRoom();
        anim.enabled = false;
        stopSr.sprite = stopSprite;
    }
}
