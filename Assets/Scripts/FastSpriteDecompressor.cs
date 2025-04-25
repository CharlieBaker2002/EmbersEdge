using System.Collections;
using UnityEngine;

public class FastSpriteDecompressor : MonoBehaviour
{
    Sprite originalSprite;
    private SpriteRenderer spriteRenderer;

    private Texture2D decompressionTexture;
    private Color[] originalPixels;
    private Color[] targetPixels;
    private float maxColorValue;

    [SerializeField] float decompressionTime = 2.0f;
    private float timer = 0.0f;

    private void Start()
    {
        if (RefreshManager.i.CASUALNOTREALTIME)
        {
            decompressionTime *= 0.1f;
        }
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalSprite = spriteRenderer.sprite;

        // Create a texture from the original sprite
        decompressionTexture = new Texture2D((int)originalSprite.rect.width, (int)originalSprite.rect.height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };

        originalPixels = originalSprite.texture.GetPixels((int)originalSprite.rect.x, (int)originalSprite.rect.y, (int)originalSprite.rect.width, (int)originalSprite.rect.height);
        targetPixels = new Color[originalPixels.Length];
        ClearTargetPixels();

        CalculateMaxColorValue();

        StartCoroutine(DecompressSprite());
    }

    private void ClearTargetPixels()
    {
        for (int i = 0; i < targetPixels.Length; i++)
        {
            targetPixels[i] = new Color(0, 0, 0, 0); // Explicitly clear to transparent black
        }
    }

    private void CalculateMaxColorValue()
    {
        maxColorValue = 0f;
        foreach (var color in originalPixels)
        {
            float value = ColorToValue(color);
            if (value > maxColorValue)
            {
                maxColorValue = value;
            }
        }
    }

    private IEnumerator DecompressSprite()
    {
        while (timer < decompressionTime)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / decompressionTime);
            UpdateSprite(progress);
            yield return null;
        }
        // Restore the original settings
        spriteRenderer.sprite = originalSprite;
        Destroy(this);
    }

    private void UpdateSprite(float progress)
    {
        for (int i = 0; i < originalPixels.Length; i++)
        {
            if (Random.value < Mathf.SmoothStep(0, 1, Mathf.Pow(progress, 3)) && originalPixels[i].a > 0) // Only update non-fully transparent pixels
            {
                targetPixels[i] = originalPixels[i];
            }
        }
        decompressionTexture.SetPixels(targetPixels);
        decompressionTexture.Apply();
        spriteRenderer.sprite = Sprite.Create(decompressionTexture, new Rect(0, 0, decompressionTexture.width, decompressionTexture.height), new Vector2(0.5f, 0.5f), originalSprite.pixelsPerUnit);
    }

    private static float ColorToValue(Color color)
    {
        return color.a * (color.r + color.g + color.b) / 3f;
    }
}
