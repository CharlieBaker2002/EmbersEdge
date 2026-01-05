using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System;
using System.Linq;
using TMPro;
using static UnityEngine.InputSystem.InputAction;

public class CharacterScript : Unit
{
    #region init
    public List<float> attributes = new List<float> { 0f, 0f, 0f }; //strength intellect agility

    [HideInInspector]
    public Vector2 mousePos;
    public static Vector2 direction;
    public static Vector2 aim;
    public static Quaternion directionQ;
    public static Quaternion aimQ;
    
    private InputAction move;
    private PlayerInput inputs;

    Camera mainCamera;
    [HideInInspector]
    public bool locked = true;
    
    public static CharacterScript CS;
    public List<WeaponScript> weapons;
    public List<MonoBehaviour> scaleables;
    [HideInInspector]
    public WeaponScript weaponScript;
    public int weaponIndex = 0;
    public Collider2D miniMarker;

    private Action<InputAction.CallbackContext> s1 = delegate { };
    private Action<InputAction.CallbackContext> s2 = delegate { };
    private Action<InputAction.CallbackContext> s3 = delegate { };
    public Action<InputAction.CallbackContext> p1 = delegate { };
    public Action<InputAction.CallbackContext> p2 = delegate { };
    public Action<InputAction.CallbackContext> p3 = delegate { };

    private Action<InputAction.CallbackContext>[] starteds;
    private Action<InputAction.CallbackContext>[] performeds;

    private Action<InputAction.CallbackContext> pd1 = delegate { };
    private Action<InputAction.CallbackContext> pd2 = delegate { };
    private Action<InputAction.CallbackContext> pd3 = delegate { };

    public Action<InputAction.CallbackContext>[] pds;
    
    public bool[] boostBools = new bool[]{false,false,false};

    public float[] spellCDs = new float[] { 0f, 0f, 0f };
    public float[] spellmaxCDs = new float[] { 0f, 0f, 0f };
    public int[] manaCosts = new int[] { 0, 0, 0 };
    public bool[] spellBools = new bool[] { false, false, false }; //for checking when performing binding
    public bool[] abilitySlot = new bool[] { false, false, false };
    public Slider[] spellSliders;
    public TextMeshProUGUI[] spellTexts;
    public Image[] abilityImgs;
    public Image[] potionImgs;
    public Slider[] potionSlides;

    public List<string> keys = new List<string>();

    public int groupCurrent;
    public int groupMax;
    public List<AllyAI> group = new();
    public GroupTile groupTile;
    public GameObject groupUIParent;
    public List<GroupTile> groupTiles;
    private Action<InputAction.CallbackContext> closeUI;
    public CoreSlider healthSlider;
    public CoreSlider shieldSlider;

    [HideInInspector]
    public float chipAmount = 0.125f;
    [HideInInspector]
    public float chipDmgCoef = 3f;
    
    public static bool moving = true;
    public LatentShield latentShield;

    public bool quickAim = true;
 
    public SpriteRenderer byebye;
    
    public Slider respawnSlider;
    public TextMeshProUGUI respawnText;
    public static bool dead = false;
    public static bool speedy = false;
    [SerializeField] public Rotator rot;

    public float turnSpeed = 270f;
    public int dashesAvailable = 0;
    public float dashTimer = 0f;
    public float maxDashTimer = 8f;
    
    private bool rotating = true;
    private float fanEngagment = 0f;
    private void Awake()
    {
        CS = this;
        GS.character = gameObject;
        mainCamera = Camera.main;
        pds = new Action<CallbackContext>[3] { pd1, pd2, pd3 };
        direction = Vector2.zero;
        ls.isCharacter = true;
        GS.AS = AS;
    }

    public bool CheckFreeBoostSlot()
    {
        return boostBools.Contains(false);
    }

    protected override void Start()
    {
        base.Start();
        Star();
    }

