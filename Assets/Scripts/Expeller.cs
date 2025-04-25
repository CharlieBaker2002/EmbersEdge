using UnityEngine;

public class Expeller : Part
{
    private float timer;
    private float refresh = 10f;
    private float reanimateTime = 2f; 
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private Sprite[] sprsreawaken;
    private bool reactivated = false;
    
    public override void StartPart(MechaSuit mecha)
    {
        CharacterScript.CS.ls.onDamageDelegate += Expel;
        base.StartPart(mecha);
    }
    
    public override void StopPart(MechaSuit m)
    {
        CharacterScript.CS.ls.onDamageDelegate -= Expel;
    }
    
    
    private void Expel(float x)
    {
        if (x > 0f) return;
        if(timer > 0f) return;
        timer = refresh;
        reactivated = false;
        StopCoroutine(nameof(GS.Animate));
        StartCoroutine(GS.Animate(sr, sprs, 0.4f, false)); 
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer >reanimateTime) return;
        if (reactivated) return;
        reactivated = true;
        engagement = 1f;
        StartCoroutine(GS.Animate(sr, sprs, reanimateTime, false));
    }
}
