using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class OrbScript : MonoBehaviour
{
    //NOW MANAGED BY ORBMANAGER!
    public static bool[] canAttract = new bool[] { true, true, true, true };
    public static int tot = 0;
    
    public int orbType = 0;
    public enum OrbState {wild, hoverstore, hover, collect, accelerate, decelerate, harvest, deposit, follow }
    public OrbState state = OrbState.wild;
    public float timeLeft = 75f;
    [HideInInspector]
    public float theta;
    public float hovTimer = -1f;
    public float rot = 1f;

    void Start()
    {
        theta = Random.Range(0f, 2 * Mathf.PI);
        if(orbType == 0)
        {
            rot = Random.Range(0, 8) * 45f;
        }
        else if(orbType == 1)
        {
            rot = Random.Range(0, 4) * 90f;
        }
        else if(orbType == 2)
        {
            rot = Random.Range(2, 4) * 90f;
        }
        else if(orbType == 3)
        {
            Random.Range(0f, 360f);
        }
    }

    public void Hover(bool store)
    {
        if (store)
        {
            state = OrbState.hoverstore;
        }
        else
        {
            state = OrbState.hover;
        }
        hovTimer = 0f;
    }

    public void Harvest()
    {
        state = OrbState.harvest;
        hovTimer = 2f;
    }

    public void ReturnToPool()
    {
        if (!gameObject.activeInHierarchy) return;
        state = OrbState.wild;
        timeLeft = 75f;
        Start();
        theta = Random.Range(0f, 360f);
        transform.parent = SpawnManager.instance.orbParent;
        SpawnManager.instance.orbPools[orbType].Release(gameObject);
    }

    private void OnEnable()
    {
        OrbManager.allOrbs.Add(this);
    }

    private void OnDisable()
    {
        OrbManager.allOrbs.Remove(this);
    }

    //public void OnTriggerEnter2D(Collider2D collision)
    //{
    //    if (canAttract[orbType])
    //    {
    //        if (collision.name == "Character")
    //        {
    //            if (state == "wild" || state == "collect")
    //            {
    //                if (ResourceManager.instance.HasRoom(orbType))
    //                {
    //                    state = "follow";
    //                    ResourceManager.instance.heldOrbs.Add(this);
    //                    col.enabled = false;
    //                    gameObject.SetActive(false);
    //                }
    //                else
    //                {
    //                    ReturnToPool();
    //                }
    //            }
    //        }
    //    }
    //}

    //void Update()
    //{
    //    if (tot > 1752 * 3 * (0.01f + SetM.OrbQuality))
    //    {
    //        if (Random.Range(0, 2) == 0)
    //        {
    //            return;
    //        }
    //    }
    //    switch (state)
    //    {
    //        case "wild":
    //            timeLeft -= Time.deltaTime;
    //            if (timeLeft <= 0f)
    //            {
    //                ReturnToPool();
    //                return;
    //            }
    //            if (timeLeft > 74f)
    //            {
    //                transform.position += (timeLeft - 74f) * Time.deltaTime * direction;
    //            }
    //            else
    //            {
    //                direction = (Vector2)(playerTransform.position - transform.position);
    //                if (canAttract[orbType])
    //                {
    //                    dist = direction.sqrMagnitude;
    //                    if (dist < moveDistance * moveDistance)
    //                    {
    //                        transform.position += 2 * speedCoefficient * Time.deltaTime * direction.normalized / Mathf.Max(0.25f, direction.sqrMagnitude);
    //                        if (transform.InDungeon())
    //                        {
    //                            if (col.enabled)
    //                            {
    //                                break;
    //                            }
    //                            else
    //                            {
    //                                col.enabled = true;
    //                            }
    //                        }
    //                        else
    //                        {
    //                            if (dist <= 5 * Time.deltaTime)
    //                            {
    //                                if (!col.enabled) col.enabled = true;

    //                            }
    //                            else
    //                            {
    //                                if (col.enabled) col.enabled = false;
    //                            }
    //                        }

    //                    }
    //                }
    //            }
    //            break;
    //        case "collect":
    //            if (canAttract[orbType])
    //            {
    //                direction = (Vector2)(playerTransform.position - transform.position);
    //                transform.position += 7.5f * Time.deltaTime * direction.normalized;
    //            }
    //            break;
    //        case "decelerate":
    //            transform.localPosition = Vector2.Lerp(transform.localPosition, Vector3.zero, 0.6f * speedCoefficient * Time.deltaTime);
    //            break;
    //        case "accelerate":
    //            transform.localPosition = Vector2.Lerp(transform.localPosition, Vector3.zero, 1.5f * speedCoefficient * Time.deltaTime / transform.localPosition.sqrMagnitude);
    //            break;
    //        case "hover":
    //            hovTimer -= Time.deltaTime;
    //            if (hovTimer > 0f)
    //            {
    //                return;
    //            }
    //            if ((transform.position - pos).sqrMagnitude < 0.1f)
    //            {
    //                pos = 0.8f * Random.insideUnitCircle + (Vector2)transform.parent.position;
    //                direction = (pos - transform.position).normalized;
    //                hovTimer = 0.05f + 0.002f * SetM.Invert(SetM.OrbQuality, tot, 1.01f);
    //                return;
    //            }
    //            transform.position += Time.deltaTime * speedCoefficient * direction;
    //            break;
    //    }
    //}
}