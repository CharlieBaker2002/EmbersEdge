using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fireball : MonoBehaviour
{
    [SerializeField] ParticleSystem ps;
    private ParticleSystem.EmissionModule em;
    private ParticleSystem.ShapeModule shap;
    [SerializeField] public int level = 1; //1-3.
    public Vector2 direction = Vector2.zero;
    [SerializeField] float castTime = 999f;
    public bool hit;
    private float discrepency;
    List<int> hitUnits = new();
    private float timeModulator = 1f;
    [SerializeField] CircleCollider2D col;

    private float pushDoubler = 1f;
    
    public void Release(float t, Vector2 d)
    {
        if (hit) return;
        castTime = t;
        transform.parent = GS.FindParent(GS.Parent.allyprojectiles);
        transform.localRotation = Quaternion.identity;
        direction = d;
    }

    IEnumerator Start()
    {
        transform.localRotation = Quaternion.identity;
        em = ps.emission;
        var main = ps.main;
        shap = ps.shape;
        float chargeTime = 2.2f + level * 0.5f;
        Vector3 startSize = (3f + level) * Vector3.one;
        Vector3 endSize = new Vector3(0.4f, 0.4f, 0.8f) * (level + 2f) / 10f;
        float endRate = 40f + GS.Sigma(level) * 60f;
        
        //PHASE IN
        for (float t = 0f; t < chargeTime; t += Time.deltaTime)
        {
            main.simulationSpeed = Mathf.Lerp(1f, 2f, t / chargeTime);
            em.rateOverTime = Mathf.Lerp(0f, endRate, t / chargeTime);
            shap.scale = Vector3.Lerp(startSize, endSize, Mathf.Sqrt(t / chargeTime));
            yield return null;
            if (t > castTime)
            {
                endSize = shap.scale;
                endRate = em.rateOverTime.constant;
                discrepency = (chargeTime - castTime) / chargeTime;
                break;
            }
        }

        float timy = Time.time;
        while (direction == Vector2.zero && Time.time - timy < 1f)
        {
            yield return null;
        }

        if (direction == Vector2.zero) //IF HOLD TOO LONG
        {
            Release(10f,IM.i.MousePosition(transform.position,true));
            direction *= 0.5f;
            endSize *= 20f;
            pushDoubler = 3f;
            for (int i = 0; i < 5; i++)
            {
                transform.Translate(direction* 0.17f * (1f + 0.5f * level),Space.World);
                yield return new WaitForFixedUpdate();
            }
            hit = true;
        }

        col.enabled = true;
        
        //MOVE
        em.rateOverTime = endRate;
        chargeTime = 2f + 0.35f * level;
        if (level == 3)
        {
            direction *= 0.9f;
        }
       
        for (float t = 0.35f + discrepency; t < chargeTime - discrepency; t += Time.deltaTime)
        {
            transform.Translate(direction * (1 + discrepency) * Mathf.Pow(t, 1.8f) * Time.deltaTime,Space.World);
            shap.scale = Vector3.Lerp(shap.scale, endSize * 0.35f, t * 0.5f * Time.deltaTime);
            em.rateOverTime = Mathf.Lerp(endRate, endRate * 2f, t * Time.deltaTime);
            if(hit) break;
            yield return null;
        }

        //BOOM
        hit = true;
        endSize *= 0.35f;
        var limitVelocity = ps.limitVelocityOverLifetime;
        limitVelocity.drag = 0f;
        limitVelocity.dampen = 0f;
        var vel2 = ps.velocityOverLifetime;
        vel2.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        em.rateOverTime = endRate * 2f;
        shap.rotation = new Vector3(0f, 0f, 45f);
        startSize = endSize;
        endSize = new Vector3(0.5f, 0.5f, 1.5f * (1 + discrepency) + 0.2f * level) * (0.5f * (level + 2));
        
        for (timeModulator = 0.25f * discrepency; timeModulator < 1f; timeModulator += 0.5f * Time.deltaTime * pushDoubler)
        {
            ps.transform.localRotation = Quaternion.Lerp(ps.transform.localRotation, Quaternion.Euler(90f, 0f, 60f),
                (1 - timeModulator) * 4 * Time.deltaTime);
            limitVelocity.dampen = timeModulator * 2f;
            limitVelocity.drag = timeModulator * 2f;
            shap.scale = Vector3.Lerp(startSize, endSize, timeModulator * 4f);
            em.rateOverTime = Mathf.Lerp(endRate * 2f, 0f, Mathf.Pow(timeModulator, 1f + 0.25f * (level - 1)));
            vel2.radial = Mathf.Lerp(-0.5f, 2f, timeModulator * 10f);
            transform.Translate(direction * Time.deltaTime * (1.8f - discrepency) *
                                Mathf.Pow(1.32f + level * 0.2f - timeModulator, 2),Space.World);
            yield return null;
        }
        
        em.rateOverTime = 0f;
        while (timeModulator < 2f)
        {
            timeModulator += Time.deltaTime * pushDoubler;
            yield return null;
        }
        timeModulator = 2f;
        col.enabled = false;
        yield return new WaitForSeconds(1f);
        Destroy(gameObject);
    }

    //Update based on ParticleSystem.
    private void Update()
    {
        if (!col.enabled) return;
        col.radius = Mathf.Pow(shap.scale.magnitude,0.25f) * 0.333f * (level + 2f) * 0.4f * (2f - timeModulator);
        col.offset = -direction * (0.6f * (1f-timeModulator));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if(other.attachedRigidbody == null) return;
        if(other.CompareTag(tag)) return;
        if (!other.attachedRigidbody.TryGetComponent<ActionScript>(out var AS)) return;
        if (AS.ls == null) return;
        int id = AS.GetInstanceID();
        if (hitUnits.Contains(id)) return;
        hitUnits.Add(id);
        
        int lvlModulator = GS.Sigma(level); //1,3,6
        float discrepencyModulator = 0.25f + 0.75f * (1 - discrepency); //0.25f for insta tap, 1f for max hold;
        float tModulator = (2f - timeModulator)/2f; //gets smaller over time.
        
        if (!hit && !AS.PS)
        {
            hit = true;
            AS.AddPush(0.5f,true, discrepencyModulator * lvlModulator * 15f * direction);
            AS.ls.Change(-lvlModulator * discrepencyModulator * 4f, 3);
            AS.ls.ChangeOverTime(-lvlModulator * 2f, 3f, 3,false);
            return;
        }
       
        AS.ls.Change(-level,3);
        AS.AddPush(0.5f,true, tModulator * discrepencyModulator * lvlModulator * 10f * pushDoubler * direction);
        AS.ls.ChangeOverTime(-lvlModulator * 2.5f * tModulator * Mathf.Sqrt(1f - discrepency), 2.5f * tModulator, 3, false);
    }

    
}
