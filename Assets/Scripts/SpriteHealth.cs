using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpriteHealth : MonoBehaviour
{
    [SerializeField] LifeScript ls;
    [SerializeField] SpriteRenderer sr;
    [SerializeField] Sprite[] sprites;
    [SerializeField] UnityEngine.Rendering.Universal.Light2D l;
    [SerializeField] Vector2 falloff;
    [SerializeField] Vector2 intensity;

    private void Start()
    {
        ls.onDamageDelegate += UpdateSprite;
    }

    private void UpdateSprite(float _)
    {
        sr.sprite = GS.PercentParameter(sprites, ls.hp / ls.maxHp);
        if (l != null)
        {
            l.falloffIntensity = Mathf.Lerp(falloff.y, falloff.x, ls.hp/ls.maxHp);
            l.intensity = Mathf.Lerp(intensity.y, intensity.x, ls.hp / ls.maxHp);
        }
    }
}
