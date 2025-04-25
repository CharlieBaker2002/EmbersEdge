using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

public class PortalScript : MonoBehaviour
{
    public static PortalScript i;
    public bool canPortal = true;
    public bool inDungeon = false;
    public Transform dungeonCamera;
    public GameObject WinUI;
    public GameObject LoseUI;   
    [HideInInspector]
    public int tsID = 0;

    CharacterScript CS;
    [SerializeField] ParticleSystem FX;

    [SerializeField] float televal;
    [SerializeField] float televalMax;
    [SerializeField] RectTransform slrt;
    [SerializeField] Camera slCam;
    [SerializeField] RawImage ri;
    [SerializeField] Color[] colGrad;
    [SerializeField] Transform[] slTs;
    bool cd = true;
    float timer = -10f;

    [SerializeField] private Color offTPColour;
    InputAction recall;

    private System.Action<float> OnDamageCancel;
    public System.Action<bool> onTeleport;

    public Sprite[] ankorSprites;
    public SpriteRenderer ankorSR;
    public Animator anim;
    public ParticleSystem PS;
    public ParticleSystem[] PSes;

    public Sprite[] quarterSprites;
    public SpriteRenderer[] quarters;
    private int quarterInt = 0;
    public List<Light2D> lights;

    private bool stopBool = false;
    public bool swapTeleIcon = false;
    bool waitMaxSlide = false;
    public bool clickSkip = false; //Sets to teleport you automatically

    public bool goingHomeNow = false; //used in DistortLens (cameraScript) to assess whether to set next day.
    public SpriteRenderer outside;
    [SerializeField] private SpriteRenderer[] quaterSRS;

    public static bool goingToDungeon = false;
    
    private void Awake()
    {
        i = this;
        UpdateSlider(televalMax);
    }

    private IEnumerator Start()
    {
        CS = CharacterScript.CS;
        recall = IM.i.pi.Player.Portal;
        recall.started += _ => StartPortal();
        GS.OnNewEra += i =>
        {
            foreach (SpriteRenderer sr in quaterSRS)
            {
                sr.material = GS.MatByEra(i, true);
            }
        };
        recall.Enable();
        OnDamageCancel = dmg => { if (dmg < 0f && timer > 0f) { Cancel(); } };
        CharacterScript.CS.ls.onDamageDelegate += OnDamageCancel;
        IncrementAnim(0);
        GS.OnNewEra += ctx =>
        {
            IncrementAnim(ctx);
            FX.GetComponent<Renderer>().material = GS.MatByEra(ctx);
        };
        SpawnManager.instance.OnNewDay += YesPortal;
        foreach (Transform t in slTs)
        {
            yield return null;
            LeanTween.moveLocalY(t.gameObject, t.localPosition.y + Random.Range(-0.08f, 0.08f), 0.6f).setLoopPingPong().setEaseInCubic();
            LeanTween.moveLocalX(t.gameObject, t.localPosition.x + Random.Range(-0.1f, 0.1f), 1f).setLoopPingPong().setEaseShake();
        }
        outside.material = Vessel.mat;
    }

    private void UpdateSlider(float val, bool onoffcall = false)
    {
        val = GS.PutInRange(val, 0f, televalMax);
        televal = val;
        float length = 180f * (1 - (val / televalMax));
        slrt.sizeDelta = new Vector2(length, 0.5f * length + 60);
        slCam.orthographicSize = 0.05f + 0.007f * length;
        if (!onoffcall)
        {
            ri.color = Color.Lerp(colGrad[0], colGrad[1], val / televalMax);
        }
    }

    private void IncrementAnim(int era)
    {
        PS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        //Destroy(PS.gameObject, 2f);
        PS.gameObject.SetActive(false);
        PS = PSes[era];
        PS.GetComponent<ParticleSystemRenderer>().trailMaterial = GS.MatByEra(era);
        anim.SetFloat("Blend", era);
        ankorSR.sprite = ankorSprites[era];
        colGrad = new Color[] { Color.Lerp(GS.ColFromEra(), Color.white, 0.35f), Color.Lerp(GS.ColFromEra(), Color.black, 0.5f) };
    }

    private void Update()
    {
        if (cd)
        {
            if (!waitMaxSlide && ri.color != offTPColour)
            {
                UpdateSlider(televal + Time.deltaTime);
            }
        }
        else
        {
            UpdateSlider(televal - Time.deltaTime);
        }
        if (timer > 0f)
        {
            if (recall.ReadValue<float>() == 0f && !clickSkip)
            {
                Cancel();
            }
            else
            {
                timer -= Time.deltaTime;
                if (IM.i.pi.Player.Portal.enabled)
                {
                    if (timer <= 0f)
                    {
                        Portal();
                    }
                }
                else
                {
                    Cancel();
                }
            }
        }
        if (televal == televalMax)
        {
            slCam.enabled = false;
        }
        else
        {
            slCam.enabled = true;
        }
    }

