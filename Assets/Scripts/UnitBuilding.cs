using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class UnitBuilding : Building
{
    public GameObject unit;
    public int[] cost;
    [HideInInspector]
    public Vector2 rallyPoint;
    private Action<InputAction.CallbackContext> clickAction;
    private Action<InputAction.CallbackContext> esc;
    private bool stopBool = false;
    public int live = 0;
    public int maxLive = 5;
    public bool allowGroup = false;
    public Sprite unitSprite;
    public TextMeshPro txt;
    public float rallyDistance = 7f;
    private GameObject rallyBounds;
    public Sprite[] automateSprites;
    private bool automate = false;
    public string unitName;

    public override void Start()
    {
        base.Start();
        rallyBounds = (GameObject) Resources.Load("RallyPoint",typeof(GameObject));
        rallyPoint = transform.position;
        rallyBounds = Instantiate(rallyBounds, transform);
        rallyBounds.SetActive(false);
        esc = delegate { stopBool = true; IM.i.pi.Player.Interact.started -= clickAction; IM.i.pi.Player.Escape.performed -= esc; };
        clickAction = delegate { stopBool = true; rallyPoint = rallyBounds.transform.position; IM.i.pi.Player.Interact.started -= clickAction; IM.i.pi.Player.Escape.performed -= esc; };
        AddSlot(new int[] { 0, 0, 0, 0 }, "Rally", Resources.Load<Sprite>("Sprites/RallyIcon"), false, SetRally);

        AddSlot(cost, unitName, unitSprite, false, SpawnNewUnit, false,delegate { live++; UpdateText(); }, SpaceForMore, IsNotAutomated);
        unit = Instantiate(unit, transform);
        unit.SetActive(false);
        OnOpen += delegate { txt.gameObject.SetActive(true); UpdateText(); };
        OnClose += delegate { txt.gameObject.SetActive(false); };

        AddSlot(new int[] { 0, 0, 0, 0 }, "Automate", automateSprites[0], false, SwitchAutomate, false,null, null, IsNotAutomated);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Don't Automate", automateSprites[1], false, SwitchAutomate, false,null, null, IsAutomated);
    }

    private bool IsNotAutomated()
    {
        return !automate;
    }

    private bool IsAutomated()
    {
        return automate;
    }

    private void SwitchAutomate()
    {
        automate = !automate;
        UpdateUI();
    }

    private void Update()
    {
        if (automate)
        {
            if(live < maxLive)
            {
                if (ResourceManager.instance.CanAfford(cost, false, false))
                {
                    ResourceManager.instance.NewTask(gameObject, cost, SpawnNewUnit);
                    live++;
                    UpdateText();
                }
            }
        }
    }

    private void UpdateText()
    {
        txt.text = live.ToString() + " / " + maxLive.ToString();
    }

    public void ReduceLive()
    {
        live--;
        UpdateText();
    }

    private bool SpaceForMore()
    {
        return live < maxLive;
    }

    public void SetRally()
    {
        rallyBounds.SetActive(true);
        StartCoroutine(RallyFollowMouse());
        IM.i.pi.Player.Interact.started += clickAction;
        IM.i.pi.Player.Escape.performed += esc;
    }

    IEnumerator RallyFollowMouse()
    {
        stopBool = false;
        if (IM.controller)
        {
            IM.i.OpenCursor();
        }
        while(stopBool == false)
        {
            Vector2 pos = IM.i.MousePosition();
            Vector2 dir = pos - (Vector2)transform.position;
            if(dir.sqrMagnitude > rallyDistance * rallyDistance)
            {
                rallyBounds.transform.position = (Vector2)transform.position + dir.normalized * rallyDistance;
            }
            else
            {
                rallyBounds.transform.position = pos;
            }
            yield return null;
        }
        StartCoroutine(FadeOut(rallyBounds.GetComponent<SpriteRenderer>())); //fade, clone & hide.
        rallyBounds = Instantiate(rallyBounds,transform);
        rallyBounds.SetActive(false);
    }

    IEnumerator FadeOut(SpriteRenderer sr)
    {
        yield return null;
        yield return null;
        while(sr.color.a > 0.1f)
        {
            sr.color = new Color(sr.color.r,sr.color.g,sr.color.b, Mathf.Lerp(sr.color.a, 0f, 5f * Time.deltaTime));
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    private void SpawnNewUnit()
    {
        var a = Instantiate(unit, transform.position + GS.RandCircle(0.6f, 1.1f), Quaternion.identity, GS.FindParent(GS.Parent.allies));
        a.GetComponent<AllyAI>().home = this;
        a.SetActive(true);
    }

    

}
