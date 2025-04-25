using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Status : MonoBehaviour
{
    public static Sprite[] statusSprites;
    public static Sprite[] borderSprites;
    
    [SerializeField] SpriteRenderer statusSr;
    [SerializeField] private SpriteRenderer borderSr;

    public int ind;
    private Unit unit;

    [Range(0, 1)] public float sliderValue;

    // New type for shields:
    public enum typ { increaseThenDecrease, decrease, flat }

    public typ taip = typ.flat;

    public bool cancelWithStun = false;
    public bool cancelWithDamage = false;

    public float value1; 
    public float value2; 

    private int colInd = 0;
    
    public float timer = 0f;
    [HideInInspector] public int unitInstanceId;
    public bool dissapearing = false;
    
    public Action<float> onDamageDelete;
    
    public static string[] typs = new string[]
    {
        "stun",
        "root",
        "push",
        "slow",
        "impenetrable",
        "stim",
        "juggernaut",
        "shield",
        "heal",
        "invulnerable",
        "immaterial",
        "reflect",
        "dodging",
        "dematerialise",
        "leech",
        "static",
        "convert",
        "weak heal",
        "weak shield",
    };

    private static Color[] cols = new Color[]
    {
        new Color32(145, 0, 200, 255),
        new Color32(25, 150, 150, 255),

        new Color32(170, 215, 255, 255),
        new Color32(50, 255, 50, 255),
        new Color32(50, 50, 255, 255),
        new Color32(255, 0, 60, 255),
    };

    private void OnEnable()
    {
        colInd = 0;
        timer = 0f;
        ind = -1;
        cancelWithDamage = false;
        cancelWithStun = false;
        dissapearing = false;
    }

    public static int GetTypIndex(string typ)
    {
        typ = typ.ToLower();
        if (typs.Contains(typ))
        {
            return Array.IndexOf(typs, typ);
        }
        return -1;
    }
    
    public void SetTyp(int ind_, Unit u, float value1_, float value2_)
    {
        ind = ind_;
        statusSr.sprite = statusSprites[ind];
        
        unit = u;
        unitInstanceId = unit.GetInstanceID();
        unit.stati.Add(this);

        value1 = value1_;
        value2 = value2_;

        // Decide the 'taip' based on index:
        switch (ind)
        {
            case 7: // shield
                colInd = 1;
                taip = typ.flat;   // <-- NEW
                break;
            case 18: // weak shield
                colInd = 1;
                taip = typ.flat;   // <-- NEW
                cancelWithDamage = true; 
                cancelWithStun = true;
                break;
            // everything else as before:
            case 0: // stun
                taip = typ.decrease;
                break;
            case 1: // root
                taip = typ.decrease;
                break;
            case 2: // push
                taip = typ.decrease;
                break;
            case 3: // slow
                taip = typ.decrease;
                break;
            case 4: // impenetrable
                if (value2 == 0)
                {
                    colInd = 1;
                    taip = typ.flat;
                    sliderValue = 1;
                    UpdateSlider();
                }
                else
                {
                    taip = typ.decrease;
                }
                break;
            case 5: // stim
                colInd = 1;
                cancelWithStun = true;
                taip = typ.decrease;
                break;
            case 6: // juggernaut
                colInd = 1;
                taip = typ.decrease;
                break;
            case 8: // heal
                colInd = 1;
                cancelWithStun = true;
                taip = typ.decrease;
                break;
            case 9: // invulnerable
                colInd = 1;
                taip = typ.decrease;
                break;
            case 10: // immaterial
                colInd = 1;
                taip = typ.decrease;
                break;
            case 11: // reflect
                colInd = 1;
                if (value2 == 0)
                {
                    taip = typ.flat;
                    sliderValue = 1;
                    UpdateSlider();
                }
                else
                {
                    taip = typ.decrease;
                }
                break;
            case 12: // dodging
                colInd = 1;
                taip = typ.decrease;
                cancelWithStun = true;
                break;
            case 13: // dematerialise
                colInd = 2;
                taip = typ.increaseThenDecrease;
                break;
            case 14: // leech
                colInd = 3;
                taip = typ.increaseThenDecrease;
                break;
            case 15: // static
                colInd = 4;
                taip = typ.increaseThenDecrease;
                break;
            case 16: // convert
                colInd = 5;
                taip = typ.increaseThenDecrease;
                break;
            case 17: // weak heal
                colInd = 1;
                taip = typ.decrease;
                cancelWithDamage = true;
                cancelWithStun = true;
                break;
        }

        // If a typical time‐based negative effect, adjust by vulnerability:
        if (taip == typ.decrease && ind != 7 && ind != 18) 
        {
            value1_ *= unit.statVulnerability;
            sliderValue = 1f;
            value1 = 1f / value1_; 
        }

        if (cancelWithDamage)
        {
            SetUpDamageDelegate();
        }

        borderSr.color = cols[colInd];
        enabled = taip != typ.flat;

        transform.localScale = Vector3.zero;
        LeanTween.scale(gameObject, Vector3.one, 0.4f).setEaseOutBack();
        unit.ApplyStatus(this);

        // For certain “increaseThenDecrease” statuses, we do initial clamp:
        if (taip == typ.increaseThenDecrease)
        {
            sliderValue = Mathf.Clamp01(value1/value2);
            UpdateSlider();
        }
    }
    
    public void SetUpDamageDelegate()
    {
        onDamageDelete = f =>
        {
            if (f >= 0f) return;
            if (this == null) return;
            if (dissapearing) return;
            if (unit == null) return;
            unit.ls.onDamageDelegate -= onDamageDelete;
            Dissapear();
            unit.StatusComplete(ind);
        };
    }
   
    void Update()
    {
        if (taip == typ.flat) return; 
        switch (taip)
        {
            case typ.decrease:
                timer+=Time.deltaTime;
                sliderValue -= Time.deltaTime * value1;
                if(sliderValue <= 0)
                {
                    Dissapear();
                    unit.StatusComplete(ind);
                }
                else
                {
                    UpdateSlider();
                }
                break;
            case typ.increaseThenDecrease:
                timer += Time.deltaTime;
                if (value1 < value2) //set to zero if not yet activated
                {
                    if (timer > 6f)
                    {
                        value1 -= 0.2f * Time.deltaTime * value2;
                        sliderValue -= 0.2f * Time.deltaTime; //remove 20% a second after waiting 6 seconds
                        if (sliderValue <= 0)
                        {
                            if (ind == 15)
                            {
                                StatusManager.RemoveStaticUnit(unit);
                            }
                            Dissapear();
                        }
                        else
                        {
                            UpdateSlider();
                        }
                    }
                }
                else
                {
                    switch (ind)
                    {
                        case 13: //dematerialise
                            return;
                        case 14: //Leech=
                            sliderValue -= Time.deltaTime * 0.3334f; //3 seconds
                            break;
                        case 15: //Static
                            sliderValue -= Time.deltaTime * 0.2f; //5 seconds
                            break;
                        case 16: //Convert  
                            sliderValue -= Time.deltaTime * 0.125f; //8 seconds
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (sliderValue <= 0f)
                    {
                        Dissapear();
                        unit.StatusComplete(ind);
                        return;
                    }
                    UpdateSlider();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Dissapear()
    {
        if (dissapearing) return;
        if (this == null) return;
        dissapearing = true;
        enabled = false;
        LeanTween.scale(gameObject, Vector3.zero, 0.4f).setEaseInBack().setOnComplete(() =>
        {
            if(unit != null) unit.stati.Remove(this);
            StatusManager.i.statusPool.Release(this);
        });
    }

    public void UpdateSlider()
    {
        borderSr.sprite = GS.PercentParameter(borderSprites, sliderValue);
    }
}