    public void UpdateCamera()
    {
        Vector3 pos = GS.CS().position;
        pos.z = -100;
        dungeonCamera.position = pos;
    }

    public static bool CanPortal()
    {
        return GS.CanAct() && i.canPortal && i.televal >= i.televalMax;
    }

    public bool StartPortal()
    {
        if (CanPortal())
        {
            cd = false;
            FX.Play();
            televalMax = 2f;
            timer = televalMax;
            UpdateSlider(televalMax);
            return true;
        }
        return false;
    }

    public void Portal(bool noDistort = false)
    {
        if (noDistort)
        {
            StartCoroutine(OrbManager.LerpDistortion(2f));
        }
        waitMaxSlide = true;
        Cancel();
        if (!inDungeon)
        {
            foreach (Vessel v in Vessel.vessels)
            {
                v.InvokeThisVessel();
            }
            stopBool = false;
            anim.SetBool("Morph", true);
            PortalTrigger.i.FadeIn();
            StartCoroutine(ToDungeonSequence());
        }
        else
        {
            goingHomeNow = true;
            if (!CharacterScript.CS.ls.hasDied)
            {
                SpawnManager.instance.AccelerateWave(false); //no need to call this twice
            }
            StartCoroutine(ToHomeSequence(noDistort));
        }
    }

    public void NoPortal()
    {
        if(canPortal)
        {
            canPortal = false;
            ri.color = offTPColour;
            UpdateSlider(1f,true);
        }
    }

    public void YesPortal()
    {
        if(canPortal != true)
        {
            canPortal = true;
            ri.color = Color.green;
            UpdateSlider(0f,true);
        }
    }

    private IEnumerator ToHomeSequence(bool noDistort = false)
    {
        int id = 0;
        if (!noDistort)
        {
            if (SetM.quickTransition)
            {
                id = SpawnManager.instance.NewTS(3f, 5);
            }
            UIManager.i.cg.alpha = 0;
            CameraScript.i.DistortLens(true, true, false);
            CameraScript.Flip(Vector2.zero, 2.75f, 1f);
            IM.i.StartCoroutine(IM.i.PS5InitColor(2));
            IM.i.BlockColourChangeForT(3f);
            IM.i.Rumble(4f, 3, true, true, 0.1f, 0.35f, 0.625f);
        }
        
        DM.i.activeRoom.ResetRoom();
        PortalTrigger.i.OffForT((20 - 4 * Mathf.Log(CS.attributes[2] + 1)) / 2);
        yield return null;
        if (swapTeleIcon)
        {
            swapTeleIcon = false;
            YesPortal();
        }
        PortalFR();
        yield return null;
        StartCoroutine(IQuarters(false));
        StartCoroutine(Enlighten(false));
        yield return new WaitForSeconds(0.25f);
        
        anim.SetBool("Morph", false);
        yield return new WaitForSeconds(1.5f);
        CameraScript.i.StopShake();
        if (id != 0 || SetM.quickTransition)
        {
            SpawnManager.instance.CancelTS(id);
        }
        PortalTrigger.i.OffForT(5f);
        PortalTrigger.i.FadeOut(true);
        CharacterScript.CS.ls.hasDied = false;
        CharacterScript.speedy = false;
    }

    private IEnumerator ToDungeonSequence()
    {
        int id = 0;
        if (SetM.quickTransition)
        {
            id = SpawnManager.instance.NewTS(3f, 5);
        }
        goingToDungeon = true;
        Transform t = GS.CS();
        IM.i.pi.Player.Disable();
        UIManager.CloseAllUIs();
        t.LeanMove(Vector3.zero, 2f).setEaseInOutCirc();
        yield return new WaitForSeconds(1.5f);
        t.SetParent(ankorSR.transform);
        t.localScale = new Vector3(1f, 1f, 1f);
        anim.SetBool("Spin", true);
        while (stopBool == false)
        {
            t.localPosition = Vector3.zero;
            anim.SetBool("Morph", true);
            yield return null;
        }
        yield return new WaitForSeconds(3.75f);
        if (id != 0 || SetM.quickTransition)
        {
            SpawnManager.instance.CancelTS(id);
        }
        MechaSuit.MakeHappy();
        CharacterScript.speedy = true;
        yield return new WaitForSeconds(3.25f);
        goingToDungeon = false;
        //UIManager.i.SetTelePhone(UIManager.TeleMode.Core,1f);
    }

    public void FadeOutOnTeleport()
    {
        PortalTrigger.i.FadeOut(true);
    }

