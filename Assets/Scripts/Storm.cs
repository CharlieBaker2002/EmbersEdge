using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal; // for Light2D

public class Storm : Spell
{
    [SerializeField] private Light2D circleLight;
    [SerializeField] private float lightOnIntensity = 2f;   // How bright to turn on
    [SerializeField] private WigglyMissile missle;
    [SerializeField] private int missileCount = 9;
    [SerializeField] private SpriteRenderer circle;
    [SerializeField] private float range = 3f;
    [SerializeField] private float deployTime = 0.5f;
    [SerializeField] float interMissileWait = 0.1f;
    private float interMissileWaitHolder;
    private float startT;
    private Coroutine reduceCo = null;
    private float radius = 1f;

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        engagement = 2f;
        var pe = IM.i.PosAndExtent(GS.CS().position, range);
        Deploy(pe.Item1, deployTime * pe.Item2/range, 99999999f, deployTime + 2f);
        startT = Time.time;
        circle.transform.localScale = Vector3.one * radius * 2f;
        circleLight.pointLightOuterRadius = circle.transform.localScale.x * 0.75f;
        circleLight.intensity = lightOnIntensity;
        this.QA(()=> GS.FadeSR(this,circle, 1f,0.5f), deployTime * pe.Item2/range);
        reduceCo = StartCoroutine(ReduceSize(deployTime * pe.Item2/range));
    }
    
    IEnumerator ReduceSize(float wait)
    {
        yield return new WaitForSeconds(wait);
        while(true)
        {
            circle.transform.localScale = Vector3.Lerp(circle.transform.localScale, Vector3.one * radius,level * 0.25f*(Time.time - startT - wait)*Time.deltaTime);
            circleLight.pointLightOuterRadius = circle.transform.localScale.x * 0.75f;
            yield return null;
        }
    }
    
    public override void Performed(InputAction.CallbackContext ctx)
    {
        engagement = 2f;
        if (reduceCo != null)
        {
            StopCoroutine(reduceCo);
        }
        StartCoroutine(ShootMissiles(startT+deployTime - Time.time, (float)ctx.duration));
    }
    
    private IEnumerator ShootMissiles(float t, float held)
    {
        held = Mathf.Sqrt(Mathf.Clamp(held, 1, 5f));
        float sqrtHeld = Mathf.Sqrt(held);
        interMissileWaitHolder = interMissileWait / held;
        if (t > 0f) yield return new WaitForSeconds(t);
        yield return new WaitForSeconds(0.25f);
        GS.FadeSR(this,circle, 1f);
        float reduceLightDelta = lightOnIntensity / missileCount;
        float rad = transform.localScale.x * circle.transform.localScale.x * 0.5f;
        Vector3 v;
        float theta = 0f;
        float dtheta = 3f * 360f / missileCount;
        for (int i = 0; i < missileCount; i++)
        {
            v = transform.position + GS.RandCircle(0f, rad);
            float z = Time.time + interMissileWaitHolder;
            Vector3 dir = v - transform.position;
            while (Time.time < z)
            {
                transform.up = Vector3.Lerp(transform.up, dir, level * 20f*Time.deltaTime);
                yield return null;
            }
            var wiggle = Instantiate(missle, transform.position + (Vector3)GS.VTheta(theta,0.2f*level), Quaternion.identity,GS.FindParent(GS.Parent.allyprojectiles));
            wiggle.SetTarget(v,level,sqrtHeld);
            circleLight.intensity -= reduceLightDelta;
            theta += dtheta;
            if (i % 3 == 0)
            {
                theta += 360f / missileCount;
            }
        }
        yield return new WaitForSeconds(1f);
        circleLight.intensity = 0f;
        engagement = 0f;
        QuickReturn();
        StopCoroutine(reduceCo);
    }
    
    public override Vector2 GetManaAndCd()
    {
        return new Vector2(3f, 14f);
    }

    public override void Intellect(float i)
    {
        
    }

    public override void LevelUp()
    {
        missileCount *= 3;
        level++;
        radius = level;
        interMissileWait *= 0.5f;
        lightOnIntensity += 0.25f;
    }
}