    void Star()
    {
        StartCoroutine(LateStart());
        inputs = IM.i.pi;
        move = inputs.Player.Movement;
        move.Enable();

        if (RefreshManager.i.DAMAGEPROTECTION)
        {
            ls.onDamageDelegate += ctx => ls.Change(-ctx, 0, false, false);
        }

        inputs.Player.Jump.started += _ =>
        {
            Vector2 mPos = IM.i.MousePosition();
            Vector2 vel = AS.rb.linearVelocity;
            Melee.TryAttack();
            Copter.Play();
        };

        inputs.Player.Jump.performed += _ =>
        {
            if (DashPump.ActivatePumps())
            {
                dashesAvailable--;
            }
            Copter.Release();
        };
        inputs.Player.Jump.Enable();

        inputs.Player.SwapWeapon.performed += _ => SwapWeapons();
        inputs.Player.SwapWeapon.Enable();
        inputs.Player.Shoot.Enable();

        inputs.Player.Spell1.Enable();
        inputs.Player.Spell2.Enable();
        inputs.Player.Spell3.Enable();
        starteds = new Action<InputAction.CallbackContext>[] { s1, s2, s3 };
        performeds = new Action<InputAction.CallbackContext>[] { p1, p2, p3 };

        inputs.Player.Potion1.performed += ctx => pds[0].Invoke(ctx);
        inputs.Player.Potion2.performed += ctx => pds[1].Invoke(ctx);
        inputs.Player.Potion3.performed += ctx => pds[2].Invoke(ctx);
        inputs.Player.Potion1.Enable();
        inputs.Player.Potion2.Enable();
        inputs.Player.Potion3.Enable();

        inputs.Player.Reload.performed += _ =>
        {
            if (Time.timeScale == 0f) return;
            if (weapons.Count == 0) return;
            if (weapons.Count < weaponIndex) return;
            weapons[weaponIndex].GetComponent<WeaponScript>().Reload();
        };
        inputs.Player.Reload.Enable();

        closeUI += _ => CloseGroup();
        ls.onDamageDelegate += _ => ColourControllerRed(_);
        
        healthSlider.InitialiseSlider(10f * GS.Era1());
    }
    #endregion
    
    IEnumerator LateStart()
    {
        yield return null;
        ls.Change(100,-1);
        inputs.Player.Spell1.started += ctx => { if (spellCDs[0] <= 0f && AS.canAct && spellBools[0] == false) { if (ResourceManager.instance.ChangeFuels(-manaCosts[0])) { starteds[0].Invoke(ctx); spellBools[0] = true; } } };
        inputs.Player.Spell1.performed += ctx => { if (spellBools[0] && AS.canAct) { performeds[0].Invoke(ctx); spellBools[0] = false; spellCDs[0] = spellmaxCDs[0]; } else { spellBools[0] = false; } };
        inputs.Player.Spell2.started += ctx => { if (spellCDs[1] <= 0f && AS.canAct && spellBools[1] == false) { if (ResourceManager.instance.ChangeFuels(-manaCosts[1])) { starteds[1].Invoke(ctx); spellBools[1] = true; } } };
        inputs.Player.Spell2.performed += ctx => { if (spellBools[1] && AS.canAct) { performeds[1].Invoke(ctx); spellBools[1] = false; spellCDs[1] = spellmaxCDs[1]; } else { spellBools[1] = false; } };
        inputs.Player.Spell3.started += ctx => { if (spellCDs[2] <= 0f && AS.canAct && spellBools[2] == false) { if (ResourceManager.instance.ChangeFuels(-manaCosts[2])) { starteds[2].Invoke(ctx); spellBools[2] = true; } } };
        inputs.Player.Spell3.performed += ctx => { if (spellBools[2] && AS.canAct) { performeds[2].Invoke(ctx); spellBools[2] = false; spellCDs[2] = spellmaxCDs[2]; } else { spellBools[2] = false; } };
        yield return null;
        yield return null;
        SwapWeapons();

    }

