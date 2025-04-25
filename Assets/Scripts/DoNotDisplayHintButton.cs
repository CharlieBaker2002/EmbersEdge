using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DoNotDisplayHintButton : MonoBehaviour, IClickable
{
   [SerializeField] private Hint h;
   [SerializeField] private Sprite[] sprs;
   [SerializeField] private Image img;

   public void OnClick()
   {
      h.doNotDisplay = !h.doNotDisplay;
      img.sprite = sprs[h.doNotDisplay ? 1 : 0];
      //Save Or Delete the fact that the hint's donotdisplay is true
   }
}
