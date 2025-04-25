using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class ColourManager : MonoBehaviour
{
   private static Color[] aCols;
   private static Color[] aOutCols;
   public static Material[] mats;

   public Material defaultSwapMat;
   public Material unlitSwapMat;
   public Material glowSwapMat;
   public Material glowUnlitSwapMat;
   
   private static readonly int Emission = Shader.PropertyToID("thecolor");
   private static readonly int ToColor2 = Shader.PropertyToID("_ToColor2");
   private static readonly int ToColor = Shader.PropertyToID("_ToColor");
   private static readonly int FromColor2 = Shader.PropertyToID("_FromColor2");
   private static readonly int FromColor = Shader.PropertyToID("_FromColor");

   [FormerlySerializedAs("cols")] public Color[] allyColours;
   [FormerlySerializedAs("colsOut")] public Color[] allyColoursOutline;
   
   private void Awake()
   {
      GS.CopyArray(ref aCols,allyColours);
      GS.CopyArray(ref aOutCols,allyColoursOutline);
      mats = new[] {  defaultSwapMat, unlitSwapMat, glowSwapMat,  glowUnlitSwapMat };
   }

   /// <summary>
   /// Instantiates too
   /// </summary>
   public static Material AllyMat(int lvlTo, bool lit = true, Color em = new Color(), int lvlFrom = 0)
   {
      int ind = 0;
      if (em.a != 0f)
      {
         ind = lit? 2 : 3;
      }
      else if(!lit)
      {
         ind = 1;
      }
      var mat = Instantiate(mats[ind]);
      mat.SetColor(FromColor, aCols[lvlFrom]);
      mat.SetColor(FromColor2, aOutCols[lvlFrom]);
      mat.SetColor(ToColor, aCols[lvlTo]);
      mat.SetColor(ToColor2, aOutCols[lvlTo]);
      if (em != new Color())
      {
         mat.SetColor(Emission, em);
      }
      return mat;
   }
}
