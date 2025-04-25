using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LootEvolutionButton : MonoBehaviour, IClickable, IHoverable
{
   [SerializeField] NewLoot daddy;
   private MechanismSO mine;
   [SerializeField] private Image img;
   [SerializeField] private TextMeshProUGUI txt;
   [SerializeField] private Image bg;
   private bool selected = false;
   
   public void Load(MechanismSO b)
   {
      mine = b;
      img.sprite = b.s;
      img.preserveAspect = true;
      txt.text = b.name;
   }

   public void Deselect()
   {
      selected = true;
      OnClick();
   }

   public void OnClick()
   {
      if (selected)
      {
         selected = false;
         daddy.PopulateWithMechanism((MechanismSO)daddy.bp,false);
         OnDeHover();
      }
      else
      {
         foreach (LootEvolutionButton e in daddy.evs)
         {
            if (e != this && e.selected)
            {
               e.Deselect();
            }
         }
         selected = true;
         LeanTween.cancel(bg.gameObject);
         LeanTween.LeanImgCol(bg, Color.yellow, 0.6f);
         daddy.PopulateWithMechanism(mine,false);
      }
   }

   public void OnHover()
   {
      if (!selected)
      {
         transform.SetSiblingIndex(0);
         LeanTween.cancel(gameObject);
         LeanTween.cancel(bg.gameObject);
         LeanTween.scale(gameObject, Vector3.one, 0.5f).setEaseOutElastic().setIgnoreTimeScale(true);
         LeanTween.LeanImgCol(bg, Color.gray, 0.5f);
      }
   }

   public void ResetBackground()
   {
      LeanTween.cancel(gameObject);
      LeanTween.scale(gameObject, Vector3.one * 0.8f, 0.4f).setEaseOutBack().setIgnoreTimeScale(true);
      bg.color = Color.black;
      selected = false;
   }

   public void OnDeHover()
   {
      if (!selected)
      {
         LeanTween.cancel(bg.gameObject);
         LeanTween.scale(gameObject, Vector3.one * 0.8f, 0.4f).setEaseOutBack().setIgnoreTimeScale(true);
         LeanTween.LeanImgCol(bg, Color.black, 0.4f);
      }
   }
}
