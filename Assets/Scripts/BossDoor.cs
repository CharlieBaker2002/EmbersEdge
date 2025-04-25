using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BossDoor : Door
{
    readonly int[] req = new int[] { 3, 5, 7 };
    [SerializeField] RawImage[] raws;
    [SerializeField] Animator anim;

    public static BossDoor i;


    public override void Awake()
    {
        base.Awake();
        i = this;
    }

    public override void OpenDoor(List<string> keys)
    {
        if (sr.isVisible)
        {
            OnBecameVisible();
        }
    }

    public void OnBecameVisible()
    {
        if (EmbersEdge.currentCores >= req[GS.era] && DM.i.activeRoom != room2)
        {
            col.isTrigger = true;
            anim.SetBool("Open", true);
        }
        else
        {
            col.isTrigger = false;
        }
    }

    private void OnBecameInvisible()
    {
        anim.SetBool("Open", false);
    }

    public override void CloseDoor()
    {
        anim.SetBool("Open", false);
        col.isTrigger = false;
    }

    public static void AddRaw(RenderTexture rt)
    {
        foreach(RawImage r in i.raws)
        {
            if(r.texture == null)
            {
                r.texture = rt;
                r.color = Color.white;
                return;
            }
        }
    }
}
