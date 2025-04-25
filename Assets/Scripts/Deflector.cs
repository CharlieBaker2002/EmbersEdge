using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Deflector : MonoBehaviour
{
    public Sprite[] sprites;
    public Sprite deactiveSprite;
    private SpriteRenderer sr;
    private Collider2D col;
    private Animator anim;
    public int race;
    public float force = 5f;
    private int points = 0;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        anim = GetComponent<Animator>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if(collision.gameObject.TryGetComponent<LifeScript>(out var ls))
        {
            if(ls.race == race)
            {
                if (collision.gameObject.TryGetComponent<ActionScript>(out var AS))
                {
                    AS.AddPush(0.65f,true,force * collision.relativeVelocity.magnitude * -collision.contacts[0].normal);
                    AS.AddCC("mass", 2f, 0.25f);
                    points--;
                    if(points <= 0)
                    {
                        col.enabled = false;
                        var mag = gameObject.AddComponent<OrbMagnet>();
                        mag.typ = OrbMagnet.OrbType.Task;
                        mag.orbType = race;
                        mag.capacity = (int)( 8 / Mathf.Pow(2, race));
                        mag.action = delegate { anim.enabled = true; };
                        sr.sprite = deactiveSprite;
                        sr.color = new Color(1, 1, 1, 0.6f);
                    }
                    else
                    {
                        sr.sprite = sprites[points-1];
                    }
                }
            }
        }
    }

    public void TurnOnCollider()
    {
        anim.enabled = false;
        col.enabled = true;
        points = 8;
    }
}
