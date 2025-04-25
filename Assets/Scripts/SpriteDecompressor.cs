using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpriteDecompressor : MonoBehaviour
{
    Sprite orig;
    SpriteRenderer sr;

    Color[] targetPixels;
    Color[] copy;
    float max;

    Texture2D T;
    Material mat;

    int N;
    int n;
    int buf;

    public List<OrbMagnet> oms = new List<OrbMagnet>();
    private int[] omValues;

    private void OnEnable()
    {
        sr = GetComponent<SpriteRenderer>();
        orig = sr.sprite;
        sr.sprite = null;
        mat = sr.material;
        sr.material = (Material)Resources.Load("Sprite-Lit-Default");

        // Create a new texture with the same dimensions as the original sprite
        T = new Texture2D((int)orig.rect.width, (int)orig.rect.height, TextureFormat.RGBA32, false, false)
        {
            filterMode = FilterMode.Point
        };

        omValues = new int[oms.Count];
        for (int i = 0; i < oms.Count; i++)
        {
            omValues[i] = (int)GS.CostFromIndex(oms[i].orbType, oms[i].capacity);
            N += omValues[i];
        }
    }

    private void Start()
    {
        copy = orig.texture.GetPixels((int)orig.rect.x, (int)orig.rect.y, (int)orig.rect.width, (int)orig.rect.height);
        targetPixels = new Color[copy.Length];
        for (int x = 0; x < targetPixels.Length; x++) targetPixels[x] = Color.clear;
        DetermineMax();
        StartCoroutine(UpdateSpriteCoroutine());
    }

    private void UpdateSprite(float x)
    {
        for (int i = 0; i < copy.Length; i++)
        {
            if (GS.Chance(100 * Mathf.Lerp(Mathf.Pow((max - ColToVal(copy[i])) / max, 5), 1f, Mathf.Pow(x, 7))))
            {
                targetPixels[i] = copy[i];
            }
        }
    }
    
    IEnumerator UpdateSpriteCoroutine()
    {
        while (true)
        {
            buf = 0;
            for (int i = 0; i < omValues.Length; i++)
            {
                if (oms[i] == null)
                {
                    buf += omValues[i];
                }
                else
                {
                    buf += (int)GS.CostFromIndex(oms[i].orbType, oms[i].orbs.Count);
                }
            }

            if (buf == N)
            {
                Destroy(this);
                sr.sprite = orig;
                sr.material = mat;
                yield break;
            }

            bool updated = false;
            for (int x = n; x < buf; x++)
            {
                UpdateSprite((float)x / N);
                updated = true;
            }

            if (updated)
            {
                T.SetPixels(targetPixels);
                yield return null;
                T.Apply();
                sr.sprite = Sprite.Create(T, new Rect(0, 0, T.width, T.height), new Vector2(0.5f, 0.5f), orig.pixelsPerUnit);
            }

            n = buf;
            yield return new WaitForSeconds(0.16666667f); // Adjust the wait time based on performance needs
        }
    }

    private void DetermineMax()
    {
        max = 0;
        foreach (var t in copy)
        {
            float z = ColToVal(t);
            if (z > max)
            {
                max = z;
            }
        }
    }

    private static float ColToVal(Color c)
    {
        return c.a * (c.r + c.g + c.b) / 3f;
    }
}