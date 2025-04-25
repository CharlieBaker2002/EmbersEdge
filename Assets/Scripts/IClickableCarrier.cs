using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IClickableCarrier : MonoBehaviour, IClickable
{
   public MonoBehaviour clickable = null;
   public void OnClick()
   {
      if (clickable == null) return;
      (clickable as IClickable)?.OnClick();
   }
}
