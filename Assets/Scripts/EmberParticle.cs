using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class EmberParticle : MonoBehaviour
{
    [SerializeField] public SpriteRenderer sr;
    [SerializeField] private Sprite[] sprs;
    private Vector2 vel;
    public float speed = 0.2f;
    public float fadeSpeed = 1f;
    public float rad = 1f;
    private float colorT;
    private bool isFading;
    public bool isStatic = false;
    [SerializeField] public EmberStoreBuilding eb;
    public bool fader = false;
    
    public void Start()
    {
        sr.material = GS.MatByEra(GS.era, true, false,true);
        if(isStatic) return;
        vel = Random.insideUnitCircle.normalized*speed;
        transform.localPosition = Random.insideUnitCircle * rad;
    }

    public void Light()
    {
        colorT = 0f;
        isFading = true;
    }
    
    private void Update()
    {
        
        // handle color fade when bouncing
        if (isFading)
        {
            int delta = Mathf.FloorToInt(colorT * 3.99f);
            sr.sprite = sprs[GS.era*4 + delta];
            sr.sortingOrder = 100 - delta;
            colorT += fadeSpeed * Time.deltaTime;
            if(fader) sr.color = new Color(1f,1f,1f,1f - colorT);
            if (colorT >= 1f)
            {
                colorT = 1f;
                isFading = false;
            }
        }
        if(isStatic) return;

        // move
        transform.position += (Vector3)vel * Time.deltaTime;

        // check wall hit
        if (transform.localPosition.sqrMagnitude > rad * rad)
        {
            var localPosition = transform.localPosition;
            Vector2 normal = ((Vector2)localPosition).normalized;

            // reflect + small random angle jitter
            vel = Vector2.Reflect(vel, normal);
            float jitter = Random.Range(-15f, 15f) * Mathf.Deg2Rad;
            vel = new Vector2(
                vel.x * Mathf.Cos(jitter) - vel.y * Mathf.Sin(jitter),
                vel.x * Mathf.Sin(jitter) + vel.y * Mathf.Cos(jitter)
            );

            // snap back to edge
            localPosition = normal * rad;
            transform.localPosition = localPosition;
            eb.Hit(localPosition);
            Light();
        }
    }
}