    private void StartedSpinning()
    {
        CameraScript.i.DistortLens(true, false, true);
        PS.gameObject.SetActive(true);
        PS.Play();
        IM.i.StartCoroutine(IM.i.PS5InitColor(3));
        IM.i.BlockColourChangeForT(4.5f);
        IM.i.Rumble(4.28f, 4, true, false, 0.1f, 0.75f);
        StartCoroutine(IQuarters(true));
        StartCoroutine(Enlighten(true));
    }

    private IEnumerator Enlighten(bool increase)
    {
        if (!increase)
        {
            foreach (Light2D l in lights)
            {
                l.intensity = 2f;
                l.pointLightOuterAngle = 135f;
                l.pointLightOuterAngle = 30f;
            }
        }
        for (float i = 4f; i > 0f; i -= Time.deltaTime)
        {
            if (increase)
            {
                foreach (Light2D l in lights)
                {
                    l.intensity += Time.deltaTime * (4f - i) * 0.2f;
                    l.pointLightOuterAngle = Mathf.Lerp(l.pointLightOuterAngle, 90f, 0.75f * Time.deltaTime);
                    l.pointLightInnerAngle = Mathf.Lerp(l.pointLightInnerAngle, 30f, 0.75f * Time.deltaTime);
                }
            }
            else
            {
                foreach (Light2D l in lights)
                {
                    l.intensity -= Time.deltaTime * Mathf.Abs(i - 4f) * 0.2f;
                    l.pointLightOuterAngle = Mathf.Lerp(l.pointLightOuterAngle, 210f, 0.75f * Time.deltaTime);
                    l.pointLightInnerAngle = Mathf.Lerp(l.pointLightInnerAngle, 90f, 0.75f * Time.deltaTime);
                }
                if (lights[0].intensity <= 0.5f)
                {
                    break;
                }
            }
            yield return null;
        }
        if (increase)
        {
            yield return new WaitForSeconds(1.5f);
        }
        foreach (Light2D l in lights)
        {
            l.intensity = 0.4f;
            l.pointLightOuterAngle = 210f;
            l.pointLightInnerAngle = 90f;
        }
    }

    public void StopEmitting()
    {
        var em = PS.emission;
        em.rateOverTime = 0;
    }

    public void IncrementQuarters(bool increment)
    {
        if (increment)
        {
            var emission = PS.emission;
            emission.rateOverTime = 100f + 150f * quarterInt * quarterInt;
            quarterInt++;
            var shape = PS.shape;
            shape.arc = 90f - 15f * quarterInt;
            if (quarterInt >= quarterSprites.Length)
            {
                quarterInt = 0;
            }
        }
        else
        {
            var emission = PS.emission;
            emission.rateOverTime = emission.rateOverTime.constant - 10 * quarterInt;
            quarterInt--;
            if (quarterInt < 0)
            {
                quarterInt = quarterSprites.Length - 1;
            }
        }
        foreach (SpriteRenderer sr in quarters)
        {
            sr.sprite = quarterSprites[quarterInt];
        }
    }

    private IEnumerator IQuarters(bool increment)
    {
        var emission = PS.emission;
        if (increment)
        {
            emission.rateOverTime = 35;
        }
        else
        {
            emission.rateOverTime = 600;
        }
        if (increment)
        {
            quarterInt = -1;
        }
        else
        {
            quarterInt = 6;
        }
        IncrementQuarters(increment);
        float tim;
        for (int i = 0; i < 5; i++)
        {
            tim = 0.66f;
            while (tim > 0f)
            {
                tim -= Time.deltaTime;
                yield return null;
            }
            IncrementQuarters(increment);
        }
        if (!increment)
        {
            yield break;
        }
        yield return new WaitForSeconds(0.25f);
        CameraScript.Flip(DM.i.activeRoom.safeSpawn.position, 6f, 2f);
        yield return new WaitForSeconds(1.75f);
        IncrementQuarters(increment); 
    }

