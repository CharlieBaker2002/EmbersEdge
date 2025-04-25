using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CD : MonoBehaviour
{
    private static readonly int Spread = Shader.PropertyToID("_Spread");
    [SerializeField] private Material rad;
    [SerializeField] private SpriteRenderer sr;
    public Transform follow;
    public Vector2 offset;
    private Material mat;
    public float value;
    private float _val;
    private float decay;
    Quaternion rot = Quaternion.Euler(0f,0f,90f);
    [SerializeField] Color[] colours = new Color[4];
    public bool paused = false;
    
    private void Awake()
    {
        sr.enabled = false;
        sr.material = Instantiate(rad);
        mat = sr.material;
        transform.rotation = rot;
        Debug.Log("This is a test!");
    }
    
    public void SetValue(float t)
    {
        if (sr.enabled) return;
        sr.enabled = true;
        _val = 0;
        value = 0;
        decay = 1f / t;
    }
    
    void Update()
    {
        if(follow == null) return;
        transform.position = follow.transform.position;
        if (offset != Vector2.zero)
        {
            transform.position += (Vector3)offset.Rotated(follow.transform.rotation.eulerAngles.z);
        }

        if (paused) return;
        if (decay != 0f)
        {
            value += decay * Time.deltaTime;
            if (value >= 1f)
            {
                value = 1f;
                decay = 0f;
                sr.enabled = false;
            }
        }
        if (_val == value) return;
        _val = value;
        mat.SetFloat(Spread, (1f-value) * 720f);
    }

    public void SetColour(Part.PartType p)
    {
        if (p is Part.PartType.Weapon or Part.PartType.Ability) //green for active parts
        {
            sr.color = colours[0];
        }
        else if (Part.Ring(p) == Part.RingClassifier.Core || p == Part.PartType.Melee) //blueish for motion stuff
        {
            sr.color = colours[1];
        }
        else
        {
            sr.color = colours[2]; //yellow otherwise
        }
    }
}
