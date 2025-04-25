using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EvolutionsButton : MonoBehaviour, IHoverable
{
    [SerializeField] private RectTransform Header;
    [SerializeField] NewLoot daddy;
    
    public void OnHover()
    {
        LeanTween.cancel(gameObject);
        ((RectTransform)transform).LeanSize(new Vector2(580f, 450f),0.8f).setEaseOutQuint().setIgnoreTimeScale(true);
        daddy.select.tag = "Misc";
        GS.QA(() => {     LeanTween.cancel(daddy.select.gameObject);
            daddy.select.gameObject.LeanScale(Vector3.zero,0.5f).setEaseOutBack().setIgnoreTimeScale(true);
            LeanTween.LeanImgCol(daddy.select.img, Color.gray, 0.4f).setEaseOutCubic().setIgnoreTimeScale(true);
        }, 1);
        LoadEvolutions(false);
    }

    public void LoadEvolutions(bool reallyLoad)
    {
        foreach (LootEvolutionButton b in daddy.evs)
        {
            b.tag = "UI";
        }
        int n = daddy.bp.relevents.Count;
        if (n == 1)
        {
            daddy.evs[1].gameObject.SetActive(true);
            if (reallyLoad)
            {
                daddy.evs[1].Load((MechanismSO)daddy.bp.relevents[0]);
            }
        }
        else if (n == 2)
        {
            daddy.evs[0].gameObject.SetActive(true);
            daddy.evs[2].gameObject.SetActive(true);
            if (reallyLoad)
            { 
                daddy.evs[0].Load((MechanismSO)daddy.bp.relevents[0]);
                daddy.evs[2].Load((MechanismSO)daddy.bp.relevents[1]);
                ((RectTransform)daddy.evs[0].transform).anchoredPosition = new Vector2(150f, -225f);
                ((RectTransform)daddy.evs[2].transform).anchoredPosition = new Vector2(-150f, -225f);
            }
        }
        else if (n == 3)
        {
            for (int i = 0; i < 3; i++)
            {
                daddy.evs[i].gameObject.SetActive(true);
                daddy.evs[i].Load((MechanismSO)daddy.bp.relevents[i]);
            }
        }
        else
        {
            ((RectTransform)daddy.evs[0].transform).anchoredPosition = new Vector2(-200f, -175f);
            ((RectTransform)daddy.evs[1].transform).anchoredPosition = new Vector2(-66.66f, -208.333f);
            ((RectTransform)daddy.evs[2].transform).anchoredPosition = new Vector2(66.66f, -241.66f);
            for (int i = 0; i < 4; i++)
            {
                daddy.evs[i].gameObject.SetActive(true);
                daddy.evs[i].Load((MechanismSO)daddy.bp.relevents[i]);
            }
        }
    }
    

    void ResetEvolutions()
    {
        foreach (LootEvolutionButton e in daddy.evs)
        {
            e.gameObject.SetActive(false);
            e.ResetBackground();
        }
    }

 
    public void OnDeHover()
    {
        LeanTween.cancel(gameObject);
        LeanTween.cancel(daddy.select.gameObject);
        daddy.select.gameObject.LeanScale(Vector3.one * 0.8f, 0.6f).setEaseOutQuint().setIgnoreTimeScale(true);
        daddy.select.tag = "UI";
        foreach (LootEvolutionButton b in daddy.evs)
        {
            b.tag = "Misc";
        }
        daddy.PopulateWithMechanism((MechanismSO)daddy.bp, false);
        ((RectTransform)transform).LeanSize(new Vector2(580f, 100f), 0.6f).setEaseOutQuint().setIgnoreTimeScale(true).setOnComplete(() => ResetEvolutions());
    }
}
