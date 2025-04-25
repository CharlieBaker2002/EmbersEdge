using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConverterNew : Building
{
    private static readonly int Thecolor = Shader.PropertyToID("thecolor");
    [SerializeField] private Material m;
  [SerializeField] private Material desired;
  [SerializeField] private Animator anim;

  public override void Start()
  {
      base.Start();
      m = Instantiate(m);
      sr.material = m;
      Destroy(GetComponent<SpriteRenderer>());
  }

  public void LerpMatColour()
  {
      StartCoroutine(LerpColI());
      return;

      IEnumerator LerpColI()
      {
          var d = desired.GetColor(Thecolor);
          Color c = m.GetColor(Thecolor);
          for (float t = 0f; t < 2f; t += 0.5f * Time.deltaTime)
          {
              m.SetColor(Thecolor, Color.Lerp(c, d, t));
              yield return null;
          }
      }
  }

  public void SpawnOrbs()
  {
      GS.CallSpawnOrbs(transform.position, new []{0,50,0,0});
  }

  public void Disable()
  {
      Destroy(sr);
      anim.enabled = false;
  }
}
