using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;

public class LifeScript : MonoBehaviour
{
    [Tooltip("White, Green, Blue, Red")]
    public int race = 0;
    public List<MonoBehaviour> onDeaths;

    [SerializeField] public List<Shield> shields = new List<Shield>();

    public float maxHp = 1f;
    public float hp = 1f;
    public float[] orbs = new float[4];
    public GameObject blood;
    [SerializeField] GameObject shieldBlood;

    private bool isProjectile = false;
    private bool hasBlood;
    [HideInInspector] public GameObject bloodHold;
    private float bloodTimer = 0f;
    [HideInInspector] public bool isCharacter = false;
    Transform FXtransform;
    [HideInInspector] public bool hasDied = false;
    public bool invulnerable = false;
    public bool animateBeforeDie = false;
    public Transform orbSpawnPlace = null;
    public System.Action<float> onDamageDelegate;
    public List<SpriteRenderer> dmgsrs = new List<SpriteRenderer>();
    public int[] thicknesses = {1};
    private bool isAlly;
    public static readonly int Thickness = Shader.PropertyToID("_Thickness");
    private static readonly Color[] dungeonHealthyCols = {new(1.5f,1.08f,3.6f), new(2.75f,2.75f,2.1f), new(3f,2f,1.6f)};
    private static readonly Color allyHealthyCol = new (0.6f,1f,0.8f);
    private static readonly Color hurtCol = new (3f, 0.14f, 0.32f);
    public static readonly int Outline = Shader.PropertyToID("_Outline");
    public Color overrideCol = Color.clear;

    // A simple counter for unique shield IDs:
    private static int shieldIDCounter = 0;

    private void Awake()
    {
        if (blood != null)
        {
            hasBlood = true;
            bloodHold = blood;
        }
        isAlly = CompareTag("Allies");
        if (GetComponent<ProjectileScript>() != null)
        {
            isProjectile = true;
        }
    }

    void Start()
    {
        FXtransform = GS.FindParent(GS.Parent.fx);
        if (hp == 1)
        {
            hp = maxHp;
        }
        LimitCheck();
    }
    
    void Update()
    {
        bloodTimer -= Time.deltaTime;
    }
    
    public void LimitCheck()
    {
        if (hp <= 0f)
        {
            OnDie();
        }
        if (hp > maxHp)
        {
            hp = maxHp;
        }
        if (isCharacter)
        {
            if (hp < 0)
            {
                hp = 0;
            }
            CharacterScript.CS.UpdateHealth(hp);
        }
        else if (!isProjectile)
        {
            if (overrideCol == Color.clear)
            {
                UpdateLineColour();
            }
        }
    }

    public void UpdateLineColour()
    {
        float amount = hp / maxHp;
        if (isAlly)
        {
            for(int i = 0; i < dmgsrs.Count; i++)
            {
                dmgsrs[i].material.SetFloat(Thickness, thicknesses[i]);
                dmgsrs[i].material.SetColor(Outline,Color.Lerp(hurtCol, allyHealthyCol, amount));
            }
        }
        else
        {
            for(int i = 0; i < dmgsrs.Count; i++)
            {
                dmgsrs[i].material.SetFloat(Thickness, thicknesses[i] * (1 + (1 - amount)) * 0.5f);
                dmgsrs[i].material.SetColor(Outline,Color.Lerp(hurtCol,dungeonHealthyCols[GS.era], amount));
            }
        }
    }
    
