using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClawBotClaw : MonoBehaviour
{
    [SerializeField]
    Sprite[] sprites;
    [SerializeField]
    SpriteRenderer sr;
    public LifeScript ls;
    [SerializeField] CircleCollider2D[] cols;

    private void Start()
    {
        ls.onDamageDelegate += UpdateSprite;
        UpdateSprite();
    }

    public void UpdateSprite(float useless = 0)
    {
        if (ls.hp < 0)
        {
            return;
        }
        float frac = Mathf.Min(1f,ls.hp / 14f);
        sr.sprite = sprites[Mathf.RoundToInt((sprites.Length - 1) * frac)];
        cols[0].radius = 0.45f * frac;
        cols[1].radius = 0.45f * frac;
    }
}
