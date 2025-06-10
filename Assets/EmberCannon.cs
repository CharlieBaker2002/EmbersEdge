using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EmberCannon : Extractor
{
    [SerializeField] EmbersEdge ee;
    [SerializeField] private Transform stick;
    public static List<EmberCannon> ecs;
    [SerializeField] private float severity = 0.1f;
    
    public void Activate()
    {
        ee.bias += severity;
        StartCoroutine(Activation());

        IEnumerator Activation()
        {
            LeanTween.value(gameObject, 0f, 1f, 5f).setOnUpdate(x =>
            {
                omega = 0.5f*x;
                em.rateOverTime = x * 40f;
            }).setEaseOutSine();
            yield return new WaitForSeconds(2.5f);
            
            int n = Mathf.FloorToInt(Random.Range(0f, 1f) + severity * 20f);
            for (int i = 0; i < n; i++)
            {
                var e = Instantiate(emb, ee.transform.position + (Vector3)Random.insideUnitCircle * 0.25f, GS.RandRot(), GS.FindParent(GS.Parent.fx));
                e.extract = this;
                e.to = transform.position + (Vector3)Random.insideUnitCircle * 0.25f;
                yield return new WaitForSeconds(1f);
            }
            StopSpinning();
        }
    }

    protected override void BEnable()
    {
        ee = SpawnManager.instance.EEs.OrderBy(x=>Vector2.Distance(transform.position, x.transform.position)).First();
        stick.transform.up = ee.transform.position - transform.position;
        ecs.Add(this);
    }

    protected override void BDisable()
    {
        ecs.Remove(this);
    }
}