      public void Change(float value, int dmgType, bool forceFX = true, bool callDelegate = true, bool noKill = false, bool ignoreDodging = false, bool XShowText = false)
    {
        if (!enabled || hasDied)
        {
            return;
        }
        if (value < 0)
        {
            // float crit = 0f;
            // switch (dmgType)
            // {
            //     case 0:
            //         if (race == 3)
            //         {
            //             crit = -0.25f;
            //         }
            //         break;
            //     case 1:
            //         if (race == 2)
            //         {
            //             crit = 0.334f;
            //         }
            //         else if (race == 1)
            //         {
            //             crit = -0.333f;
            //         }
            //         break;
            //     case 2:
            //         if (race == 3)
            //         {
            //             crit = 0.334f;
            //         }
            //         else if (race == 2)
            //         {
            //             crit = -0.333f;
            //         }
            //         break;
            //     case 3:
            //         if (race == 0)
            //         {
            //             crit = 0.334f;
            //         }
            //         else if (race == 3)
            //         {
            //             crit = -0.333f;
            //         }
            //         break;
            //     default:
            //         crit = 0f;
            //         break;
            // }
            // value *= 1 + crit;
            if (invulnerable && !ignoreDodging)
            {
                return;
            }
            float dmgBuffer = value;

            value = RemoveFromShields(dmgBuffer);
            hp += value;
           
            if (noKill)
            {
                if(hp < 0)
                {
                    hp = 0.01f;
                }
            }
            if (callDelegate)
            {
                onDamageDelegate?.Invoke(value);
            }
            if ((value <= 0f && bloodTimer < 0f) || forceFX || XShowText)
            {
                if (!isProjectile && hasBlood && bloodTimer < 0f)
                {
                    bloodTimer = 0.15f;
                    if (value == 0f && shieldBlood != null)
                    {
                        Instantiate(shieldBlood,transform.position,transform.rotation,FXtransform);
                    }
                    else
                    {
                        Instantiate(blood, transform.position, transform.rotation, FXtransform);
                    }
                  
                }
                if (!isProjectile)
                {
                    if (XShowText)
                    {
                        if (value == 0)
                        {
                            UIManager.i.DamageText(dmgBuffer * 20f, -2, transform.position); //overTimeDmg
                        }
                        else
                        {
                            if (isAlly)
                            {
                                UIManager.i.DamageText(dmgBuffer * 20f, -1, transform.position); //overTimeDmg
                            }
                            else
                            {
                                UIManager.i.DamageText(dmgBuffer * 20f, dmgType, transform.position); //overTimeDmg
                            }
                        }
                      
                      
                    }
                    else if(forceFX)
                    {
                        if (value == 0)
                        {
                            UIManager.i.DamageText(dmgBuffer, -2, transform.position); //reg dmg
                        }
                        else
                        {
                            if (isAlly)
                            {
                                UIManager.i.DamageText(dmgBuffer, -1, transform.position); //reg dmg
                            }
                            else
                            {
                                UIManager.i.DamageText(dmgBuffer, dmgType, transform.position); //reg dmg
                            }
                        }
                      
                    }
                }
            }
        }
        else
        {
            hp += value;
            if (callDelegate)
            {
                onDamageDelegate?.Invoke(value);
                if (isCharacter)
                {
                    CharacterScript.CS.ColourControllerGreen(value);
                }
            }
        }
        LimitCheck();
    }

    /// <summary>
    /// Subtracts as much damage as possible from available shields, from oldest to newest
    /// </summary>
    private float RemoveFromShields(float dmg)
    {
        float remainingDmg = dmg;
        for (int i = 0; i < shields.Count; i++)
        {
            float str = shields[i].strength;
            if (str > 0f)
            {
                // how much we can absorb from this shield
                float absorb = Mathf.Min(str, -remainingDmg); // 'str' vs the positive magnitude
                shields[i] = new Shield(shields[i], -absorb); 
                remainingDmg += absorb; 
                if (remainingDmg == 0f) break;
            }
        }
        return remainingDmg;
    }

    public void ChangeOverTime(float value, float time, int dmgType, bool noKill = true, Status s = null)
    {
        StartCoroutine(ChangeOverTimeIE(value, time, dmgType, noKill, s));
    }

    private IEnumerator ChangeOverTimeIE(float value, float time, int dmgType, bool noKill, Status s)
    {
        bool concernedAboutStat = (s != null);
        float timer = time;
        while (timer > Time.fixedDeltaTime)
        {
            if (concernedAboutStat && s.dissapearing) 
                yield break;
            
            timer -= Time.fixedDeltaTime;
            if (RefreshManager.twentyFixFrame)
            {
                Change(value * (Time.fixedDeltaTime / time), dmgType, true, false, noKill, true, true);
            }
            else
            {
                Change(value * (Time.fixedDeltaTime / time), dmgType, false, false, noKill, true);
            }
            yield return new WaitForFixedUpdate();
        }
        if (time != 0)
        {
            Change(value * timer / time, dmgType, false, false, noKill, true);
        }
    }

