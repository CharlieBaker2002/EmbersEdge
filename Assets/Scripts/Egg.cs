using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Egg : MonoBehaviour
{
    public GameObject g;
    public CircleCollider2D[] cols;
    public float circleRad;
    public SpriteRenderer[] srs;
    public Light2D[] ls;
    public float lMax = 1.75f;
    public Vector2[] birthDirs = new Vector2[] { new Vector2(0,0.01f) };
    public float time;
    [Header("Contains all sprites including first sprite:")]
    public Sprite[] sprites;
    private int count = 0;
    private float maxTime;
    private float timer;
    private bool spawned = false;

    private void Awake()
    {
        foreach(Light2D l in ls)
        {
            l.lightCookieSprite = sprites[0];
            l.intensity = 0.4f * lMax + 0.6f * lMax / sprites.Length;
        }
        maxTime = time / sprites.Length;
        timer = maxTime;
    }

    private void Update()
    {
        if (!spawned)
        {
            float r = FirstNotNull();
            for(int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                {
                    cols[i].radius = r;
                }
            }
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                count++;
                timer = maxTime;
                if (count < sprites.Length)
                {
                    for (int i = 0; i < srs.Length; i++)
                    {
                        if (srs[i] != null)
                        {
                            srs[i].sprite = sprites[count];
                            ls[i].intensity += 0.6f * lMax / sprites.Length;
                            ls[i].lightCookieSprite = sprites[count];
                        }
                    }
                }
                else
                {
                    spawned = true;
                    for (int i = 0; i < srs.Length; i++)
                    {
                        if (cols[i] != null)
                        {
                            cols[i].enabled = false;
                            StartCoroutine(Lerp(srs[i], ls[i]));
                            for (int j = 0; j < birthDirs.Length; j++)
                            {
                                Instantiate(g, srs[i].transform.position + (Vector3)birthDirs[j], GS.VTQ(birthDirs[j]), GS.FindParent(GS.Parent.enemies));
                            }
                        }
                    }
                }
            }
        }
    }

    private IEnumerator Lerp(SpriteRenderer sr, Light2D l)
    {
        l.enabled = false;
        while (true)
        {
            if(sr == null)
            {
                yield break;
            }
            sr.color = Color.Lerp(sr.color, Color.clear, Mathf.Min(0.5f, Time.deltaTime));
            if(sr.color.a < 0.1f)
            {
                Destroy(gameObject);
            }
            yield return null;
        }
    }

    private float FirstNotNull()
    {
        for(int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                return Mathf.Lerp(cols[i].radius, circleRad, Mathf.Min(1,4 * Time.deltaTime/ time));
            }
        }
        return 0;
    }
}
