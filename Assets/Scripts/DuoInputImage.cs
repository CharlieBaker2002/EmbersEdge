using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DuoInputImage : MonoBehaviour
{
      public static List<DuoInputImage> duos;
      public Image img;
      public TextMeshProUGUI txt;

      public void SetController()
      {
            txt.gameObject.SetActive(false);
            img.gameObject.SetActive(true);
      }

      public void SetText()
      {
            txt.gameObject.SetActive(true);
            img.gameObject.SetActive(false);
      }

      public static void SetAll(bool isController)
      {
            foreach (DuoInputImage c in duos)
            {
                  if (isController)
                  {
                        c.SetController();
                  }
                  else
                  {
                        c.SetText();
                  }
            }
      }
}
