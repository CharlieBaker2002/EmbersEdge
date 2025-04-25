using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Explosive : MonoBehaviour
{
    [Header("Positive For Damage!")]
    public float damage = 1f;
    public float damageOverT = 0f;
    public float damageOverTTime = 2f;
    public float radius = 1f;
    public int damageType = 0;
    [Tooltip("Does up to half damage when within further half blast radius")]
    public bool distanceScaleHalf = false;
    public bool allowBuildings = false;
    public bool allowProjectiles = false;
    public UnityEngine.Rendering.Universal.Light2D li;
    public float explodeTime = 0.4f;
    private List<Transform> enemies;
    private LifeScript l;

    public void Boom()
    {
        if (li != null) { StartCoroutine(Light()); }
        enemies = GS.FindEnemies(tag, transform.position, radius, allowBuildings, allowProjectiles);
        if (enemies.Count == 0) return;
        foreach(Transform t in enemies)
        {
            l = t.GetComponentInChildren<LifeScript>();
            if (l == null) continue;
            if (distanceScaleHalf)
            {
                l.Change(-ConvertAmount(damage, t), damageType);
                if (damageOverT != 0)
                {
                    l.ChangeOverTime(-ConvertAmount(damageOverT, t), damageOverTTime, damageType);
                }
            }
            else
            {
                l.Change(-damage, damageType);
                if (damageOverT != 0)
                {
                    l.ChangeOverTime(-damageOverT, damageOverTTime, damageType,false);
                }
            }
        }
    }

    private float ConvertAmount(float x, Transform T)
    {
        float dist = (T.position - transform.position).magnitude;
        if(dist <= radius / 2)
        {
            return x;
        }
        dist -= radius * 0.5f;
        dist /= (radius * 0.5f);
        dist = 1 - dist; //inverted half normalized distance (normalized with half)
        return x * 0.5f * (1 + dist);

    }

    private IEnumerator Light()
    {
        float max = li.intensity + 1f;
        float coef = 4.5f / explodeTime;
        for(float i = 0f; i < explodeTime * 0.5f; i += Time.deltaTime)
        {
            li.intensity = Mathf.Lerp(li.intensity, max, Time.deltaTime * coef);
            yield return null;
        }
        for (float i = 0f; i < explodeTime * 0.5f; i += Time.deltaTime)
        {
            li.intensity = Mathf.Lerp(li.intensity, 0f, Time.deltaTime * coef);
            yield return null;
        }
        li.intensity = 0f;
    }

    public void DestroyNow()
    {
        Destroy(gameObject);
    }
}
