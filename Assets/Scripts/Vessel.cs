using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Vessel : Building
{
    public MechanismSO bp;
    [SerializeField] private Transform[] ts;
    private float[] speeds = new float[3];
    [SerializeField] public Image img;

    [SerializeField] ParticleSystem FX;
    private ParticleSystem.EmissionModule em;
    private ParticleSystem.ShapeModule shap;

    [SerializeField] private ParticleSystem FX2;
    private ParticleSystem.EmissionModule em2;
    private ParticleSystem.ShapeModule shap2;

    public bool engaged;
    public static Material mat;
    private static readonly int Thecolor = Shader.PropertyToID("thecolor");

    public Transform combinerDestination;
    public static List<Vessel> vessels;
    public Part p;

    private bool readyToGive = false;
    private bool preventOpen = false;

    public override void Start()
    {
        base.Start();
        em = FX.emission;
        em2 = FX2.emission;
        shap = FX.shape;
        shap2 = FX2.shape;
        FX.GetComponent<ParticleSystemRenderer>().material = mat;
        FX2.GetComponent<ParticleSystemRenderer>().material = mat;
        OnClose += () => UIManager.partOptionOpened = false;
        vessels.Add(this);
    }
    
   
    public void InvokeThisVessel()
    {
        if (!enabled) return;
        if (bp == null) return;
        if (p != null) return;
        if (!readyToGive) return;
        preventOpen = true;
        readyToGive = false;
        RefreshManager.i.StartCoroutine(EnlightenVesselMaterial());
        UsePart(); 
        RefreshManager.i.StartCoroutine(WaitAndFinish());
    }

    private IEnumerator WaitAndFinish()
    {
        while (engaged)
        {
            yield return null;
        }

        MechaSuit.m.AddParts(new MechanismSO[] { bp });
        p = MechaSuit.m.parts[^1];

        PortalScript.i.outside.LeanSRColor(Color.clear, 2.5f)
                   .setEaseInSine()
                   .setOnComplete(() => 
                     mat.SetColor(Thecolor, new Color(0.25f, 0.25f, 0.25f, 1f))
                   );
        
        RefreshManager.i.QA(() =>
        {
            InstantAct(bp, false);
            preventOpen = false;
            Upgrade(() =>
            {
                readyToGive = true;
            }, bp.powerRequired);

        },3f);
    }


    public override void OnClick()
    {
        if (!enabled) return;
        if (preventOpen) return;
        if(!UIParent.activeInHierarchy) RefreshToOptionsTiles();
        UIManager.currentVessel = this;
        base.OnClick();
    }

    protected virtual void RefreshToOptionsTiles()
    {
        ResetTiles();
        AddSlot(new int[]{0,0,0,0}, "Weapons", UIManager.i.partClassifierImages[0],false, () => ShowParts(Part.PartType.Weapon));
        AddSlot(new int[]{0,0,0,0}, "Abilities", UIManager.i.partClassifierImages[1],false, () => ShowParts(Part.PartType.Ability));
        AddSlot(new int[]{0,0,0,0}, "Automations", UIManager.i.partClassifierImages[3],false, () => ShowParts(Part.PartType.Automation));
        AddSlot(new int[]{0,0,0,0}, "Melees", UIManager.i.partClassifierImages[8],false, () => ShowParts(Part.PartType.Melee));
        AddSlot(new int[]{0,0,0,0}, "Defences", UIManager.i.partClassifierImages[4],false, () => ShowParts(Part.PartType.Defence));
        AddSlot(new int[]{0,0,0,0}, "Kinematics", UIManager.i.partClassifierImages[5],false, () => ShowParts(Part.PartType.Kinematic));
        AddSlot(new int[]{0,0,0,0}, "Resources", UIManager.i.partClassifierImages[6],false, () => ShowParts(Part.PartType.Energy));
        AddSlot(new int[]{0,0,0,0}, "Utility", UIManager.i.partClassifierImages[7],false, () => ShowParts(Part.PartType.Utility));
        AddSlot(new int[]{0,0,0,0}, "Boosts", UIManager.i.partClassifierImages[2],false, () => ShowParts(Part.PartType.Boost));
        AddSlot(new int[]{0,0,0,0}, "Standalone", UIManager.i.partClassifierImages[9],false, () => ShowParts(Part.PartType.Standalone));
        AddSlot(new int[] { 0, 0, 0, 0 }, "Remove Part", UIManager.i.partClassifierImages[10],false, () =>
        {
            if (engaged) return;
            RemPart(true);
        });
    }

    void RemPart(bool remBP)
    {
        if (remBP)
        {
            bp = null;
            DeletePart();
        }
        img.color = Color.clear;
        ResetTiles();
        UIParent.SetActive(false);
    }

    void DeletePart()
    {
        if (p != null)
        {
            MechaSuit.m.MurkPart(p,MechaSuit.m.parts.IndexOf(p),true);
            p = null;
        }
    }
    
    void ShowParts(Part.PartType p)
    {
        ResetTiles();
        UIManager.partOptionOpened = true;
        AddSlot(new int[]{0,0,0,0}, "Back", null,false, GoBackButton);
        foreach(Blueprint b in BlueprintManager.researched)
        {
            if (b is MechanismSO m)
            {
                Part.PartType typ = m.p.taip;
                if(typ != p) continue;
                AddSlot(new int[]{0,0,0,0}, b.name, b.s, true, () =>
                {
                    RemoveIcons();
                    readyToGive = false;
                    Upgrade(() =>
                    {
                        readyToGive = true;
                    }, m.powerRequired);
                }, false,  () => InstantAct(b));
                tiles[^1].SetTextN(m.powerRequired);
            }
        }
        UpdateUI();
    }

    public void GoBackButton()
    {
        ResetTiles();
        RefreshToOptionsTiles();
        UpdateUI();
        UIManager.partOptionOpened = false;
    }

    protected void ResetTiles()
    {
        for(int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] != null)
            {
                Destroy(tiles[i].gameObject);
            }
        }
        tiles = new List<BaseTile>();
    }

    public void InstantAct(Blueprint b, bool rem = true)
    {
        if (rem)
        {
            RemPart(true);
        }
        bp = (MechanismSO)b;
        img.sprite = b.s;
        img.preserveAspect = true;
        img.color = Color.white;
        img.transform.localScale = Vector3.zero;
        LeanTween.scale(img.gameObject, Vector3.one * 0.5f, 0.5f).setEaseOutBack();
        ResetTiles();
        UIParent.SetActive(false);
    }
    void UsePart()
    {
        engaged = true;
        ResetTiles();
        UIParent.gameObject.SetActive(false);
        StopAllCoroutines();
        shap.arcSpeedMultiplier = 2f;
        StartCoroutine(combinerDestination != null ? CombineMove() : Move());
    }

    IEnumerator CombineMove()
    {
        FX.gameObject.SetActive(true);
        FX2.gameObject.SetActive(true);
        StartCoroutine(Emission());
        Quaternion rot = GS.VTQ(transform.position - combinerDestination.position);
        rot = Quaternion.Euler(Mathf.Sin(rot.eulerAngles.z * Mathf.Deg2Rad) * 30f, 0f, rot.eulerAngles.z + 180f);
        for(float t = 0f; t < 1.5f; t+=2*Time.deltaTime)
        {
            img.transform.rotation = Quaternion.Lerp(img.transform.rotation,rot,2*t*Time.deltaTime);
            yield return null;
        }
        img.transform.LeanMove(combinerDestination.position, 0.5f).setEaseOutCubic();
        yield return new WaitForSeconds(0.75f);
        engaged = false;
        FX.gameObject.SetActive(false);
        FX2.gameObject.SetActive(false);
        img.transform.rotation = Quaternion.identity;
        img.transform.localPosition = new Vector3(0f,0f,0.5f);
    }
    
    IEnumerator Move()
{
    FX.gameObject.SetActive(true);
    FX2.gameObject.SetActive(true);
    StartCoroutine(Emission());

    for (float t = 0f; t < 1.5f; t += Time.deltaTime)
    {
        Vector3 currentTargetPos = CharacterScript.CS.transform.position;
        Quaternion rot = Quaternion.LookRotation(currentTargetPos - transform.position);

        rot = Quaternion.Euler(
            Mathf.Sin(rot.eulerAngles.z * Mathf.Deg2Rad) * 30f,
            0f,
            rot.eulerAngles.z + 180f
        );

        img.transform.rotation = Quaternion.Lerp(
            img.transform.rotation,
            rot,
            0.4f * t * Time.deltaTime
        );
        yield return null;
    }

    float n = em.rateOverTime.constant;  
    float multiplier = 1f;  

    float closeEnoughDistance = 0.2f;  // how close we need to be before stopping
    float chaseTime = 0f;             // how long we've been chasing
    float baseChaseSpeed = 2.0f;      // base speed for the chase
    float accelFactor   = 2.0f;       // how quickly it speeds up over time

    while (Vector3.Distance(img.transform.position, CharacterScript.CS.transform.position) > closeEnoughDistance)
    {
        chaseTime += Time.deltaTime;

        float chaseSpeed = baseChaseSpeed + accelFactor * chaseTime; 

        Vector3 currentTargetPos = CharacterScript.CS.transform.position;
        Quaternion rot = Quaternion.LookRotation(currentTargetPos - transform.position);
        rot = Quaternion.Euler(
            Mathf.Sin(rot.eulerAngles.z * Mathf.Deg2Rad) * 30f,
            0f,
            rot.eulerAngles.z + 180f
        );

        img.transform.rotation = Quaternion.Lerp(
            img.transform.rotation,
            rot,
            Time.deltaTime * (1.5f + chaseTime)
        );

        img.transform.position = Vector3.MoveTowards(
            img.transform.position,
            currentTargetPos,
            chaseSpeed * Time.deltaTime
        );

        shap.arcSpeedMultiplier  = 2f + 2f * multiplier * chaseTime;
        shap2.arcSpeedMultiplier = -2f - 2f * multiplier * chaseTime;

        shap.radius = Mathf.Lerp(
            shap.radius,
            0.08f,
            0.1f * Time.deltaTime * chaseTime * chaseTime * chaseTime * Mathf.Pow(multiplier, 3)
        );
        shap2.radius = shap.radius - 0.075f;
        em.rateOverTime = em2.rateOverTime = n + chaseTime * multiplier * 0.25f * n;

        yield return null;
    }

    PortalScript.i.outside.color = Color.Lerp(
        PortalScript.i.outside.color,
        Color.white,
        0.05f
    );
    PortalScript.i.outside.color = Color.Lerp(
        PortalScript.i.outside.color,
        Color.white,
        0.1f
    );

    this.QA(() =>
    {
        em.rateOverTime  = 0f;
        em2.rateOverTime = 0f;
    }, 0.1f);

    StartCoroutine(StopSpinning());

    for (float t = 1f; t > 0f; t -= Time.deltaTime)
    {
        img.color = new Color(1f, 1f, 1f, t);
        img.transform.localScale = new Vector3(t, t, t);
        yield return null;
    }

    engaged = false;
    yield return new WaitForSeconds(1.5f);

    FX.gameObject.SetActive(false);
    FX2.gameObject.SetActive(false);

    img.transform.rotation      = Quaternion.identity;
    img.transform.localPosition = new Vector3(0f, 0f, 0.5f);
}

    IEnumerator Emission()
    {
        em.rateOverTime = 120f * (0.1f + 0.9f * SetM.FXQuality);
        for (float t = 0f; t < 100f; t += 40f * Time.deltaTime)
        {
            shap.arcSpeedMultiplier = 0.5f + 0.015f * t;
            shap2.arcSpeedMultiplier = -shap.arcSpeedMultiplier;
            FX.transform.localScale = FX2.transform.localScale = new Vector3(t, t, t)*0.01f;
            shap.radius = 0.1f + 0.003f * t;
            shap2.radius = shap.radius - 0.075f;
            yield return null;
        }
        FX.transform.localScale = FX2.transform.localScale = Vector3.one;
    }

    private void Update()
    {
        for (int i = 0; i < 3; i++)
        {
            ts[i].transform.Rotate(Vector3.forward, Time.deltaTime * speeds[i]);
        }

        if (engaged) return;
        if (readyToGive)
        {
            if (p == null & img.transform.localScale.x == 0.5f)
            {
                LeanTween.scale(img.gameObject, Vector3.one, 0.5f).setEaseOutBack();
            }
        }

        if (p == null || !readyToGive)
        {
            img.transform.rotation = Quaternion.Euler(0f,
                45f*Mathf.Sin(Time.deltaTime),
                10f*Mathf.Cos(6f*Time.time/4));
        }
        else 
        {
            img.transform.rotation = Quaternion.Euler(0f, 45f*Mathf.Sin(2f*Time.time), 30f*Mathf.Sin(2f*Time.time));
        }
    }

    public IEnumerator StartSpinning()
    {
        for (float t = 0f; t < 2.25f; t += 2*Time.deltaTime)
        {
            speeds[0] = Mathf.Lerp(speeds[0], 60f, Time.deltaTime * t / 4.5f);
            yield return null;
        }
        for (float t = 0f; t < 2.25f; t += 2*Time.deltaTime)
        {
            speeds[0] = Mathf.Lerp(speeds[0], 60f, Time.deltaTime*(t + 2.25f)/4.5f);
            speeds[1] = Mathf.Lerp(speeds[1], -90f, Time.deltaTime*t/4.5f);
            yield return null;
        }
        speeds[0] = 60f;
        for (float t = 0f; t < 2.25f; t += 2*Time.deltaTime)
        {
            speeds[1] = Mathf.Lerp(speeds[1], -90f, Time.deltaTime*(t + 2.25f)/4.5f);
            speeds[2] = Mathf.Lerp(speeds[2], 150f, Time.deltaTime*t/4.5f);
            yield return null;
        }
        speeds[1] = -90f;
        for (float t = 0f; t < 2.25f; t += 2*Time.deltaTime)
        {
            speeds[2] = Mathf.Lerp(speeds[2], 150f, Time.deltaTime*(t + 2.25f)/4.5f);
            yield return null;
        }
        speeds[2] = 150f;
    }

    private IEnumerator StopSpinning()
    {
        for (float t = 0f; t < 2.25f; t += 2* Time.deltaTime)
        {
            speeds[2] = Mathf.Lerp(speeds[2], 0f, Time.deltaTime * t / 4.5f);
            yield return null;
        }
        for (float t = 0f; t < 2.25f; t += 2* Time.deltaTime)
        {
            speeds[2] = Mathf.Lerp(speeds[2], 0f, Time.deltaTime*(t + 2.25f)/4.5f);
            speeds[1] = Mathf.Lerp(speeds[1], 0f, Time.deltaTime * t / 4.5f);
            yield return null;
        }
        speeds[2] = 0f;
        for (float t = 0f; t < 2.25f; t += 2* Time.deltaTime)
        {
            speeds[1] = Mathf.Lerp(speeds[1], 0f, Time.deltaTime*(t + 2.25f)/4.5f);
            speeds[0] = Mathf.Lerp(speeds[0], 0f, Time.deltaTime * t / 4.5f);
            yield return null;
        }
        speeds[1] = 0f;
        for (float t = 0f; t < 2.25f; t +=2*Time.deltaTime)
        {
            speeds[0] = Mathf.Lerp(speeds[0], 0f, Time.deltaTime*(t + 2.25f)/4.5f);
            yield return null;
        }
        speeds[0] = 0f;
    }
    
    public static IEnumerator EnlightenVesselMaterial()
    {
        for (float t = 0f; t < 2f; t += 0.3f*Time.deltaTime)
        {
            float x = 0.25f + t*t*t; 
            mat.SetColor(Thecolor, new Color(x, x, x, 1f));
            yield return null;
        }
    }
}