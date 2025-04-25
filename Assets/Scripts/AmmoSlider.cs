using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AmmoSlider : CoreSlider
{
    public static AmmoSlider i;
    public float ammoSliderLength = 5;
    public Slider weaponSlider;
    public TextMeshProUGUI weaponText;
    public TextMeshProUGUI clipsText;

    private void Awake()
    {
        i = this;
    }

    public override void UpdateMax(float m)
    {
        max = m;
        bas.sizeDelta = new Vector2(length * 96, 100);
    }

    public void InitWeapon(WeaponScript w)
    {
        weaponSlider.maxValue = Mathf.Max(0.1f,w.attackReset);
        weaponText.text = w.name.Split("(Clone)")[0];
        UpdateMax(w.ammoPerClip);
        UpdateSlider(w.ammoInClip);
        WeaponSliderResetClips(w.totalAmmo);
        WeaponSliderResetT(w.reloadTimer);
    }

    public void WeaponSliderResetT(float reset)
    {
        weaponSlider.value = weaponSlider.maxValue - reset;
        if(weaponSlider.maxValue - weaponSlider.value < 0.05f)
        {
            weaponSlider.value = weaponSlider.maxValue;
        }
    }

    public void WeaponSliderResetClips(int maxAmmo)
    {
        if (maxAmmo > 100000)
        {
            clipsText.text = "Infinite" + " Reserve";
            return;
        }
       clipsText.text = maxAmmo + " Reserve";}
           
}
