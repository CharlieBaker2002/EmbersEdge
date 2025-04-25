using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AllyAI : Unit, IOnDeath, IClickable
{
    // modal movement
    [HideInInspector]
    public UnitBuilding home;
    public enum Mode { rally, follow};
    [HideInInspector]
    public Mode mode = Mode.rally;

    public Vector2 targetPoint = new Vector2();
    private float timer = 0f;
    public float resetTimer = 1.5f;
    public float exploreRadius = 2f;
    [Tooltip("ydelta is subtracted from pushPoint")]
    public float ydelta = 0f;

    public static List<AllyAI> allies = new List<AllyAI>();
    [HideInInspector]
    public float pushPoint = 5f;
    public static bool rallyMode = true;
    [HideInInspector]
    public bool skrskr = false;
    [HideInInspector]
    public bool stopSkr = false;
    public Sprite spr;
    public int groupCost = 1;

    protected override void Start()
    {
        base.Start();
        allies.Add(this);
        if (rallyMode)
        {
            mode = Mode.rally;
        }
    }

    protected override void Update()
    {
        if(timer > 0f)
        {
            timer -= Time.deltaTime;
        }
        else
        {
            timer = resetTimer;
            NewTarget();
        }
    }

    public void NewTarget()
    {
        if(mode == Mode.follow)
        {
            targetPoint = (Vector2) GS.CS().position + GS.RandCircleV2(0, exploreRadius);
            if (!skrskr && !stopSkr)
            {
                if (Vector2.Distance(GS.CS().position, transform.position) > exploreRadius * 4f)
                {
                    skrskr = true;
                    StartCoroutine(SkrSkr());
                }
            }
        }
        else if(mode == Mode.rally)
        {
            targetPoint = home.rallyPoint + GS.RandCircleV2(0, exploreRadius);
        }
    }
    
    private IEnumerator SkrSkr()
    {
        Vector2 targ = targetPoint;
        Vector3 pos = transform.position;
        while(Vector2.Distance(transform.position, targ) > 0.1f)
        {
            if(skrskr == false)
            {
                yield break;
            }
            transform.position = pos + 5f * Time.deltaTime * ((Vector3)targ - pos).normalized;
            pos = transform.position;
            yield return null;
        }
        skrskr = false;
        NewTarget();
    }

    public void MoveToRoom(Vector2 safeSpot)
    {
        if (!stopSkr)
        {
            safeSpot += (Vector2)GS.RandCircle(0.15f, 0.35f);
            skrskr = false;
            stopSkr = true;
            StartCoroutine(SkrToRoom(safeSpot));
        }
    }

    private IEnumerator SkrToRoom(Vector2 safeSpot)
    {
        yield return null;
        yield return null;
        AS.interactive = false;
        Vector3 pos = transform.position;
        while (Vector2.Distance(transform.position, safeSpot) > 0.1f)
        {
            transform.position = pos + 5f * Time.deltaTime * ((Vector3)safeSpot - pos).normalized;
            pos = transform.position;
            yield return null;
        }
        NewTarget();
        yield return new WaitForSeconds(0.2f);
        skrskr = false;
        stopSkr = false;
        AS.interactive = true;
    }

    public void OnDeath()
    {
        home.ReduceLive();
        allies.Remove(this);
        if (CharacterScript.CS.group.Contains(this))
        {
            CharacterScript.CS.groupCurrent -= groupCost;
            CharacterScript.CS.group.Remove(this);
            CharacterScript.CS.DeleteTile(ls);
        }
    }
    

    public static void SkrDaHomies(Vector2 safeP)
    {
        foreach (AllyAI ai in CharacterScript.CS.group)
        {
            ai.MoveToRoom(safeP);
        }
    }

    public override void OnClick()
    {
        base.OnClick();
        if (home.allowGroup)
        {
            if (!CharacterScript.CS.group.Contains(this))
            {
                if (CharacterScript.CS.groupCurrent + groupCost <= CharacterScript.CS.groupMax)
                {
                    exploreRadius /= 2;
                    CharacterScript.CS.groupCurrent += groupCost;
                    CharacterScript.CS.group.Add(this);
                    CharacterScript.CS.NewTile(this);
                    mode = Mode.follow;
                }
            }
            else
            {
                CharacterScript.CS.RefreshGroupUI();
            }
        }
    }
}