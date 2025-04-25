using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderScript : MonoBehaviour
{
    public string task;
    public Slider slider; 
    public Text txt;
    public LifeScript LS;
    public CharacterScript CS;
    public WeaponScript ws;
    
    private void Update()
    {
       switch (task.ToLower())
       {
            case "health":
                HealthSlider();
                return;
            case "ammoandreload":
                if(ws!= null){
                    AmmoAndReloadSlider();
                }
                return;
            case "ammo":
                if(ws!= null){
                    Ammo();
                }
                return;
                
        }
    }

    private void HealthSlider()
    {
        slider.maxValue = LS.maxHp;
        float hp = LS.hp;
        if(hp > LS.maxHp)
        {
            hp = LS.maxHp;
        }
        else if (hp < 0f)
        {
            hp = 0;
        }
        slider.value = hp;
        txt.text = " " + Mathf.RoundToInt(hp).ToString() + " / " + LS.maxHp;
    }

    private void ManaSlider()
    {
        slider.maxValue = LS.maxHp;
        slider.value = LS.hp;
        txt.text = " " + LS.hp + " / " + LS.maxHp;
    }

    private void AmmoAndReloadSlider()
    {
        if(ws.reloadTimer > 0f)
        {
            slider.maxValue = ws.maxReload;
            slider.value = ws.reloadTimer;
            txt.text = (Mathf.Round(ws.reloadTimer)).ToString();
        }
        else
        {
            slider.value = 0;
            if(ws.ammoInClip == 0)
            {
                if(ws.totalAmmo == 0){
                    txt.text = "No Ammo";
                }
                else{
                    txt.text = "Reload";
                }
                
            }
            else
            {
                txt.text = " " + ws.ammoInClip.ToString();
                if (ws.totalAmmo < 1000)
                {
                    txt.text += " / " + ws.totalAmmo.ToString();
                }
                else
                {
                    txt.text += " / ∞";
                }
            }
        }
    }

    private void Ammo()
    {   
        if(ws.reloadTimer <= 0f)
        {
            if (ws.attackTimer <= 0f)
            {
                slider.value = slider.maxValue;
            }
            else
            {
                slider.maxValue = ws.attackReset;
                slider.value = ws.attackTimer;
            }
        }
        else
        {
            slider.value = 0;
        }
    }

    public void UpdateValues(WeaponScript weapon){
        if(weapon != null)
        {
            ws = weapon;
            if (task.ToLower() == "ammo")
            {
                txt.text = ws.name;
            }
        }
    }
}