    public void OnDie(bool makeOrbs = true)
    {
        if (hasDied) return;
        hasDied = true;
        if (hasBlood)
        {
            Instantiate(blood, transform.position, transform.rotation, FXtransform);
        }
        if (makeOrbs && orbs != null)
        {
            GS.CallSpawnOrbs(transform.position, orbs, orbSpawnPlace, !transform.InDungeon());
        }
        if (isCharacter)
        {
            if (MechaSuit.lastlife)
            {
                CharacterScript.CS.StartCoroutine(CharacterScript.CS.DieDie());
            }
            else
            {
                CharacterScript.ChangeToLastLife();
                hasDied = false;
            }
        }
        else if (name == "Throne")
        {
            return;
        }
        else if (animateBeforeDie)
        {
            transform.parent = null;
            GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
            foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            {
                col.enabled = false;
            }
            GetComponentInChildren<Animator>()?.SetBool("Die", true);
            foreach (MonoBehaviour mono in GetComponentsInChildren<MonoBehaviour>())
            {
                if (mono is not DestroyAfterTime && mono != this 
                    && mono is not UnityEngine.Rendering.Universal.Light2D && mono is not IOnDeath)
                {
                    mono.enabled = false;
                }
            }
        }
        else
        {
            Destroy(gameObject);
        }
        if (onDeaths != null)
        {
            for (int i = 0; i < onDeaths.Count; i++)
            {
                if (onDeaths[i] != null)
                {
                    onDeaths[i].GetComponent<IOnDeath>()?.OnDeath();
                }
                else
                {
                    onDeaths.RemoveAt(i);
                    i -= 1;
                }
            }
        }
    }

    public int CreateShield(float maxStrength, bool startFull, bool isWeak)
    {
        int newID = shieldIDCounter++;
        shields.Add(new Shield(maxStrength, startFull, newID, isWeak));
        return newID;
    }
    
    public void RemoveShield(int shieldID)
    {
        for (int i = 0; i < shields.Count; i++)
        {
            if (shields[i].ID == shieldID)
            {
                shields.RemoveAt(i);
                break;
            }
        }
    }

    public float ModifyShieldStrength(int shieldID, float amount)
    {
        for (int i = 0; i < shields.Count; i++)
        {
            if (shields[i].ID == shieldID)
            {
                shields[i] = new Shield(shields[i], amount);
                return shields[i].strength;
            }
        }
        return -999f;
    }

    public void EnforceShieldCaps()
    {
        float sumStrong = 0f;
        float sumWeak = 0f;

        for (int i = 0; i < shields.Count; i++)
        {
            if (shields[i].isWeak) sumWeak += shields[i].strength;
            else sumStrong += shields[i].strength;
        }

        // If total strong shield > maxHp, reduce from the *end*:
        if (sumStrong > maxHp)
        {
            float excess = sumStrong - maxHp;
            for (int i = shields.Count - 1; i >= 0 && excess > 0f; i--)
            {
                if (!shields[i].isWeak && shields[i].strength > 0f)
                {
                    float st = shields[i].strength;
                    if (st > excess)
                    {
                        shields[i] = new Shield(shields[i], -excess);
                        excess = 0f;
                    }
                    else
                    {
                        shields[i] = new Shield(shields[i], -st);
                        excess -= st;
                    }
                }
            }
        }

        // If total weak shield > maxHp, reduce from the *end*:
        if (sumWeak > maxHp)
        {
            float excess = sumWeak - maxHp;
            for (int i = shields.Count - 1; i >= 0 && excess > 0f; i--)
            {
                if (shields[i].isWeak && shields[i].strength > 0f)
                {
                    float st = shields[i].strength;
                    if (st > excess)
                    {
                        shields[i] = new Shield(shields[i], -excess);
                        excess = 0f;
                    }
                    else
                    {
                        shields[i] = new Shield(shields[i], -st);
                        excess -= st;
                    }
                }
            }
        }
    }

    [System.Serializable]
    public struct Shield
    {
        public float strength;
        public float maxStrength;
        public int ID;
        public bool isWeak;

        public Shield(float maxStrength, bool startFull = true, int ID = -1, bool isWeak = true)
        {
            this.maxStrength = maxStrength;
            this.strength = startFull ? maxStrength : 0f;
            this.ID = ID;
            this.isWeak = isWeak;
        }

        /// <summary>
        /// Copy constructor but modifies 'strength' by some amount (positive or negative).
        /// </summary>
        public Shield(Shield prev, float change)
        {
            this.maxStrength = prev.maxStrength;
            this.ID = prev.ID;
            this.isWeak = prev.isWeak;

            float newStr = prev.strength + change;
            if (newStr < 0f) newStr = 0f;
            if (newStr > maxStrength) newStr = maxStrength;
            this.strength = newStr;
        }
    }
}