    public void PortalFR() //called in spin
    {
        CharacterScript.CS.ls.Change(999f,-1);
        ResourceManager.instance.ChangeFuels(999f);
        GS.CS().SetParent(null);
        GS.CS().localScale = new Vector3(1, 1, 1);
        stopBool = true;
        anim.SetBool("Spin", false);
        PS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        this.QA(() => PS.gameObject.SetActive(false), 2f);
        StopCoroutine(nameof(ToDungeonSequence));
        IM.i.pi.Player.Enable();
        inDungeon = !inDungeon;
        CS.locked = true;
        foreach (AllyAI AI in CharacterScript.CS.group)
        {
            AI.skrskr = false;
            if (!inDungeon)
            {
                AI.transform.position = transform.position + new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0f);
                AI.targetPoint = AI.transform.position;
            }
            else
            {
                AI.transform.position = DM.i.activeRoom.safeSpawn.position + new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0f);
                AI.targetPoint = AI.transform.position;
            }
        }
        CS.transform.position = inDungeon ? DM.i.activeRoom.safeSpawn.position : transform.position;
        CS.transform.position = new Vector3(CS.transform.position.x, CS.transform.position.y, -1);
        MechaSuit.m.TPFollowers();
        if (inDungeon)
        {
            MapManager.i.SetMap(true);
            DM.i.activeRoom.OnEnter();
            foreach (OrbScript t in ResourceManager.instance.heldOrbs)
            {
                t.transform.localScale = Vector3.one;
            }
        }
        else
        {
            MapManager.i.SetMap(false);
            if (!SpawnManager.instance.waveCompleted)
            {
                Invoke(nameof(NoPortal),3f);
            }
        }
        StartCoroutine(OrbManager.LerpDistortion(1f,2.5f));

        StartCoroutine(TurnOffSoonI());
        onTeleport?.Invoke(inDungeon);
        MechaSuit.m.RemoveTemporary();
    }

    public void QuickOffSlider()
    {
        UpdateSlider(0f,false);
    }

    IEnumerator TurnOffSoonI()
    {
        yield return new WaitForSeconds(6f);
        waitMaxSlide = false;
    }

    public void Cancel()
    {
        clickSkip = false;
        FX.Stop();
        timer = -1f;
        cd = true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        ResourceManager.instance.IterateMagnets();
        if (collision.name == "Character")
        {
            BlueprintManager.LootSafe();
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.name == "Character")
        {
            BlueprintManager.LootSafe(false);
        }
    }

    public void Win()
    {
        WinUI.SetActive(true);
        tsID = SpawnManager.instance.NewTS(0, Mathf.Infinity);
        IM.i.KeepControllerMoving();
    }

    public void Lose()
    {
        LoseUI.SetActive(true);
        tsID = SpawnManager.instance.NewTS(0, Mathf.Infinity);
        IM.i.KeepControllerMoving();
    }

    public void DefeatedBoss()
    {
        SpawnManager.instance.dayState = SpawnManager.DayState.Day;
        SpawnManager.instance.timeText.text = "Ember's Edge Deconstructing";
        SpawnManager.instance.timeText.color = new Color(0.25f, 1f, 0.35f);
        IM.i.pi.Player.Portal.Disable();
        CharacterScript.CS.AS.interactive = false;
        GS.Stat(CharacterScript.CS,"invulnerable",5f);
        CharacterScript.CS.ls.Change(1000000, 0);
        foreach (GameObject g in SpawnManager.instance.alives)
        {
            if (g != null) { g.GetComponentInChildren<LifeScript>().OnDie(); }
        }
        SpawnManager.instance.alives.Clear();
        StartCoroutine(DefeatedBossI());
    }

    private IEnumerator DefeatedBossI()
    {
        yield return new WaitForSeconds(0.1f);
        Portal();
        yield return new WaitForSeconds(2f);
        foreach (EmbersEdge E in SpawnManager.instance.EEs)
        {
            E.Dissapear();
        }
        CameraScript.i.StartTemporaryZoom(1.5f, 3f, 0.02f, 0.01f);
        yield return new WaitForSeconds(8f);
        GS.IncrementEra();
        IM.i.pi.Player.Portal.Enable();
        CharacterScript.CS.AS.interactive = true;
        SpawnManager.instance.timeText.text = "Ember's Edge Inactive";
        SpawnManager.instance.timeText.color = new Color(0.849f, 0.849f, 0.849f);
        yield return new WaitForSeconds(5f);
        if (GS.era == 1)
        {
            Baron.current.Two();
        }
        else
        {
            Baron.current.Three();
        }
        YesPortal();
    }

    public void TeleShortCut(Room r)
    {
        if(r == DM.i.activeRoom)
        {
            return;
        }
        UIManager.i.FadeOutCanvas();
        IM.i.pi.Disable();
        StartCoroutine(TeleShortCutI(r.safeSpawn.position));

        IEnumerator TeleShortCutI(Vector2 position)
        {
            yield return new WaitForSeconds(1f);
            CameraScript.i.DistortLens(true, true);
            yield return new WaitForSeconds(1f);
            Collider2D[] results = new Collider2D[10];
            CharacterScript.CS.AS.rb.GetAttachedColliders(results);
            foreach (Collider2D col in results)
            {
                if(col != null)
                {
                    col.enabled = false;
                }
            }
            LeanTween.move(CharacterScript.CS.gameObject, position, 1.25f);
            yield return new WaitForSeconds(1.25f);
            CameraScript.i.DistortLens(false, true);
            yield return new WaitForSeconds(0.75f);
            UIManager.i.FadeInCanvas();
            foreach (Collider2D col in results)
            {
                if (col != null)
                {
                    col.enabled = true;
                }
            }
            IM.i.pi.Enable();
            r.OnEnter();
        }
    }
    
  
}