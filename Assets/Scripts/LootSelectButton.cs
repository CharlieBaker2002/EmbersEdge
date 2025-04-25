using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class LootSelectButton : MonoBehaviour, IClickable, IHoverable
{
    [SerializeField] public Image img;
    [FormerlySerializedAs("l")] [SerializeField] private NewLoot daddy;
    public bool en = true;
    
    public void OnClick()
    {
        if(!en) return;
        en = false;
        if (BlueprintManager.lootChoices > 0)
        {
            daddy.chosen = true;
            daddy.StopAllCoroutines();
            daddy.End();
            switch (daddy.bp.classifier)
            {
                case Blueprint.Classifier.Bonus:
                    Instantiate(daddy.bp.g, CharacterScript.CS.transform.position, Quaternion.identity, GS.FindParent(GS.Parent.loot)).name = daddy.bp.name;
                {
                    BlueprintManager.Chosen();
                    return;
                }
                case Blueprint.Classifier.Mechanism:
                {
                    MechaSuit.m.AddParts(new[] {(MechanismSO)daddy.bp}, false, true);
                    BlueprintManager.held.Add(daddy.bp);
                    if (daddy.bp.unique)
                    {
                        BlueprintManager.toDiscover.Remove(daddy.bp);
                    }
                    UIManager.i.AddBPImg(daddy.bp.s);
                    break;
                }
                default:
                    Debug.LogWarning("Implement Non Bonus/Mechnism Blueprint Chosen Functionality");
                    break;
            }
            BlueprintManager.Chosen();
        }
    }

    public void OnHover()
    {
        LeanTween.cancel(gameObject);
        LeanTween.cancel(img.gameObject);
        LeanTween.scale(gameObject, Vector3.one, 0.5f).setEaseOutCubic().setIgnoreTimeScale(true);
        LeanTween.LeanImgCol(img, Color.white, 0.4f).setEaseOutCubic().setIgnoreTimeScale(true);
    }

    public void OnDeHover()
    {
        LeanTween.cancel(gameObject);
        LeanTween.cancel(img.gameObject);
        LeanTween.scale(gameObject, Vector3.one * 0.8f, 0.5f).setEaseOutBack().setIgnoreTimeScale(true);
        LeanTween.LeanImgCol(img, Color.gray, 0.4f).setEaseOutCubic().setIgnoreTimeScale(true);
    }
}
