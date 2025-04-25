using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class Spell : Part
{
    private MechanismSO oLevel1 = null;
    private MechanismSO oLevel2 = null;
    private Sprite initS = null;
    private Dictionary<int, int> removeInds = new Dictionary<int, int>();
    private Sprite[] lvlSprites = new Sprite[2];
    private GameObject ability;
    [HideInInspector]
    public Sprite s;
    private int[] cost;
    protected AntiRotate ar;
    
    SpriteRenderer GetIndicatorSprite()
    {
        if (((MonoBehaviour)this).GetComponentInChildren<AbilityRangeIndicator>())
        {
            return ((MonoBehaviour)this).GetComponentInChildren<AbilityRangeIndicator>().render;
        }
        return null;
    }

    public virtual void Started(InputAction.CallbackContext ctx)
    {
        engagement = 1f;
        transform.rotation = CharacterScript.CS.transform.rotation;
        ar.enabled = false;
    }

    public virtual void Performed(InputAction.CallbackContext ctx)
    {
        cd.SetValue(CharacterScript.CS.spellmaxCDs[removeInds[MechaSuit.m.GetInstanceID()]]);
        engagement = 0f;
        ar.enabled = true;
    }

    public virtual Vector2 GetManaAndCd()
    {
        return Vector2.zero;
    }

    public virtual void Intellect(float intellect)
    {
        
    }

    public virtual void LevelUp()
    {
        
    }
    
    public override void StartPart(MechaSuit m)
    {
        base.StartPart(m);
        
        MechaSuit.abilitiesLeft--;
        ar = GetComponent<AntiRotate>();
        if (level == 0)
        {
            level = 1;
        }
        var sprite = s;
        Spell ability1 = null;
        Spell ability2 = null;
        if (oLevel1 != null)
        {
            var ab = Instantiate(oLevel1.g, ability.transform);
            ab.transform.localPosition = new Vector3(0, 0, 0);
            ability1 = ab.GetComponent<Spell>();
            this.LevelUp();
            sprite = lvlSprites[0];
            if (oLevel2 != null)
            {
                var ab2 = Instantiate(oLevel2.g, ability.transform);
                ab2.transform.localPosition = new Vector3(0, 0, 0);
                ability2 = ab2.GetComponent<Spell>();
                this.LevelUp();
                ability1.LevelUp();
                sprite = lvlSprites[1];
            }
        }
        int ind = CharacterScript.CS.NewAbility(this, ability1, ability2, sprite);
        if (!removeInds.ContainsKey(m.GetInstanceID()))
        {
            removeInds.Add(m.GetInstanceID(), ind);
        }

        //ABILITY RANGE INDICATOR
        List<SpriteRenderer> srs = new() { this.GetIndicatorSprite()};
        if (ability1 != null)
        {
            srs.Add(ability1.GetIndicatorSprite());
        }
        if (ability2 != null)
        {
            srs.Add(ability2.GetIndicatorSprite());
        }
        AbilityRangeIndicator.srs[ind] = srs.ToArray();
    }
    

    public override void StopPart(MechaSuit m)
    {
        MechaSuit.abilitiesLeft++;
        CharacterScript.CS.RemoveAbility(removeInds[m.GetInstanceID()]);
    }

    public void LevelUpDo(MechanismSO o)
    {
        if (level == 1)
        {
            initS = Copy(s);
            level = 2;
            description += " | " + o.description;
            oLevel1 = Instantiate(o);
            lvlSprites[0] = Create2(s, oLevel1.s);
            s = lvlSprites[0];
        }

        else if (level == 2)
        {
            oLevel2 = Instantiate(oLevel1);
            oLevel1 = Instantiate(o);
            level = 3;
            description += " | " + o.description;
            var spr1 = Create2(initS, oLevel2.s, false);
            var spr2 = Create2(initS, oLevel1.s, false);
            lvlSprites[1] = Create2(spr1, spr2);
            s = lvlSprites[1];
        }
        GS.TimesArray(ref cost, 2);
        GS.AddArray(ref cost, o.cost);
    }

    #region SpriteManip
    //Unity lays out the array's pixels left to right, bottom to top.
    //What do we want? We want to overlay them with some small amount of contorsion. oS on top of s1, blended.
    //Make a texture with the biggest width and height.
    //Scale out s1 and oS by whichever coef is smaller between the texture width and texture height (unless both is 1), make note of width and height.
    //Alpha blend oS onto the texture
    public Sprite Create2(Sprite s1, Sprite s2, bool noreread = true)
    {
        var targetTexture = new Texture2D((int)(Mathf.Max(s1.rect.width, s2.rect.width)), (int)Mathf.Max(s1.rect.height, s2.rect.height), TextureFormat.RGBA32, false, false);
        targetTexture.filterMode = FilterMode.Point;
        var targetPixels = targetTexture.GetPixels();
        for (int x = 0; x < targetPixels.Length; x++) targetPixels[x] = Color.clear;// default pixels are not set

        float scaleCoef = Mathf.Min(targetTexture.width / s1.rect.width, targetTexture.height / s1.rect.height);
        Vector2Int s1NewDimensions = new Vector2Int(Mathf.RoundToInt(s1.rect.width * scaleCoef), Mathf.RoundToInt(s1.rect.height * scaleCoef));
        var s1Pixels = s1.texture.GetPixels((int)s1.rect.x, (int)s1.rect.y, (int)s1.rect.width, (int)s1.rect.height); //figure out scale and dimensions of upscal
        var pixelBuffer = new List<Color>(s1Pixels);
        if (scaleCoef != 1)
        {
            float prev = 0;
            for (int i = 0; i < s1Pixels.Length; i += 1)
            {
                if (pixelBuffer.Count == targetPixels.Length)
                {
                    break;
                }
                if (prev > 1 / scaleCoef)
                {
                    prev -= (1 + (1 / (float)scaleCoef));
                    i -= 1;
                    pixelBuffer.Add(s1Pixels[i]); //upscale
                    continue;
                }
                prev += 1;
            }
        }
        s1Pixels = pixelBuffer.ToArray();

        scaleCoef = Mathf.Min(targetTexture.width / s2.rect.width, targetTexture.height / s2.rect.height);
        Vector2Int s2NewDimensions = new Vector2Int(Mathf.FloorToInt(s2.rect.width * scaleCoef), Mathf.FloorToInt(s2.rect.height * scaleCoef));
        var s2Pixels = s2.texture.GetPixels((int)s2.rect.x, (int)s2.rect.y, (int)s2.rect.width, (int)s2.rect.height); //figure out scale and dimensions of upscal
        pixelBuffer = new List<Color>(s2Pixels);
        if (scaleCoef != 1)
        {
            float prev = 0;
            for (int i = 0; i < s2Pixels.Length; i += 1)
            {
                if (pixelBuffer.Count == targetPixels.Length)
                {
                    break;
                }
                if (prev > 1 / scaleCoef)
                {
                    prev -= (1 + 1 / (float)scaleCoef);
                    pixelBuffer.Add(s2Pixels[i]); //upscale
                    i -= 1;
                    continue;
                }
                prev += 1;
            }
        }
        s2Pixels = pixelBuffer.ToArray();

        for (int i = 0; i < s1Pixels.Length; i++)
        {
            targetPixels[NextIndex(i, s1NewDimensions, new Vector2Int(targetTexture.width, targetTexture.height))] = s1Pixels[i];
        }

        for (int i = 0; i < s2Pixels.Length; i++)
        {
            Color currentCol = s2Pixels[i];
            Color blendCol = targetPixels[NextIndex(i, s2NewDimensions, new Vector2Int(targetTexture.width, targetTexture.height))];
            float blendAlpha = blendCol.a;
            float invBlendAlpha = 1f - 0.5f * blendCol.a;
            float alpha = blendAlpha + invBlendAlpha * currentCol.a;
            Color result = (blendCol * blendAlpha + currentCol * currentCol.a * invBlendAlpha) / alpha;
            result.a = alpha;
            targetPixels[NextIndex(i, s2NewDimensions, new Vector2Int(targetTexture.width, targetTexture.height))] = result;
        }

        targetTexture.SetPixels(targetPixels);
        targetTexture.Apply(false, noreread);
        Sprite newSprite = Sprite.Create(targetTexture, new Rect(new Vector2(), new Vector2(targetTexture.width, targetTexture.height)), new Vector2(), 1, 0, SpriteMeshType.FullRect);
        return newSprite;
    }

    private int NextIndex(int i, Vector2Int spriteDim, Vector2Int textureDim)
    {
        //sprite positions
        int x = i % spriteDim.x;
        int y = Mathf.FloorToInt(i / spriteDim.x);

        return x + y * textureDim.x;
    }

    public Sprite Copy(Sprite s1)
    {
        var targetTexture = new Texture2D((int)s1.rect.width, (int)s1.rect.height, TextureFormat.RGBA32, false, false);
        targetTexture.filterMode = FilterMode.Point;
        var targetPixels = targetTexture.GetPixels();
        for (int x = 0; x < targetPixels.Length; x++) targetPixels[x] = Color.clear;// default pixels are not set

        var sourcePixels = s1.texture.GetPixels((int)s1.rect.x, (int)s1.rect.y, (int)s1.rect.width, (int)s1.rect.height);
        for (int j = 0; j < sourcePixels.Length; j++)
        {
            var source = sourcePixels[j];
            targetPixels[j] = source; //copy OG pixels
        }
        targetTexture.SetPixels(targetPixels);
        targetTexture.Apply(false, false);
        return Sprite.Create(targetTexture, new Rect(new Vector2(), new Vector2((int)s1.rect.width, (int)s1.rect.height)), new Vector2(), 1, 0, SpriteMeshType.FullRect);
    }
    #endregion

    public override bool CanAddThisPart()
    {
        return CharacterScript.CS.spellBools.Any(x => x == false);
    }

}
