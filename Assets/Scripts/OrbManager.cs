using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class OrbManager : MonoBehaviour
{
    public static List<OrbScript> allOrbs;
    public Transform CS;
    float dist;
    float[] speeds = new float[] { 1f, 1.2f, 0.5f, 1.6f };
    float[] disperseSpeeds = new float[] { 1f, 1.2f, 0.5f, 1.6f };
    Vector2 dir;
    public static float distortion = 1f;

    private void Start()
    {
        if (RefreshManager.i.CASUALNOTREALTIME)
        {
            speeds = new[] { 5f, 5f, 5f, 5f };
        }
    }

    void Update()
    {
        //float p = Time.time % 40f;
        //Debug.Log(p + " " + distortion);
        //if(p < 17.5f)
        //{
        //    distortion = Mathf.Lerp(distortion, 2.5f, 0.0005f * Time.deltaTime * p * p);
        //}
        //else if(p < 20)
        //{
        //    distortion = Mathf.Lerp(distortion, 3.5f, Time.deltaTime);
        //}
        //else if(p > 22.5)
        //{
        //    distortion = Mathf.Lerp(distortion, 1f, 0.003f * Time.deltaTime * Mathf.Pow(42.5f - p,2));
        //}

        for(int i = 0; i < OrbScript.tot; i++)
        {
            if(i >= allOrbs.Count) { break; }
            OrbScript o = allOrbs[i];
            if(o == null) { continue; }
            if (!o.isActiveAndEnabled) { continue; }
            if (OrbScript.tot > 1752 * 3 * (0.01f + SetM.OrbQuality))
            {
                if (Random.Range(0, 2) == 0)
                {   
                    return;
                }
            }
            switch (o.state)
            {
                case OrbScript.OrbState.wild:
                    o.timeLeft -= Time.deltaTime;
                    if (o.timeLeft <= 0f)
                    {
                        o.ReturnToPool();
                        return;
                    }
                    if (o.timeLeft > 74f)
                    {
                        o.transform.position += (o.rot+90) * (o.timeLeft - 74f) * 2f * disperseSpeeds[o.orbType] * Time.deltaTime * new Vector3(Mathf.Sin(o.theta),Mathf.Cos(o.theta))/360f * Random.Range(1.5f,2f);
                    }
                    else
                    {
                        dir = (Vector2)(CS.position - o.transform.position);
                        if (OrbScript.canAttract[o.orbType])
                        {
                            dist = dir.sqrMagnitude;
                            if (dist < Mathf.Pow(disperseSpeeds[o.orbType] * Time.deltaTime, 2))
                            {
                                PlayerCollide(o);
                                continue;
                            }
                            if (dist < 5f + o.orbType)
                            {
                                o.transform.position += disperseSpeeds[o.orbType] * Time.deltaTime * (Vector3)dir.normalized / Mathf.Max(0.25f, dist);
                            }
                        }
                    }
                    break;
                case OrbScript.OrbState.collect:
                    if (OrbScript.canAttract[o.orbType])
                    {
                        dir = CS.position - o.transform.position;
                        dist = dir.sqrMagnitude;
                        if (dist < Mathf.Pow(10f * Time.deltaTime, 2))
                        {
                            PlayerCollide(o);
                            continue;
                        }
                        o.transform.position += 7.5f * Time.deltaTime * (Vector3)dir.normalized;
                    }
                    break;
                case OrbScript.OrbState.decelerate:
                    o.transform.localPosition = Vector2.Lerp(o.transform.localPosition, Vector3.zero, 0.6f * speeds[o.orbType] * Time.deltaTime);
                    continue;
                case OrbScript.OrbState.accelerate:
                    o.transform.Translate(speeds[o.orbType] * Time.deltaTime * 3 * -o.transform.localPosition.normalized);
                    continue;
                case OrbScript.OrbState.harvest:
                    if (o.hovTimer > 0f)
                    {
                        o.hovTimer -= Time.deltaTime;
                        if(o.hovTimer <= 0f)
                        {
                            o.transform.localPosition = Vector2.zero;
                            o.hovTimer = -1f;
                        }
                        o.transform.localPosition = Vector2.Lerp(o.transform.localPosition, Vector2.zero, 3f * Time.deltaTime * (2f-o.hovTimer));
                        
                    }
                    else
                    {
                        o.transform.localPosition = Vector2.Lerp(o.transform.localPosition, Random.insideUnitCircle * 0.2f ,  Time.deltaTime);
                    }
                    continue;
                case OrbScript.OrbState.hover:
                    o.hovTimer -= Time.deltaTime;
                    if (o.hovTimer > 0f)
                    {
                        o.transform.localPosition = Vector3.Lerp(o.transform.localPosition, new Vector2(distortion * (-0.5f + 1f * Mathf.PerlinNoise(Mathf.Sin(o.theta + 0.8f*Time.time),0.35f*Time.time)), -0.5f + Mathf.PerlinNoise(Mathf.Cos(o.theta+ 0.8f*Time.time), 0.35f*Time.time)).Rotated(o.rot), 4f * Time.deltaTime);
                    }
                    else if(o.hovTimer < 0f)
                    {
                        o.theta = Random.Range(0f, 2 * Mathf.PI);
                        o.hovTimer = Random.Range(2f, 4f);
                    }
                    break;
                case OrbScript.OrbState.hoverstore:
                    o.hovTimer -= Time.deltaTime;
                    if (o.hovTimer > 0f)
                    {
                        dir = new Vector2(distortion * (-0.5f + 1f * Mathf.PerlinNoise(Mathf.Sin(o.theta + 0.8f * Time.time), 0.35f * Time.time)), -0.5f + Mathf.PerlinNoise(Mathf.Cos(o.theta + 0.8f * Time.time), 0.35f * Time.time)).Rotated(o.rot);
                        o.transform.localPosition = Vector3.Lerp(o.transform.localPosition, new Vector3(dir.x*1.75f,dir.y*0.75f, 0f) , 4f * Time.deltaTime);
                    }
                    else if (o.hovTimer < 0f)
                    {
                        o.theta = Random.Range(0f, 2 * Mathf.PI);
                        o.hovTimer = Random.Range(2f, 4f);
                    }
                    break;
                case OrbScript.OrbState.deposit:
                    float r = o.transform.localPosition.magnitude;
                    r = Mathf.Lerp(r, 0f, Time.deltaTime * disperseSpeeds[o.orbType]);
                    float radtheta = Mathf.Atan2(o.transform.localPosition.y, o.transform.localPosition.x);
                    radtheta = Mathf.Lerp(radtheta, o.theta, Time.deltaTime * 0.3f * disperseSpeeds[o.orbType]);
                    o.transform.localPosition = new Vector3(Mathf.Cos(radtheta) * r, Mathf.Sin(radtheta) * r);
                    continue;
            }
        }
    }

    void PlayerCollide(OrbScript o)
    {  
        if (ResourceManager.instance.HasRoom(o.orbType))
        {
            o.state = OrbScript.OrbState.follow;
            ResourceManager.instance.heldOrbs.Add(o);
            o.gameObject.SetActive(false);
        }
        else
        {
            o.ReturnToPool();
        }
    }

    public static IEnumerator LerpDistortion(float t, float wait = 0f)
    {
        if(wait > 0f)
        {
            yield return new WaitForSeconds(wait);
        }
        for (float i = 0f; i <= 5f; i+= Time.deltaTime)
        {
            distortion = Mathf.Lerp(distortion, t, i * Time.deltaTime);
            yield return null;
        }
    }
    //era, 
}


