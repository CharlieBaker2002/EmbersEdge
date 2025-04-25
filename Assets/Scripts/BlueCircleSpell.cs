using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class BlueCircleSpell : Spell
{
    public GameObject prefab;
    public float n = 10;
    float maxTime = 4f;
    float spinTimer = 0f;
    float theta = 0f;
    private float atr = 0;
    public bool notEmbers = true;
    private Transform parent;
    
    [SerializeField] Transform stick;
    
    private void Start()
    {
        if (notEmbers)
        {
            stick.parent = GS.CS();
        }
        parent = notEmbers ? stick : GS.FindParent(GS.Parent.allyprojectiles);
    }

    void OnDestroy()
    {
        if (stick != null)
        {
            Destroy(stick.gameObject);
        }
    }

    void Update()
    {
        //transform.rotation = Quaternion.identity;
        if (notEmbers)
        {
            if (spinTimer > 0f)
            {
                theta += (3 + 0.6f * ((level - 1) * 1.5f + maxTime - spinTimer)) * 10f * Mathf.PI * Time.deltaTime;
                stick.rotation = Quaternion.Euler(new Vector3(0, 0f, theta));
            }
        }
    }

    IEnumerator GenerateRing(Transform[] projs, float time)
    {
        yield return new WaitForSeconds(time);
        float timer = maxTime;
        while (timer > 0f)
        {
            timer -= Time.deltaTime;
            foreach (Transform proj in projs)
            {
                if (proj != null)
                {
                    proj.localPosition += 0.3f * Time.deltaTime * proj.localPosition.normalized;
                }
            }
            yield return null;
        }
        foreach (Transform proj in projs)
        {
            if (proj != null)
            {
                Destroy(proj.gameObject);
            }
        }
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        for (int j = 0; j < level; j++)
        {
            float flux = 1 + 0.5f * ResourceManager.instance.UseCores(1, 1);
            int newN = Mathf.RoundToInt(n * flux);
            if (j == 0)
            {
                spinTimer = maxTime + (level - 1) * 1.5f;
            }
            float theta2;
            Transform[] transforms = new Transform[newN];
            Vector2[] dirs = new Vector2[newN];
            if (notEmbers)
            {
                Transform t = stick.transform;
                for (int i = 0; i < newN; i++)
                {
                    theta2 = i * 2 * Mathf.PI / newN;
                    Vector3 vect = new Vector3(0.2f * Mathf.Sin(theta2), 0.2f * Mathf.Cos(theta2), 0f);
                    transforms[i] = Instantiate(prefab, t.position + vect, t.rotation, parent).transform;
                    transforms[i].GetComponent<ProjectileScript>().father = CharacterScript.CS.transform;
                    if (!notEmbers)
                    {
                        dirs[i] = transforms[i].position - transform.position;
                    }
                }
            }
            else
            {
                for (int i = 0; i < newN; i++)
                {
                    theta2 = i * 2 * Mathf.PI / newN;
                    Vector3 vect = new Vector3(0.2f * Mathf.Sin(theta2), 0.2f * Mathf.Cos(theta2), 0f);
                    transforms[i] = Instantiate(prefab, transform.position + vect, transform.rotation, parent).transform;
                    transforms[i].GetComponent<ProjectileScript>().father = CharacterScript.CS.transform;
                    if (!notEmbers)
                    {
                        dirs[i] = transforms[i].position - transform.position;
                    }
                }
            }
           
            foreach (Transform t in transforms)
            {
                t.GetComponent<ProjectileScript>().damage *= 1 + 0.1f *atr;
                if (!notEmbers)
                {
                    t.GetComponent<ProjectileScript>().timer *= 0.9f + 0.2f * level;
                    t.GetComponent<ActionScript>().maxVelocity *= 0.6f + 0.45f * level;
                    t.GetComponent<Seeking>().seekDistance *= 0.6f + 0.45f * level;
                    t.GetComponent<Seeking>().force *= 0.8f + 0.23f * level;
                    t.gameObject.SetActive(false);
                    
                }
            }
            if (notEmbers)
            {
                StartCoroutine(GenerateRing(transforms, j * 1.5f));
            }
            else
            {
                StartCoroutine(ActiveOverTime(transforms,dirs));
            }
        }
    }

    IEnumerator ActiveOverTime(Transform[] ts, Vector2[] dirs)
    {
        for(int i = 0; i < ts.Length; i++)
        {
            ts[i].gameObject.SetActive(true);
            ts[i].GetComponent<ProjectileScript>().SetValues(dirs[i], tag);
            yield return new WaitForSeconds(Random.Range(0.1f,0.25f));
        }
    }


    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        return;
    }

    public override void LevelUp()
    {
        if (level < 3)
        {
            level += 1;
            n *= 1.4f;
            maxTime += 1.5f;
        }
    }
    public override Vector2 GetManaAndCd()
    {
        if (notEmbers)
        {
            return new Vector2(1 + 2 * level, 12 - 2 * level);
        }
        return new Vector2(1.5f + 0.5f * level * level, 5 - level);
    }

    public override void Intellect(float i)
    {
        atr = i;
    }
}

