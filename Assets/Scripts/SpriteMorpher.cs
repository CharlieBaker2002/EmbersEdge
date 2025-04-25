using System.Collections.Generic;
using UnityEngine;

public class SpriteMorpher : MonoBehaviour
{
    public Sprite startSprite;
    public Sprite endSprite;
    private SpriteRenderer sr;

    private Texture2D morphTexture;
    private Material originalMaterial;

    public List<OrbMagnet> oms = new List<OrbMagnet>();
    private int[] orbValues;
    private int totalOrbValue;
    private int currentOrbValue;

    private int[] sortedPixelIndices;
    private Color[] morphPixels;

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();
       // originalMaterial = sr.material;
       // sr.material = new Material(Shader.Find("Sprites/Default"));
        startSprite = sr.sprite;
        
        InitializeTextures();
        InitializeOrbValues();
        InitializeIntensityBasedMorph();
    }

    private void InitializeTextures()
    {
        int width = Mathf.Max((int)startSprite.rect.width, (int)endSprite.rect.width);
        int height = Mathf.Max((int)startSprite.rect.height, (int)endSprite.rect.height);

        morphTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };

        sr.sprite = Sprite.Create(morphTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f),
            startSprite.pixelsPerUnit);

        morphPixels = new Color[width * height];
    }

    private void InitializeOrbValues()
    {
        orbValues = new int[oms.Count];
        totalOrbValue = 0;
        for (int i = 0; i < oms.Count; i++)
        {
            orbValues[i] = (int)GS.CostFromIndex(oms[i].orbType, oms[i].capacity);
            totalOrbValue += orbValues[i];
        }
    }

    private void InitializeIntensityBasedMorph()
    {
        Color[] startPixels = startSprite.texture.GetPixels((int)startSprite.rect.x, (int)startSprite.rect.y,
            (int)startSprite.rect.width, (int)startSprite.rect.height);

        sortedPixelIndices = new int[startPixels.Length];
        for (int i = 0; i < startPixels.Length; i++)
            sortedPixelIndices[i] = i;

        System.Array.Sort(sortedPixelIndices, (a, b) => 
            CalculateBrightness(startPixels[a]).CompareTo(CalculateBrightness(startPixels[b])));
    }

    private float CalculateBrightness(Color color)
    {
        return (color.r + color.g + color.b) / 3f;
    }

    private void LateUpdate()
    {
        UpdateOrbValues();
        float morphFactor = (float)currentOrbValue / totalOrbValue;
        UpdateMorph(morphFactor);

        if (morphFactor >= 1f)
        {
         //   sr.material = originalMaterial;
            sr.sprite = endSprite;
            Destroy(this);
        }
    }

    private void UpdateOrbValues()
    {
        currentOrbValue = 0;
        for (int i = 0; i < oms.Count; i++)
        {
            if (oms[i] != null)
            {
                currentOrbValue += (int)GS.CostFromIndex(oms[i].orbType, oms[i].orbs.Count);
            }
        }
    }

    private void UpdateMorph(float factor)
    {
        Color[] startPixels = startSprite.texture.GetPixels((int)startSprite.rect.x, (int)startSprite.rect.y,
            (int)startSprite.rect.width, (int)startSprite.rect.height);
        Color[] endPixels = endSprite.texture.GetPixels((int)endSprite.rect.x, (int)endSprite.rect.y,
            (int)endSprite.rect.width, (int)endSprite.rect.height);

        int pixelsToMorph = Mathf.FloorToInt(factor * sortedPixelIndices.Length);

        for (int i = 0; i < sortedPixelIndices.Length; i++)
        {
            int index = sortedPixelIndices[i];
            morphPixels[index] = i < pixelsToMorph ? endPixels[index] : startPixels[index];
        }

        morphTexture.SetPixels(morphPixels);
        morphTexture.Apply();
    }
}