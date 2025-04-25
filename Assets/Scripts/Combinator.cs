using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Combinator : Building
{
    [SerializeField] protected Vessel[] vessels;
    public MechanismSO upgradeBP;
    
    [SerializeField] private GameObject burstFX;
    
    [SerializeField] ParticleSystem FX;
    private ParticleSystem.EmissionModule em;
    private ParticleSystem.ShapeModule shap;

    [SerializeField] private ParticleSystem FX2;
    private ParticleSystem.EmissionModule em2;
    private ParticleSystem.ShapeModule shap2;
    [SerializeField] Image img;

    public override void Start()
    {
        base.Start();
        em = FX.emission;
        em2 = FX2.emission;
        shap = FX.shape;
        shap2 = FX2.shape;
        FX.GetComponent<ParticleSystemRenderer>().material = Vessel.mat;
        FX2.GetComponent<ParticleSystemRenderer>().material = Vessel.mat;
    }
    protected void ResetTiles()
    {
        for(int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] != null)
            {
                Destroy(tiles[i].gameObject);
            }
        }
        tiles = new List<BaseTile>();
    }


    public void MoveIcon()
    {
        StartCoroutine(MoveIconI());
    }

    private IEnumerator MoveIconI()
    {
        yield return new WaitForSeconds(1f);
        img.transform.localScale = Vector3.zero;
        Instantiate(FX,transform.position,Quaternion.identity,transform);
        img.sprite = upgradeBP.s;
        LeanTween.scale(img.gameObject, Vector3.one, 0.5f);
        img.color = Color.white;
        yield return new WaitForSeconds(0.75f);
        StartCoroutine(Move());
    }
    
    
    IEnumerator Move()
   {
      FX.gameObject.SetActive(true);
      FX2.gameObject.SetActive(true);
      StartCoroutine(Emission());
      Quaternion rot = GS.VTQ(transform.position);
      rot = Quaternion.Euler(Mathf.Sin(rot.eulerAngles.z * Mathf.Deg2Rad) * 30f, 0f,rot.eulerAngles.z + 180f);
     
      for(float t = 0f; t < 1.5f; t+=0.8f*Time.deltaTime)
      {
         img.transform.rotation = Quaternion.Lerp(img.transform.rotation,rot,Time.deltaTime);
         yield return null;
      }
      
      Vector3 endPos = img.transform.position.normalized * 1f;
      float d = Mathf.Pow(transform.position.sqrMagnitude,0.25f);
      img.transform.LeanMove(endPos, d).setEaseInCubic().setOnComplete(() => PortalScript.i.outside.color = Color.Lerp(PortalScript.i.outside.color,Color.white,0.05f));
      float n = em.rateOverTime.constant;
      float multiplier = 3f / d;
      for(float t = 0f; t < d; t+=Time.deltaTime)
      {
         img.transform.rotation = Quaternion.Lerp(img.transform.rotation,rot,Time.deltaTime*0.25f* (1.5f+t));
         shap.arcSpeedMultiplier = 2f + 2*multiplier*t;
         shap2.arcSpeedMultiplier = -2f - 2*multiplier*t;
         shap.radius = Mathf.Lerp(shap.radius, 0.08f, 0.1f * Time.deltaTime * t * t * t * Mathf.Pow(multiplier,3));
         shap2.radius = shap.radius - 0.075f;
         em.rateOverTime = em2.rateOverTime = n + t * multiplier * 0.25f * n;
         yield return null;
      }
      img.transform.position = endPos;
      PortalScript.i.outside.color = Color.Lerp(PortalScript.i.outside.color, Color.white, 0.1f);
      this.QA(() => { em.rateOverTime = em2.rateOverTime = 0f; },0.1f);
      //StartCoroutine(StopSpinning());
      for (float t = 1f; t > 0f; t -= Time.deltaTime)
      {
         img.color = new Color(1f,1f,1f,t);
         img.transform.localScale = new Vector3(t,t,t);
         yield return null;
      }
      yield return new WaitForSeconds(1.5f);
      FX.gameObject.SetActive(false);
      FX2.gameObject.SetActive(false);
      img.transform.rotation = Quaternion.identity;
      img.transform.localPosition = new Vector3(0f,0f,0.5f);
      img.color = Color.clear;
      ResetTiles();
      UIParent.SetActive(false);
   }
    
    IEnumerator Emission()
    {
        em.rateOverTime = 120f * (0.1f + 0.9f * SetM.FXQuality);
        for (float t = 0f; t < 100f; t += 80f* Time.deltaTime)
        {
            shap.arcSpeedMultiplier = 0.5f + 0.015f * t;
            shap2.arcSpeedMultiplier = -shap.arcSpeedMultiplier;
            FX.transform.localScale = FX2.transform.localScale = new Vector3(t, t, t) * 0.01f;
            shap.radius = 0.1f + 0.003f * t;
            shap2.radius = shap.radius - 0.075f;
            yield return null;
        }
        FX.transform.localScale = FX2.transform.localScale = Vector3.one;
    }

    protected void InstantAct(Blueprint b)
    {
        foreach (Vessel v in vessels)
        {
            v.InstantAct(b);
        }
    }
}