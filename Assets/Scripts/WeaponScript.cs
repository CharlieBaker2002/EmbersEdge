using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponScript : Part
{
    [HideInInspector]
    public CharacterScript CS;
    public float[] restockCost = new float[4]; //dont edit, this is 20% bp cost.
    public GameObject[] projectilesPrefab = new GameObject[3];
    public GameObject[] projectiles = new GameObject[3]; //up to 3 types
    [Header("zero for norm click, -1 for repeat hold, >0 for release shot")]
    public float option;
    private bool updateChecker = false;
    [Serializable]
    public struct SpecialAttack
    {
        public bool active;
        public bool repeatChances;
        public SpecialAttack(bool ap, bool rCp)
        {
            active = ap;
            repeatChances = rCp;
        }
    }
    public SpecialAttack sp = new SpecialAttack(false, false);
    [Header("n | regSpr | fail | msBtw | randSpr | msStart | x | y | strength | randStrength")]
    public int[] p1modifier = new int[10];
    public int[] p2modifier = new int[10];
    public int[] p3modifier = new int[10];
    [Header("Critical hit on projectiles 1 & Sequential attacks if not -1 (AttackReset -> msStart, msStart -> AttackReset)")]
    public int sequentialInt = -1; //if -1 ranged, else melee: rounds shot individually, rest time between shots equal to mStart
    public int ammoPerClip = 4;
    [HideInInspector]
    public int ammoInClip = 0;
    public int maximumAmmo = 100;
    public int totalAmmo = 0;
    public float maxReload = 2.5f;
    [HideInInspector]
    public float reloadTimer = 0f;
    public float attackReset = 0.5f;
    [HideInInspector]
    public float attackTimer = 0f;
    [HideInInspector]
    public float strength = 0;
    private InputAction shoot;
    private ActionScript AS;
    private GameObject cast;
    private ParticleSystem ps;
    private ParticleSystem.MainModule castfx;
    private Action<InputAction.CallbackContext>[] dels = new Action<InputAction.CallbackContext>[] {null, null};
    Coroutine colCo;
    private bool leveled = false;
    public float knockback;

    private float shootHeight;
    
    int shootID;
    
    private bool hasStarted = false;
    
    private Coroutine bounceCoroutine;
    private float currentBounceAngle = 0f;
    private float springDamping = 0.8f; // Damping factor to reduce bounce accumulation
    private float springSpeed = 5f;    // Base spring return speed
    private float maxBounce = 45f;    // Maximum allowable bounce angle
    
    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        cd.offset = new Vector2(0f, sr.sprite.rect.height * 0.5f * 0.015625f);
        mecha.weapons.Add(this);
        enabled = true;
        if (level >= 1 && !leveled)
        {
            GetComponent<WeaponLeveler>().LevelUp(level - 1);
            leveled = true;
        }
        CharacterScript.CS.weapons.Add(this);
        mecha.weapons.Add(this);
        CharacterScript.CS.SwapWeapons();
        shootHeight = sr.sprite.rect.height * 0.015625f;
    }
    
    private void Awake()
    {
        ammoInClip = ammoPerClip;
        totalAmmo = maximumAmmo - ammoPerClip;
    }
    
    private void Start()
    {
        for (int i = 0; i < 3; i++)
        {
            if (projectilesPrefab[i] != null && projectiles[i] == null)
            {
                projectiles[i] = Instantiate(projectilesPrefab[i], transform.position, transform.rotation, transform);
                projectiles[i].SetActive(false);
            }
        }
        CS = transform.parent.GetComponent<CharacterScript>();
        hasStarted = true;
        cast = Instantiate(SpawnManager.instance.castFX, transform.position, transform.rotation, transform);
        cast.SetActive(true);
        ps = cast.GetComponent<ParticleSystem>();
        castfx = ps.main;
        AS = CharacterScript.CS.AS;
        OnEnable();
    }
    
    void OnEnable()
    {
        //LeanTween.scale(gameObject,Vector 3.one,0.5f).setEaseOutBack();
        if (cd != null)
        {
            cd.offset = new Vector2(0f, sr.sprite.rect.height * 0.5f * 0.015625f);
            cd.paused = false;
        }
        transform.localPosition = transform.localPosition.normalized * 0.1f;
        engagement = 1f;
        if (hasStarted)
        {
            shoot = IM.i.pi.Player.Shoot;
            if (option != -1f)
            {
                dels[0] = _ => TryShoot();
                shoot.started += dels[0];
                if (option > 0f)
                {
                    dels[1] = ctx => Up(ctx.duration);
                    shoot.canceled += dels[1];
                }
            }
            else
            {
                dels[0] = ctx => SetUpdateCheckerTrue();
                dels[1] = ctx => { updateChecker = false; IM.i.BlockVB(shootID); };
                shoot.started += dels[0];
                shoot.canceled += dels[1];
            }
        }
    }
    
    private void SetUpdateCheckerTrue()
    {
        if(reloadTimer <= 0.15f)
        {
            updateChecker = true;
            shootID = IM.i.Rumble(ammoPerClip * attackReset, 0, false, true, 0f, 0.5f);
        }
        
    }

    void OnDisable()
    {
        if (cd != null)
        {
            cd.offset = new Vector2(0f, sr.sprite.rect.height * 0.25f * 0.015625f);
            cd.paused = true;
        }
        updateChecker = false;
        engagement = 0f;
        transform.localRotation = Quaternion.identity;
        transform.localPosition = transform.localPosition.normalized * (MechaSuit.poweredDist - sr.sprite.rect.height * 0.25f * 0.015625f);
        //LeanTween.cancel(gameObject);
        //transform.localScale = Vector3.one *0.5f;
        if (!hasStarted) return;
        if (option != -1f)
        {
            shoot.started -= dels[0];
            if (option > 0f)
            {
                shoot.canceled -= dels[1];
            }
        }
        else
        {
            shoot.started -= dels[0];
            shoot.canceled -= dels[1];
        }
    }

    private IEnumerator CastColour(float t)
    {
        yield return new WaitForSeconds(t);
        castfx.startColor = UIManager.i.colSO.Level2;
        shootID = IM.i.Rumble(10, 0, false, true, 0f, 0.5f);
    }

    void Up(double duration)
    {
        if (duration >= option && castfx.loop)
        {
            CallShoot();
        }
        if (colCo!=null)
        {
            StopCoroutine(colCo);
        }
        if(shootID != -1)
        {
            IM.i.BlockVB(shootID);
        }
        castfx.loop = false;
    }

    public override void StopPart(MechaSuit m)
    {
        OnDisable();
        if (CharacterScript.CS.weaponIndex == CharacterScript.CS.weapons.IndexOf(this))
        {
            CharacterScript.CS.SwapWeapons();
        }
        CharacterScript.CS.weapons.Remove(this);
        enabled = false;
    }

    void Update()
    {
        attackTimer -= Time.deltaTime;
        if (reloadTimer > 0f)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f)
            {
                AmmoSlider.i.UpdateSlider(ammoInClip); //set to full OR total ammo if out of "clips"
                IM.i.Rumble(0.05f, 0, false, true, 1, 0.1f, 1f);
                IM.i.Rumble(0.05f, 0, false, true, 1, 0.1f, 1f, 0.15f);
            }
            else
            {
                if(totalAmmo == 0)
                {
                    AmmoSlider.i.UpdateSlider(Mathf.Min(ammoInClip,Mathf.Floor(AmmoSlider.i.max * (maxReload - reloadTimer) / maxReload)));
                }
                else
                {
                    AmmoSlider.i.UpdateSlider(Mathf.Floor(AmmoSlider.i.max * (maxReload - reloadTimer) / maxReload));
                }
                
            }
        }
        if(attackTimer > 0f)
        {
            AmmoSlider.i.WeaponSliderResetT(attackTimer);
        }
        if(updateChecker)
        {
            TryShoot();
        }
        if (CharacterScript.CS.quickAim)
        {
            transform.rotation = GS.VTQ(IM.i.MousePosition(transform.position, true));
        }
    }


    void TryShoot()
    {
        if (!GS.CanAct()) return;
        if (ammoInClip > 0f)
        {
            if (reloadTimer <= 0f & attackTimer <= 0f)
            {
                if (option > 0f)
                {
                    castfx.startColor = UIManager.i.colSO.Level1;
                    castfx.loop = true;
                    ps.Play();
                    colCo = StartCoroutine(CastColour(option));
                    shootID = IM.i.Rumble(option, 2, false, true, 0.05f, 0, 0.5f);
                }
                else
                {
                    CallShoot();
                }
            }
        }
    }
    
    public void Reload()
    {
        if(reloadTimer <= 0f && totalAmmo > 0 && ammoInClip < ammoPerClip)
        {
            var buffer = Mathf.Min(ammoPerClip - ammoInClip, totalAmmo);
            reloadTimer = maxReload * buffer / ammoPerClip;
            cd.SetValue(reloadTimer);
            if (option == -1)
            {
                updateChecker = false;
            }

            if (!RefreshManager.i.ARENAMODE)
            {
                totalAmmo -= buffer;
            }
           
            ammoInClip += buffer;
        }
        AmmoSlider.i.WeaponSliderResetClips(totalAmmo);
    }
    
    void CallShoot()
    {
        // Apply knockback force
        AS.TryAddForce(-transform.up * knockback, true);

        // Start or restart the spring effect
        float bounceAmount = Mathf.Clamp(knockback * 5f, 10f, 45f); // Bounce angle based on knockback
        if (bounceCoroutine != null)
        {
            StopCoroutine(bounceCoroutine); // Stop the existing coroutine if running

            // Adjust damping and spring speed for rapid presses
            springDamping = Mathf.Clamp(springDamping * 0.9f, 0.5f, 1f); // Reduce damping with each rapid press
            springSpeed = Mathf.Clamp(springSpeed * 1.1f, 5f, 15f);      // Increase spring speed for faster return
        }
        else
        {
            // Reset to default spring behavior if there's a pause
            springDamping = 0.8f;
            springSpeed = 5f;
        }

        bounceCoroutine = StartCoroutine(WeaponBounce(bounceAmount));

        // Rumble feedback
        if (option == -1)
        {
            IM.i.Rumble(attackReset / 3, 0, true, true, 0.5f, 0.5f);
        }
        else if (option == 0)
        {
            IM.i.Rumble(Mathf.Max(attackReset / 4, 0.05f), 2, true, true, 0.1f, 0.1f, 0.1f);
        }
        else
        {
            IM.i.Rumble(0.1f, 2, true, true, 0.1f, 0.1f, 0.1f);
        }

        // Update ammo and attack timer
        ammoInClip--;
        AmmoSlider.i.UpdateSlider(ammoInClip);
        if (ammoInClip == 0)
        {
            Reload();
        }
        attackTimer = attackReset;
        cd.SetValue(attackReset);
        // Handle projectiles
        int startPoint = 0;
        if (sequentialInt != -1)
        {
            startPoint = sequentialInt;
            sequentialInt += 1;
            if (sequentialInt >= projectiles.Length)
            {
                sequentialInt = 0;
            }
        }
        for (int t = startPoint; t < projectiles.Length; t++)
        {
            GameObject pType = projectiles[t];
            if (pType == null)
            {
                continue;
            }
            int[] modifier;
            switch (t)
            {
                case 0:
                    modifier = p1modifier;
                    break;
                case 1:
                    modifier = p2modifier;
                    break;
                case 2:
                    modifier = p3modifier;
                    break;
                default:
                    modifier = p1modifier;
                    Debug.Log("makeProjectiles weapon script issue with projectile indexing");
                    break;
            }

            if (sp.active && t == 0)
            {
                if (UnityEngine.Random.Range(0, 100) > modifier[2])
                {
                    StartCoroutine(Shoot(modifier, pType, sp.repeatChances));
                    if (sequentialInt != -1)
                    {
                        sequentialInt = 0;
                        attackTimer = modifier[5] * 0.001f;
                    }
                    return;
                }
                else
                {
                    continue;
                }
            }
            StartCoroutine(Shoot(modifier, pType));
            if (sequentialInt != -1)
            {
                attackTimer = modifier[5] * 0.001f;
                return;
            }
        }
    }
    
    IEnumerator Shoot(int[] modifier, GameObject pType, bool repeatChances = true){
        float xpos = -0.1f*modifier[6]/2;
        float ypos = -0.1f*modifier[7]/2 + shootHeight;
        float xIncrement = (modifier[6]== 0) ? 0 : 0.1f*modifier[6] / (modifier[0] - 1);
        float yIncrement = (modifier[7] == 0) ? 0 : 0.1f*modifier[7] / (modifier[0] - 1);
        float timer = modifier[5] * 0.001f;
        if (sequentialInt != -1)
        {
            timer = attackReset;
        }
        while (timer > 0f) //pause for initial wait
        {
            timer -= Time.deltaTime;
            yield return null;
        }
        for (int i = 0; i<modifier[0]; i++){ //foreach bullet of this type / shot
            int x = UnityEngine.Random.Range(0, 100);
            if(x <= modifier[2] && repeatChances)
            { //if its not an unlucky bullet
                xpos += xIncrement;
                ypos += yIncrement;
                continue;
            }
            float rot;
            if(modifier[0] > 1)
            {
                rot = UnityEngine.Random.Range(-modifier[4], modifier[4]) - 0.5f * modifier[1] + i * modifier[1] / (modifier[0] - 1) + transform.rotation.eulerAngles.z;
            }
            else
            {
                rot = UnityEngine.Random.Range(-modifier[4], modifier[4]) + transform.rotation.eulerAngles.z;
            }
            float ang = Mathf.Deg2Rad * transform.rotation.eulerAngles.z;
            GameObject bul = Instantiate(pType, transform.position + new Vector3(Mathf.Cos(ang)*xpos - Mathf.Sin(ang)*ypos, Mathf.Sin(ang)*xpos + Mathf.Cos(ang)*ypos), Quaternion.Euler(0, 0, rot), GS.FindParent(GS.Parent.allyprojectiles));
            ProjectileScript ps = bul.GetComponent<ProjectileScript>();
            xpos += xIncrement;
            ypos += yIncrement;
            bul.SetActive(true);
            ps.SetValues(new Vector2(-Mathf.Cos((rot - 90) * 2 * Mathf.PI / 360), Mathf.Cos(rot * 2 * Mathf.PI / 360)), "Allies",strength+(1 + strength)*(modifier[8] + UnityEngine.Random.Range(0,modifier[9]+1)),GS.CS());
            if(modifier[3] > 0)
            {
                timer = modifier[3] * 0.001f;
                while (timer > 0f)
                {
                    timer -= Time.deltaTime;
                    yield return null; //pause for inbetween shots
                }
            }
        }
    }
    
    private IEnumerator WeaponBounce(float bounceAmount)
    {
        // Calculate the new target angle with damping
        float adjustedBounceAmount = bounceAmount * Mathf.Clamp(springDamping, 0.5f, 1f);
        float targetAngle = Mathf.Clamp(currentBounceAngle - adjustedBounceAmount, -maxBounce, maxBounce);
        float initialAngle = currentBounceAngle;
        currentBounceAngle = targetAngle; // Update the cumulative angle

        // Spring back to the target angle
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * springSpeed * 3f;
            float newAngle = -Mathf.Lerp(initialAngle, targetAngle, t);
            transform.localRotation = Quaternion.Euler(newAngle, transform.localRotation.eulerAngles.y,transform.localRotation.eulerAngles.z );
            yield return null;
        }

        // Quickly return to the original position
        float returnSpeed = springSpeed * (1 + (1 - springDamping)); // Faster return if damping is high
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * returnSpeed * 0.5f;
            float newAngle = -Mathf.Lerp(targetAngle, 0f, t);
            transform.localRotation = Quaternion.Euler(newAngle, transform.localRotation.eulerAngles.y, transform.localRotation.eulerAngles.z);
            currentBounceAngle = Mathf.Lerp(targetAngle, 0f, t); // Update the cumulative angle gradually
            yield return null;
        }

        // Reset the angle to zero completely
        currentBounceAngle = 0f;
        springDamping = 0.8f;
        springSpeed = 5f;
        bounceCoroutine = null;
    }
    
    public override bool CanAddThisPart()
    {
        return CharacterScript.CS.weapons.Count < 1 + MechaSuit.level;
    }

}
