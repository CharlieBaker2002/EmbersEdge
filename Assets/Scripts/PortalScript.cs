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

    [SerializeField] float televal;
    [SerializeField] float televalMax;
    const float CHANNEL_TIME = 0.6f;   // how long you hold V to teleport — short so it's easy to trigger
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

    public Sprite[] quarterSprites;
    public SpriteRenderer[] quarters;
    private int quarterInt = 0;

    private bool stopBool = false;
    public bool swapTeleIcon = false;
    bool waitMaxSlide = false;
    public bool clickSkip = false; //Sets to teleport you automatically

    public static bool goingHomeNow = false; //used in DistortLens (cameraScript) to assess whether to set next day.
    [SerializeField] private SpriteRenderer[] quaterSRS;

    public static bool goingToDungeon = false;

    [SerializeField] public SpriteRenderer[] rimSRs;
    [SerializeField] public Sprite[] rimSprites;
    [SerializeField] public Sprite[] rimLightSprites;
    [SerializeField] public Sprite[] outerRimSprites;

    [SerializeField] private Ember portalEmber;
    [SerializeField] private Ember characterEmber;

    private float emitRate = 0f;
    private float emitTimer = 0.1f;
    private float charEmitRate = 0f;
    private float charEmitTimer = 0.1f;
    private int emitRampTweenId = -1;
    
    private void Awake()
    {
        i = this;
        televalMax = CHANNEL_TIME;
        UpdateSlider(televalMax);
        charEmitRate = 0f;
    }

    private IEnumerator Start()
    {
        CS = CharacterScript.CS;
        recall = IM.i.pi.Player.Portal;
        recall.started += _ => OnRecallPressed();
        GS.OnNewEra += i =>
        {
            foreach (SpriteRenderer sr in quaterSRS)
            {
                sr.material = GS.MatByEra(i, true);
            }
        };
        GS.OnNewEra += era =>
        {
            rimSRs[0].sprite = rimSprites[era];
            rimSRs[1].sprite = rimLightSprites[era];
            rimSRs[2].sprite = outerRimSprites[era];
            rimSRs[1].material = GS.MatByEra(era, false, false);
        };
        recall.Enable();
        OnDamageCancel = dmg => { if (dmg < 0f && timer > 0f) { Cancel(); } };
        CharacterScript.CS.ls.onDamageDelegate += OnDamageCancel;
        IncrementAnim(0);
        GS.OnNewEra += ctx =>
        {
            IncrementAnim(ctx);
        };
        SpawnManager.instance.OnNewDay += YesPortal;
        foreach (Transform t in slTs)
        {
            yield return null;
            LeanTween.moveLocalY(t.gameObject, t.localPosition.y + Random.Range(-0.08f, 0.08f), 0.6f).setLoopPingPong().setEaseInCubic();
            LeanTween.moveLocalX(t.gameObject, t.localPosition.x + Random.Range(-0.1f, 0.1f), 1f).setLoopPingPong().setEaseShake();
        }
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
        anim.SetFloat("Blend", era);
        ankorSR.sprite = ankorSprites[era];
        colGrad = new Color[] { Color.Lerp(GS.ColFromEra(), Color.white, 0.35f), Color.Lerp(GS.ColFromEra(), Color.black, 0.5f) };
    }

    private void Update()
    {
        // The dungeon minimap camera used to be repositioned by Room.OnEnter as the player moved
        // between rooms. The mining dungeon has no rooms, so drive it here instead: keep the dungeon
        // camera centred on the player every frame while underground.
        if (inDungeon && dungeonCamera != null) UpdateCamera();

        emitTimer -= Time.deltaTime * emitRate;
        while (emitTimer <= 0f)
        {
            emitTimer += 1f;
            var emb = Instantiate(portalEmber, Vector3.zero, Quaternion.identity, GS.FindParent(GS.Parent.fx));
            float t = Mathf.Clamp01(emitRate / 20f);
            emb.to = (Vector3)Random.insideUnitCircle.normalized * Random.Range(0.5f, 0.5f + 9f * t);
            emb.flightTime = Mathf.Lerp(2f, 0.15f, t);
        }
        
        charEmitTimer -= Time.deltaTime * charEmitRate;
        while (charEmitTimer <= 0f)
        {
            charEmitTimer += 1f;
            Transform charT = GS.CS();
            if (!charT) break;
            Vector3 target = charT.position;
            Vector3 origin = target + (Vector3)Random.insideUnitCircle.normalized * Random.Range(0f, 0.25f);
            var cemb = Instantiate(characterEmber, origin, GS.RandRot(), GS.FindParent(GS.Parent.fx));
            cemb.to = target;
            cemb.flightTime = 0.6f;   // short — so letting go mid-channel doesn't leave embers streaming in for seconds
        }
        
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
        // At base you can only head to the dungeon while peaceful & unarmed — not once a wave is
        // armed/pending or actively attacking. (Returning FROM the dungeon is always allowed.)
        if (!i.inDungeon && (SpawnManager.instance.waveArmed || SpawnManager.instance.dayState != SpawnManager.DayState.Day))
            return false;
        // No charge-up gate: you can (re)channel at any time — right after a wave ends, or even the
        // instant you let go and the slider is still recovering. The slider is now just feedback.
        return GS.CanAct() && i.canPortal;
    }

    // V key. In the dungeon the ember tether gets first claim on the press (throw onto a passive
    // core / untether — a core-locked line refuses and consumes the press). At base with an armed
    // wave, V SUMMONS the wave instead of charging a teleport (you can't go back to the dungeon
    // until it's cleared). Otherwise it charges a teleport as before.
    private void OnRecallPressed()
    {
        if (EmberTether.HandleRecall()) return;
        if (!inDungeon && SpawnManager.instance.waveArmed)
        {
            SpawnManager.instance.TryStartWave();
            return;
        }
        StartPortal();
    }

    public bool StartPortal()
    {
        if (CanPortal())
        {
            cd = false;
            charEmitRate = inDungeon ? 6f : 3f;
            televalMax = CHANNEL_TIME;
            timer = televalMax;
            UpdateSlider(televalMax);
            if (inDungeon)
                emitRampTweenId = LeanTween.value(gameObject, 0f, 8f, televalMax)
                    .setEaseInSine().setOnUpdate(x => emitRate = x).id;
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
        if (!inDungeon)
        {
            Ember.TriggerPortalBurst(GS.CS().position, Vector3.zero);
            // Dematerialise punch — fires as the first portal embers trail in from the base to the core.
        }
        Cancel();
        CharacterScript.CS.Hide();
        if (!inDungeon)
        {
            foreach (Vessel v in Vessel.vessels)
            {
                v.InvokeThisVessel();
            }
            stopBool = false;
            anim.SetBool("Morph", true);
            PortalTrigger.i.FadeIn();

            // Lock input and UI immediately so player can't move during burst
            goingToDungeon = true;
            SpawnManager.instance.HideWavePreview(); // drop the pre-dungeon forecast while we're away
            IM.i.pi.Player.Disable();
            UIManager.CloseAllUIs();
            // Teleporting out from INSIDE the manifestor trigger skips its OnTriggerExit2D, leaking its
            // key guides — a stale "V" would then block the dungeon's "Tether The Core" prompt.
            UIManager.DeleteKey("V");
            UIManager.DeleteKey("B");
            UIManager.DeleteKey("SELECT");

            // Character bursts in same direction as VFX (away from base), then snaps to zero
            Vector3 charPos = CS.transform.position;
            Vector3 burstDir = charPos.sqrMagnitude > 0.0001f ? charPos.normalized : Vector3.right;
            LeanTween.move(CS.gameObject, charPos + burstDir * 1.5f, 0.2f).setEase(LeanTweenType.easeOutExpo)
                .setOnComplete(() =>
                    LeanTween.move(CS.gameObject, Vector3.zero, 0.35f).setEase(LeanTweenType.easeInCubic)
                        .setOnComplete(() => StartCoroutine(ToDungeonSequence())));
        }
        else
        {
            goingHomeNow = true;
            // Returning home no longer auto-starts the wave. It is ARMED on arrival (PortalFR) and
            // summoned manually by the player (V / Tele-Phone). Death still force-starts it via
            // AccelerateWave(true) -> forceStartOnReturn.
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
            UpdateSlider(televalMax,true);   // show the portal READY (bar empty) — no wait after a wave
        }
    }

    private IEnumerator EmitReversePortalEmbers(Vector3 center)
    {
        yield return new WaitForSeconds(1.5f);
        // Rematerialise punch — the embers were flung outward; this fires as they reach back into the centre.
        //this.QA(() => Shockwave.Spawn(Vector3.zero, 2f, 0.5f, 4f), 0.8f);
        float elapsed = 0f;
        float duration = 1.5f;
        float rate = 8f;
        float t = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            t -= Time.deltaTime * rate;
            while (t <= 0f)
            {
                t += 1f;
                var emb = Instantiate(portalEmber, center + (Vector3)Random.insideUnitCircle.normalized * Random.Range(2f, 6f), Quaternion.identity, GS.FindParent(GS.Parent.fx));
                emb.to = Vector3.zero;
                emb.reversePortalEmber = true;
                emb.flightTime = Random.Range(0.4f, 0.8f);
            }
            yield return null;
        }
    }

    private IEnumerator ToHomeSequence(bool noDistort = false)
    {
        int id = 0;
        Shockwave.EEWave((Vector2)CameraScript.i.transform.position,8f);
        if (!noDistort)
        {
            if (SetM.quickTransition)
            {
                id = SpawnManager.instance.NewTS(3f, 5);
            }
            UIManager.i.cg.alpha = 0;
            CameraScript.i.locked = false;
            CameraScript.i.noMove = true;
            CameraScript.i.DistortLens(true, true, true);
            this.QA(() =>
            {
                CameraScript.Flip(Vector2.zero, 2.75f, 1f);
                Shockwave.Spawn(CameraScript.i.transform.position, 2.25f, 0.5f, 2f);
            },1f);
            this.QA(() =>
            {
                Shockwave.Spawn(Vector3.zero, 2.75f, -3f, 5f);
            },2f);

            StartCoroutine(EmitReversePortalEmbers(EmbersEdge.mainCore.transform.position));
            IM.i.StartCoroutine(IM.i.PS5InitColor(2));
            IM.i.BlockColourChangeForT(3f);
            IM.i.Rumble(4f, 3, true, true, 0.1f, 0.35f, 0.625f);
   
        }
        
        // The pocket ember the player is carrying home pays out as they rematerialise: it charges up
        // at the arrival point (inside the converging reverse-ember VFX) so the final landing
        // shockwave at (0,0) reads as the blast that launches it out into the base.
        if (EmberStore.pending > 0)
            this.QA(() => StartCoroutine(PayOutPendingEmber()), noDistort ? 0.75f : 1.1f);

        if (MineDungeonManager.i != null) MineDungeonManager.i.ResetActivePocket();
        else DM.i.activeRoom.ResetRoom();
        PortalTrigger.i.OffForT((20 - 4 * Mathf.Log(CS.attributes[2] + 1)) / 2);
        
        yield return null;
        if (swapTeleIcon)
        {
            swapTeleIcon = false;
            YesPortal();
        }
        Vector3 dungeonPos = GS.CS().position;
        Ember.TriggerPortalBurst(dungeonPos, dungeonPos);
        PortalFR();
        yield return null;
        StartCoroutine(IQuarters(false));
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

    public void StopEmitting()
    {
        emitRate = 0f;
    }

    static Ember extractEmberPrefab;   // the extractor's collect-ember (Resources/ExtractEmber)

    // The dungeon harvest arriving home: every ember held from completed pockets charges up at the
    // arrival point and shoots out of (0,0) into a building that wants it — the extractor's collect
    // animation (same prefab: charge-up, flicker, bezier flight, landing burst) run in reverse,
    // outward from the player instead of in from the map edge. Targets come from the cable network's
    // own demand order (constructors → ember generators → stores); each landing deposits for real.
    private IEnumerator PayOutPendingEmber()
    {
        int n = EmberStore.TakePending();
        if (n <= 0) yield break;
        if (EnergyManager.i == null) { EmberStore.Bank(n); yield break; }

        List<EmberConnector> targets = EnergyManager.i.ResolveEmberDemand(n);
        if (targets.Count < n) EmberStore.Bank(n - targets.Count);   // no store anywhere: tally the rest
        if (targets.Count == 0) yield break;

        if (extractEmberPrefab == null) extractEmberPrefab = Resources.Load<Ember>("ExtractEmber");
        if (extractEmberPrefab == null)
        {
            foreach (EmberConnector t in targets) EmberStore.Deliver(t, Vector3.zero);
            yield break;
        }

        float stagger = Mathf.Min(0.15f, 2f / targets.Count);
        foreach (EmberConnector target in targets)
        {
            EmberConnector c = target;
            var e = Instantiate(extractEmberPrefab, (Vector3)(Random.insideUnitCircle * 0.35f),
                GS.RandRot(), GS.FindParent(GS.Parent.fx));
            e.to = c.transform.position + (Vector3)(Random.insideUnitCircle * 0.2f);
            e.onComplete += () => EmberStore.Deliver(c, e.transform.position);
            yield return new WaitForSeconds(stagger);
        }
    }

    public void MakeSpawnFX(Vector2 centre)
    {
        StartCoroutine(Spawn());
        return;

        IEnumerator Spawn()
        {
            for (int z = 0; z < 100; z++)
            {
                var emb = Instantiate(portalEmber, centre, Quaternion.identity, GS.FindParent(GS.Parent.fx));
                emb.to = (Vector3)centre + (Vector3)Random.insideUnitCircle.normalized * Random.Range(1f,5f);
                emb.flightTime = 3f;
                // var em = emb.trailPS[0].emission; em.rateOverDistanceMultiplier *= 3f; 
                // var man = emb.trailPS[0].main; man.simulationSpeed = 3f;
                // em = emb.trailPS[1].emission; em.rateOverDistanceMultiplier *= 3f; 
                // man = emb.trailPS[1].main; man.simulationSpeed = 3f;
                yield return GS.WFFU;
            }
        }
    }

    private IEnumerator ToDungeonSequence()
    {
        int id = 0;
        if (SetM.quickTransition)
        {
            id = SpawnManager.instance.NewTS(3f, 5);
        }
        // goingToDungeon, Player.Disable, CloseAllUIs already done in Portal() before burst
        Transform t = GS.CS();
        // character already at Vector3.zero from burst — skip LeanMove
        LeanTween.value(gameObject,5f,35f,3f).setEaseInSine().setOnUpdate(x=>
        {
            emitRate = x;
        });
        yield return new WaitForSeconds(1.2f);
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
        IM.i.StartCoroutine(IM.i.PS5InitColor(3));
        IM.i.BlockColourChangeForT(4.5f);
        IM.i.Rumble(4.28f, 4, true, false, 0.1f, 0.75f);
        StartCoroutine(IQuarters(true));
    }
    public void IncrementQuarters(bool increment)
    {
        if (increment)
        {
            quarterInt++;
            if (quarterInt >= quarterSprites.Length)
            {
                quarterInt = 0;
            }
        }
        else
        {
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
        Ember.TriggerPortalEmberBurst();
        yield return new WaitForSeconds(0.5f);
        CameraScript.Flip(DungeonSpawn(), 6f, 2f, reverse: true);
        Shockwave.EEWave(Vector2.zero,12.5f);
        yield return new WaitForSeconds(1.75f);
        IncrementQuarters(increment); 
    }

    public void PortalFR() //called in spin
    {
        Ember.trackGen++;
        CharacterScript.CS.ls.Change(999f,-1);
        ResourceManager.instance.ChangeFuels(999f);
        GS.CS().SetParent(null);
        GS.CS().localScale = new Vector3(1, 1, 1);
        stopBool = true;
        anim.SetBool("Spin", false);
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
                AI.transform.position = DungeonSpawn() + new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0f);
                AI.targetPoint = AI.transform.position;
            }
        }
        CS.transform.position = inDungeon ? DungeonSpawn() : transform.position;
        CS.transform.position = new Vector3(CS.transform.position.x, CS.transform.position.y, -1);
        MechaSuit.m.TPFollowers();
        if (inDungeon)
        {
            MapManager.i.SetMap(true);
            Melee.RefreshDurability();   // refill drill durability on each dive
            // dungeon time RESUMES: thaw the frozen enemies, arm the in-range spawners (0–30s)
            if (MineDungeonManager.i != null) MineDungeonManager.i.OnEnterDungeon();
            if (MineDungeonManager.i == null) DM.i.activeRoom.OnEnter();
            // (mining pockets self-activate when the player tunnels into them)
            foreach (OrbScript t in ResourceManager.instance.heldOrbs)
            {
                t.transform.localScale = Vector3.one;
            }
        }
        else
        {
            MapManager.i.SetMap(false);
            // dungeon time FREEZES: pending spawner plans land instantly, then every enemy is
            // disabled where it stands until the next dive
            if (MineDungeonManager.i != null) MineDungeonManager.i.OnLeaveDungeon();
            // Dungeon run finished -> arm the next wave (absorbed cores are now included). The player
            // summons it with V / the Tele-Phone; teleport stays locked until it's cleared. On death
            // the punishment flag force-starts it immediately.
            SpawnManager.instance.ArmWave();
            if (SpawnManager.instance.forceStartOnReturn)
            {
                SpawnManager.instance.forceStartOnReturn = false;
                SpawnManager.instance.TryStartWave();
            }
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
        charEmitRate = 0f;
        if (emitRampTweenId >= 0) { LeanTween.cancel(emitRampTweenId); emitRampTweenId = -1; }
        emitRate = 0f;
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

    // Where the player/allies land on diving in — the mining entry if present, else the active room.
    Vector3 DungeonSpawn()
        => MineDungeonManager.i != null && MineDungeonManager.i.entryPoint != null
            ? MineDungeonManager.i.entryPoint.position
            : DM.i.activeRoom.safeSpawn.position;

    public void DefeatedBoss()
    {
        SpawnManager.instance.inBossTransition = true; // don't arm a wave on the boss-room return home
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
        SpawnManager.instance.inBossTransition = false; // new era is set up; dungeon-run to arm its first wave
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