    protected override void Update()
    {
        base.Update();
        
        direction = move.ReadValue<Vector2>();
        if(direction.sqrMagnitude != 0 && !IM.controller)
        { 
            direction = direction.normalized;
            directionQ = GS.VTQ(direction);
        }
        moving = direction != Vector2.zero;
        DoFans();
        float prev = dashTimer;
        dashTimer -= Time.deltaTime;
        if(prev > 0f && dashTimer <= 0f)
        {
           RefreshKinematics();
        }
        if (AS.canAct && !AS.rooted)
        {
            TurnAndAim();
        }
        
        UpdateLineColour(true);
        if (ls.shields.Count == 1)
        {
            shieldStati[0]?.gameObject.SetActive(false);
            shieldStati[1]?.gameObject.SetActive(false);
        }
        else
        {
            if (ls.shields.Any(x => x.isWeak))
            {
                shieldStati[1]?.gameObject.SetActive(true);
                if (ls.shields.Any(x => !x.isWeak))
                {
                    shieldStati[0]?.gameObject.SetActive(false); //could be both types active
                }
            }
            else
            {
                shieldStati[0]?.gameObject.SetActive(true);
            }
        }
        shieldSlider.UpdateSlider(shieldStati[0] == null ? 0f : shieldStati[0].value1 + (shieldStati[1] == null ? 0f : shieldStati[1].value1 ));
        
        rot.omega = Mathf.Lerp(rot.omega,AS.rb.linearVelocity.magnitude * 10f + 10f, 2f * Time.deltaTime);
    }

    public void RefreshKinematics()
    {
        dashesAvailable = DashPump.ReawakenPumps();
        Copter.coptersAvailable = 0;
        foreach (Copter c in Copter.copters)
        {
            if (c.Reawaken())
            {
                Copter.coptersAvailable++;
            }
        }
        Melee.ResetWeapons();
    }

    void DoFans()
    {
        float engagement = 0f;
        if (dashTimer > 0f) engagement += 0.5f;
        if (rotating) engagement += 0.5f;
        fanEngagment = Mathf.Lerp(fanEngagment, engagement, (engagement > 0f ? 10f : 2f) * Time.deltaTime);
        foreach (Fan f in Fan.fans)
        {
            f.engagement = fanEngagment;
        }
    }

    void FixedUpdate()
    {
        mousePos = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        AS.TryAddForce(475f * Time.fixedDeltaTime * AS.mass * DashPump.GetPumpValues(), true);
        
        if (locked)
        {
            DoDirectors();
            DoJetsAndWheels();
            Omnimove.Activate(direction);
            AS.TryAddForce(1750f * Time.fixedDeltaTime * Omnimove.Fire(),true); //activation called from inputaction delegate above

        }
        for (int i = 0; i < 3; i++)
        {
            spellCDs[i] -= Time.deltaTime;
        }
        for (int i = 0; i < 3; i++) 
        {
            if (abilitySlot[i] != false)
            {
                spellSliders[i].value = Mathf.Max(0,spellmaxCDs[i] - spellCDs[i]);
            }
        }
    }

    #region Movement

    void DoJetsAndWheels()
    {
          if (moving)
          {
              float n = 1;
              foreach (Wheel w in Wheel.wheels)
              {
                  n += 1;
              }
              AS.TryAddForce(50f * Time.fixedDeltaTime * n * direction.normalized,true);
              if (speedy)
              {
                  AS.TryAddForce(50f * Time.fixedDeltaTime * 20f * AS.mass * direction.normalized,true);
              }
              n = 0;
              foreach (Jet j in Jet.jets)
              {
                  if (Mathf.Abs(Vector2.SignedAngle(j.baseSR.transform.up, direction)) <= j.angleLimit)
                  {
                      j.acco = true;
                      n += j.timer * j.strength;
                  }
                  else
                  {
                      j.acco = false;
                  }
              }
              AS.TryAddForce(80f * Time.fixedDeltaTime * n * direction,true);

          }
          else { Jet.jets.ForEach(j => j.acco = false); }
    }

