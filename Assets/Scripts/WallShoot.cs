using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class WallShoot : WallClinger
{
    [SerializeField] private GameObject burst;
    private float start;
    [SerializeField] Vector2 speedRange = new Vector2(5f, 1f);
    [SerializeField] private float duration;
    [SerializeField] private bool scaleY;
    private float speedSave;

    [SerializeField] private float heightMultiplier = 1f;
    private void Awake()
    {
        speedSave = speed;
        start = Time.time;
        act = (vector3, quaternion) =>
        {
            var ob = Instantiate(burst, vector3, quaternion);
            if (!scaleY) return;
            var v = ob.transform.localScale;
            ob.transform.localScale = new Vector3(v.x * (reverse ? -1f : 1f),v.y* heightMultiplier*(1f - (Time.time - start)/duration), v.z);
        };
    }

    private void Update()
    {
        speed = speedSave * Mathf.Lerp(speedRange.x,speedRange.y,(Time.time - start)/duration);
        if (Time.time - start > duration)
        {
            act = (vector3, quaternion) => { };
            Destroy(gameObject,0.5f);
        }
    }
}