    void DoDirectors()
    {
        if (moving)
        {
            foreach(Accelerator a in Accelerator.accels)
            {
                if (Mathf.Abs(Vector2.SignedAngle(a.direction, direction.Rotated(-transform.rotation.eulerAngles.z))) <= 65f)
                {
                    a.on = true;
                    AS.TryAddForce(direction * (a.force * Time.fixedDeltaTime), true);
                }
                else
                {
                    a.on = false;
                }
            }
        }
        else
        {
            foreach (Accelerator a in Accelerator.accels)
            {
                Debug.Log(a.gameObject,a.gameObject);
                a.on = false;
            }
        }
    }
    private void TurnAndAim()
    {
        if (IM.controller)
        {
            aim = IM.i.pi.Player.Aim.ReadValue<Vector2>();
            if (aim.sqrMagnitude > 0f)
            {
                aim = aim.normalized;
                aimQ = GS.VTQ(aim);
            }
            if (IM.i.controllerCursor.gameObject.activeInHierarchy)  return;
        }
        else
        {
           aim = new Vector2(mousePos.x - transform.position.x, mousePos.y - transform.position.y).normalized;
         
           aimQ = GS.VTQ(aim);
        }

        Quaternion prev = transform.rotation;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, aimQ, turnSpeed * Time.deltaTime);
        rotating = Quaternion.Angle(prev, transform.rotation) > Time.deltaTime * turnSpeed * 0.5f;
    }
    
    #endregion

    #region WSRP
    
    public void SwapWeapons()
    {
        if (weapons.Count > weaponIndex)
        {
            weapons[weaponIndex].enabled = false;
        }
        weaponIndex += 1;
        if (weaponIndex >= weapons.Count)
        {
            weaponIndex = 0;
        }
        if (weapons.Count > 0)
        {
            weaponScript = weapons[weaponIndex];
            weapons[weaponIndex].enabled = true;
            weapons[weaponIndex].transform.localRotation = Quaternion.identity;
            AmmoSlider.i.InitWeapon(weapons[weaponIndex].GetComponent<WeaponScript>());
            AmmoSlider.i.WeaponSliderResetT(weapons[weaponIndex].GetComponent<WeaponScript>().attackTimer);
            if (Weapon().ammoInClip == 0) Weapon().Reload();
            MechaSuit.m.RotatePowered(Weapon().transform);
        }
    }

    public int NewBoost(Boost p)
    {
        int ind = Array.IndexOf(boostBools, false);
        if (ind == -1)
        {
            return -1;
        }
        MechaSuit.boostsLeft--;
        pds[ind] += p.Consume;
        boostBools[ind] = true;
        p.sr.color = Color.white;
        potionImgs[ind].sprite = p.s;
        potionImgs[ind].color = Color.white;
        potionImgs[ind].preserveAspect = true;
        potionImgs[ind].GetComponentInParent<Slider>().value = 1;
        potionImgs[ind].gameObject.SetActive(true);
        return ind;
    }

    public int NewAbility(Spell ability0, Spell ability1, Spell ability2, Sprite s)
    {
        int n = Array.IndexOf(abilitySlot, false);
        if(n == -1)
        {
            throw new Exception("No availale ability slot");
        }
        abilitySlot[n] = true;
        Vector2[] manaCd = new Vector2[] { -Vector2.one, -Vector2.one, -Vector2.one };
        var abilities = new Spell[] { ability0, ability1, ability2 };
        for(int i = 0; i < 3; i++)
        {
            if (abilities[i] == null)
            {
                break;
            }
            var ability = abilities[i];
            manaCd[i] = ability.GetManaAndCd();
            starteds[n] += ability.Started;
            performeds[n] += ability.Performed;
        }
        UpdateSpellValues(manaCd, n);
        abilityImgs[n].sprite = s;
        abilityImgs[n].preserveAspect = true;
        abilityImgs[n].color = Color.white;
        abilityImgs[n].gameObject.SetActive(true);
        return n;
    }

    public void RemoveAbility(int ind)
    {
        starteds[ind] = delegate { };
        performeds[ind] = delegate { };
        abilitySlot[ind] = false;

        abilityImgs[ind].sprite = null;
        abilityImgs[ind].color = Color.clear;
        spellSliders[ind].value = 0;
        abilityImgs[ind].gameObject.SetActive(false);
    }

    public void ResetAbilityCosts()
    {
        for(int i = 0; i < 3; i++)
        {
            manaCosts[i] = 0;
        }
    }

    void UpdateSpellValues(Vector2[] manaCD, int ind)
    {
        manaCosts[ind] = 0;
        spellmaxCDs[ind] = 0f;
        int a = 0;
        for (int i = 3; i > 0; i--)
        {
            if (manaCD[3 - i] == -Vector2.one)
            {
                break;
            }
            a += i;
            spellmaxCDs[ind] += manaCD[3 - i].y * i;
            manaCosts[ind] += (int)manaCD[3 - i].x;
        }
        spellmaxCDs[ind] /= a;
        spellCDs[ind] = 0;
        spellSliders[ind].maxValue = spellmaxCDs[ind];
        spellTexts[ind].text = manaCosts[ind].ToString();
    }

    #endregion 

    public WeaponScript Weapon()
    {
        return weapons[weaponIndex].GetComponent<WeaponScript>();
    }

    public void AltGroupUI()
    {
        if (groupUIParent.activeInHierarchy)
        {
            CloseGroup();
        }
        else
        {
            OpenGroup();
        }
    }

    public void NewTile(AllyAI ai)
    {
        GroupTile t = Instantiate(groupTile, groupUIParent.transform);
        t.Init(ai.name, ai.spr, ai.groupCost, ai.ls);
        groupTiles.Add(t);
        RefreshGroupUI();
    }

    public void DeleteTile(LifeScript ls)
    {
        GroupTile tile = null;
        foreach (GroupTile t in groupTiles)
        {
            if (t.ls == ls)
            {
                tile = t;
            }
        }
        groupTiles.Remove(tile);
        Destroy(tile.gameObject);
        if (groupUIParent.activeInHierarchy)
        {
            RefreshGroupUI();
        }
    }

    public void RefreshGroupUI()
    {
        for (int i = 0; i < groupTiles.Count; i++)
        {
            groupTiles[i].transform.position = BM.i.UIspots[i].position;
        }
        if (!groupUIParent.activeInHierarchy)
        {
            UIManager.CloseAllUIs();
            OpenGroup();
        }
        //if (groupUIParent.activeInHierarchy)
        //{
        //    List<Transform> ts = new List<Transform>();
        //    foreach (Transform t in groupUIParent.transform)
        //    {
        //        ts.Add(t);
        //    }
        //    while(ts.Count > 0)
        //    {
        //        Destroy(ts[0].gameObject);
        //        ts.RemoveAt(0);
        //    }
        //    for (int i = 0; i < group.Count; i++)
        //    {
        //        GroupTile t = Instantiate(groupTile, BM.i.UIspots[i].position, Quaternion.identity, groupUIParent.transform);
        //        t.Init(group[i].name, group[i].spr, group[i].groupCost, group[i].ls);
        //    }
        //}
        //else
        //{
        //    UIManager.CloseAllUIs();
        //    OpenGroup();
        //}
    }

    private void OpenGroup()
    {
        IM.i.pi.Player.Escape.performed += closeUI;
        groupUIParent.SetActive(true);
    }

    private void CloseGroup()
    {
        IM.i.pi.Player.Escape.performed -= closeUI;
        groupUIParent.SetActive(false);
    }

    public void UpdateHealth(float hp)
    {
        healthSlider.UpdateSlider(hp);
    }

    public void ColourControllerRed(float dmg, bool time = false)
    {
        if(dmg > 0) { return; };
        dmg = Mathf.Abs(dmg);
        if (!time)
        {
            IM.i.SetColour(IM.i.dmgGradient, 0.4f, Mathf.Min(Mathf.CeilToInt(dmg * 0.25f), 5));
        }
        else
        {
            IM.i.SetColour(IM.i.dmgGradient, 0.4f, Mathf.CeilToInt(dmg * 2.5f));
        }
    }

    public void ColourControllerGreen(float heal, bool time = false)
    {
        heal = Mathf.Abs(heal);
        if (!time)
        {
            IM.i.SetColour(IM.i.healGradient, 0.75f, Mathf.Min(Mathf.CeilToInt(heal * 0.25f), 5));
        }
        else
        {
            IM.i.SetColour(IM.i.healGradient, 0.75f, Mathf.CeilToInt(heal * 2.5f));
        }
    }

    public void ColourControllerDeath()
    {
        IM.i.SetColour(IM.i.dieGradients[GS.era], 0.6f, 5);
        IM.i.BlockColourChangeForT(3.1f);
    }

    //Destroys Current Mechasuit Too.
    public static void ChangeToLastLife()
    {
        PortalScript.i.StartCoroutine(BlueprintManager.i.DestroyLoot());
        GS.RemAllStats(CS);
        GS.Stat(CS, "invulnerable", 3f);
        GS.Stat(CS, "dodging", 3f);
        
        MechaSuit.MakeSad();
        MechaSuit.m.RemoveNonStubborn();
        MechaSuit.Announce();
        
        CS.SwapWeapons();
        CameraScript.i.StartShaking();
        CS.ls.hp = 0.01f;
    }
    
    public IEnumerator DieDie()
    {
        if (RefreshManager.i.ARENAMODE)
        {
            PortalScript.i.Lose();
            yield break;
        }
        dead = true;
        GS.AS.rooted = true;
        GS.AS.interactive = false;
        AS.Stop();
        UIManager.CloseAllUIs();
        PortalScript.i.NoPortal();
        MechaSuit.m.DieFR();
        yield return new WaitForSeconds(3f);
        CameraScript.i.StartTemporaryZoomRegular(10f, 20f + 5f*GS.era, 0.015f, 0.005f);
        SpawnManager.instance.AccelerateWave(true);

        if (PortalScript.i.inDungeon)
        {
            this.QA(() => MechaSuit.Announce(),2f);
            CS.ColourControllerDeath();
            PortalScript.i.Portal();
            DM.i.activeRoom = DM.i.initR[GS.era];
            PortalScript.i.dungeonCamera.position = new Vector3(DM.i.activeRoom.transform.position.x, DM.i.activeRoom.transform.position.y, PortalScript.i.dungeonCamera.position.z);
        }
        else
        {
            MechaSuit.Announce();
            GS.CS().position = Vector3.zero;
        }
        
        GS.AS.Stop();
        int day = SpawnManager.day;
        yield return new WaitForSeconds(1.5f);
        UIManager.noInCanvas = true;
        UIManager.i.FadeOutCanvas();
        MapManager.i.Shrink();
        respawnSlider.fillRect.GetComponent<Image>().color = Color.clear;
        LeanTween.LeanImgCol(respawnSlider.fillRect.GetComponent<Image>(), new Color(0f,0f,0,0.25f), 2f).setEaseOutCirc();
        respawnText.color = Color.clear;
        LeanTween.LeanTMPColor(respawnText, new Color(1f,1f,1f,1f), 2f).setEaseOutCirc();
        respawnSlider.maxValue = 25f + 5f * GS.era;
        for (float t = 0f; t < respawnSlider.maxValue; t += Time.deltaTime)
        {
            yield return null;
            RespawnUIUpdate(respawnSlider.maxValue - t);
            if (day != SpawnManager.day)
            {
                break;
            }
        }
        UIManager.noInCanvas = false;
        UIManager.i.FadeInCanvas();
        CameraScript.i.StopAllCoroutines();
        yield return null;
        dead = false;
        MechaSuit.m.Restart();
        respawnSlider.value = 0;
        GS.AS.rooted = false;
        GS.AS.interactive = true;
        respawnText.text = "";
        MechaSuit.MakeHappy();
        CameraScript.ZoomPermanent(CameraScript.i.correctScale,0.01f);
    }

    protected override void PlaceStats()
    {
        var accos = stati.Where(x => x.gameObject.activeInHierarchy).ToList();
        Vector3 b = transform.position + iconPlacer;
        for (int i = 0; i < accos.Count(); i++)
        {
            accos[i].transform.position = b + Vector3.right * (i * 0.4f - (accos.Count - 1f) * 0.2f);
        }
    }

    void RespawnUIUpdate(float t)
    {
        respawnSlider.value = t;
        respawnText.text = t.ToString("F0");
    }
}