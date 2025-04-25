using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResearchUI : MonoBehaviour
{
    // public static ResearchUI i;
    // public Animator[] modes;
    // public TextMeshProUGUI[] costTexts;
    // public Image tick;
    // public TextMeshProUGUI mainText;
    // public TextMeshProUGUI mainText2;
    // public TextMeshProUGUI descr;
    // public TextMeshProUGUI descr2;
    // public Image[] imgs;
    // public Image[] imgs2;
    // public GameObject l2;
    // public GameObject r2;
    // public GameObject l1;
    // public GameObject r1;
    //
    // int mode = 0;
    // Blueprint current;
    // Blueprint current2;
    // Blueprint[] possibles;
    // Blueprint[] secondPossibles;
    // int ind = 0;
    // int ind2 = 0;
    // bool moving = false;
    // int[] cost = new int[4];
    //
    // public ResearchFacility facility = null;
    //
    // public void Awake()
    // {
    //     i = this;
    //     foreach(Image img in imgs)
    //     {
    //         img.preserveAspect = true;
    //     }
    //     foreach (Image img in imgs2)
    //     {
    //         img.preserveAspect = true;
    //     }
    // }
    //
    // private void OnEnable()
    // {
    //     SwapMode(0);
    // }
    //
    // private void Update()
    // {
    //     if (!l1.activeInHierarchy)
    //     {
    //         tick.color = Color.grey;
    //         return;
    //     }
    //     else if (ResourceManager.instance.CanAfford(cost, false, false) && Mathf.Max(cost)!=0)
    //     {
    //         tick.color = Color.green;
    //     }
    //     else
    //     {
    //         tick.color = Color.red;
    //     }
    // }
    //
    // private int AlterInd(int x, Blueprint[] bps)
    // {
    //     if (bps.Length == 0)
    //     {
    //         throw new System.Exception("0 Length BPS");
    //     }
    //     if (x < 0)
    //     {
    //         x += bps.Length;
    //     }
    //     if (x >= bps.Length)
    //     {
    //         x -= bps.Length;
    //     }
    //     if (x < 0 || x >= bps.Length)
    //     {
    //         return (AlterInd(x, bps));
    //     }
    //     return x;
    // }
    //
    // private void RearrangeImages(Image[] ims, bool left, bool three = false)
    // {
    //     Image[] buf = new Image[] { };
    //     GS.CopyArray(ref buf, ims);
    //     if(three == false)
    //     {
    //         if (left)
    //         {
    //             ims[0] = buf[1];
    //             ims[1] = buf[2];
    //             ims[2] = buf[3];
    //             ims[3] = buf[4];
    //             ims[4] = buf[0];
    //         }
    //         else
    //         {
    //             ims[0] = buf[4];
    //             ims[1] = buf[0];
    //             ims[2] = buf[1];
    //             ims[3] = buf[2];
    //             ims[4] = buf[3];
    //         }
    //     }
    //     else
    //     {
    //         if (left)
    //         {
    //             ims[1] = buf[2];
    //             ims[2] = buf[3];
    //             ims[3] = buf[1];
    //         }
    //         else
    //         {
    //             ims[1] = buf[3];
    //             ims[2] = buf[1];
    //             ims[3] = buf[2];
    //         }
    //     }
    // }
    //
    // public void SetCost(int[] c)
    // {
    //     string s;
    //     for (int i = 0; i < 4; i++)
    //     {
    //         switch (i)
    //         {
    //             case 0:
    //                 s = "White: ";
    //                 break;
    //             case 1:
    //                 s = "Green: ";
    //                 break;
    //             case 2:
    //                 s = "Blue: ";
    //                 break;
    //             default:
    //                 s = "Red: ";
    //                 break;
    //         }
    //         if (i >= c.Length)
    //         {
    //             cost[i] = 0;
    //             costTexts[i].text = "";
    //         }
    //         else
    //         {
    //             if (c[i] == 0)
    //             {
    //                 cost[i] = 0;
    //                 costTexts[i].text = "";
    //             }
    //             else
    //             {
    //                 cost[i] = c[i];
    //                 costTexts[i].text = s + c[i].ToString();
    //             }
    //         }
    //     }
    // }
    //
    // private void RefreshDetails()
    // {
    //     if(mode == 0)
    //     {
    //         SetCost(current.cost);
    //     }
    //     else if(mode == 1)
    //     {
    //         if (current2 == null)
    //         {
    //             SetCost(new int[] { });
    //         }
    //         else
    //         {
    //             SetCost(current2.cost);
    //         }
    //     }
    //     else if(mode == 2)
    //     {
    //         if(current == current2)
    //         {
    //             descr.text = current.name + ": " + current.description;
    //             descr2.text = "Cannot upgrade with one-self!";
    //             SetCost(new int[] { });
    //             return;
    //         }
    //         int[] cbuf = new int[4];
    //         GS.CopyArray(ref cbuf, current.cost);
    //         GS.AddArray(ref cbuf, current2.cost);
    //         SetCost(cbuf);
    //     }
    //     descr.text = current.name + ": " + current.description;
    //     if(current2 != null)
    //     {
    //         descr2.text = current2.name + ": " + current2.description;
    //     }
    // }
    //
    // private Blueprint[] ApplicableWeaponUpgrades(GameObject g)
    // {
    //     List<Blueprint> bs = new List<Blueprint>();
    //     foreach(Blueprint b in secondPossibles)
    //     {
    //         if(b.g == g)
    //         {
    //             bs.Add(b);
    //         }
    //     }
    //     return bs.ToArray();
    // }
    //
    // public void Move(bool left, bool top = true)
    // {
    //     if (moving || possibles.Length == 0)
    //     {
    //         return;
    //     }
    //     moving = true;
    //     if (top)
    //     {
    //         ind -= left ? -1 : 1;
    //         ind = AlterInd(ind, possibles);
    //         current = possibles[ind];
    //         var imbuf = new Image[] { };
    //         GS.CopyArray(ref imbuf, imgs);
    //         Sprite s = possibles[AlterInd(ind + 2 * (left?1:-1),possibles)].s;
    //         StartCoroutine(MoveI(imbuf, left ? -1 : 1,s));
    //         RearrangeImages(imgs, left);
    //         if(mode == 1)
    //         {
    //             var bs = ApplicableWeaponUpgrades(current.g);
    //             if(bs.Length == 0)
    //             {
    //                 for (int i = 1; i < 4; i++)
    //                 {
    //                     imgs2[i].color = Color.clear;
    //                 }
    //                 descr2.text = "No weapon upgrades currently available for this weapon...";
    //                 current2 = null;
    //             }
    //             else
    //             {
    //                 for (int i = 1; i < 4; i++)
    //                 {
    //                     imgs2[i].color = Color.white;
    //                     imgs2[i].sprite = bs[AlterInd(i - 1, bs)].s;
    //                 }
    //                 ind2 = AlterInd(1, bs);
    //                 current2 = bs[AlterInd(1, bs)];
    //             }
    //         }
    //     }
    //     else
    //     {
    //         if (mode == 1)
    //         {
    //             Blueprint[] bs = ApplicableWeaponUpgrades(current.g);
    //             ind2 -= left ? -1 : 1;
    //             ind2 = AlterInd(ind2, bs);
    //             current2 = bs[ind2];
    //             Sprite s = bs[AlterInd(ind2 + 1 * (left ? 1 : -1), bs)].s;
    //             var imbuf = new Image[3];
    //             imbuf[0] = imgs2[1];
    //             imbuf[1] = imgs2[2];
    //             imbuf[2] = imgs2[3];
    //             StartCoroutine(MoveI(imbuf, left ? -1 : 1,s));
    //             RearrangeImages(imgs2, left,true);
    //         }
    //         else
    //         {
    //             ind2 -= left ? -1 : 1;
    //             ind2 = AlterInd(ind2, secondPossibles);
    //             current2 = secondPossibles[ind2];
    //             var imbuf = new Image[] { };
    //             GS.CopyArray(ref imbuf, imgs2);
    //             Sprite s = secondPossibles[AlterInd(ind2 + 2 * (left ? 1 : -1), secondPossibles)].s;
    //             StartCoroutine(MoveI(imbuf, left ? -1 : 1,s));
    //             RearrangeImages(imgs2, left);
    //         }
    //     }
    //     RefreshDetails();
    // }
    //
    // public IEnumerator MoveI(Image[] ims, int x, Sprite next)
    // {
    //     float now = 0;
    //     float mag = ims.Length == 5 ? 1 : 5f/3f;
    //     while (Mathf.Abs(now) < mag *0.4f)
    //     {
    //         //at each timestep, adjust rot, position, scale
    //         for (int i = 0; i < ims.Length; i++)
    //         {
    //             float cur;
    //             if (ims.Length == 3)
    //             {
    //                 cur = now + Mathf.Lerp(-0.66666666f, 0.666666666f, (float)i / (ims.Length - 1));
    //             }
    //             else
    //             {
    //                 cur = now + Mathf.Lerp(-0.8f, 0.8f, (float)i / (ims.Length - 1));
    //             }
    //            
    //             if(cur > 1f)
    //             {
    //                 ims[i].sprite = next;
    //                 cur -= 2;
    //             }
    //             else if(cur < -1f)
    //             {
    //                 ims[i].sprite = next;
    //                 cur += 2;
    //             }
    //             SetTransform(ims[i].transform,cur);
    //         }
    //         now += x * Time.deltaTime * mag;
    //         yield return null;
    //     }
    //     //final slotting
    //     now = x * 0.4f * mag;
    //     for (int i = 0; i < ims.Length; i++)
    //     {
    //         float cur;
    //         if (ims.Length == 3)
    //         {
    //             cur = now + Mathf.Lerp(-0.66666666f, 0.666666666f, (float)i / (ims.Length - 1));
    //         }
    //         else
    //         {
    //             cur = now + Mathf.Lerp(-0.8f, 0.8f, (float)i / (ims.Length - 1));
    //         }
    //         if (cur > 1f)
    //         {
    //             cur -= 2;
    //         }
    //         else if(cur < -1f)
    //         {
    //             cur += 2;
    //         }
    //         SetTransform(ims[i].transform,cur);
    //     }
    //     moving = false;
    // }
    //
    // /// <summary>
    // /// x between -1 and 1
    // /// </summary>
    // private void SetTransform(Transform t, float x)
    // {
    //     t.localPosition = new Vector2(Mathf.Lerp(-800, 800, 0.5f * (1 + x)), t.localPosition.y);
    //     t.localScale = new Vector3(Mathf.Lerp(0.2f, 2, 1 - Mathf.Abs(x)), Mathf.Lerp(0.2f, 2, 1 - Mathf.Abs(x)), 1);
    //     t.rotation = Quaternion.Euler(new Vector3(0f, Mathf.Lerp(-50f, 50f, 0.5f * (1+x)),0f));
    // }
    //
    // private void ClearInfo()
    // {
    //     costTexts[0].text = "";
    //     costTexts[1].text = "";
    //     costTexts[2].text = "";
    //     costTexts[3].text = "";
    //     current = null;
    //     current2 = null;
    //     foreach (Image img in imgs)
    //     {
    //         img.sprite = null;
    //     }
    //     descr.text = "";
    //     descr2.text = "";
    // }
    //
    // private void Set()
    // {
    //     for (int i = 0; i < 5; i++)
    //     {
    //         imgs[i].sprite = possibles[AlterInd(i, possibles)].s;
    //     }
    //     ind = AlterInd(2, possibles);
    //     current = possibles[AlterInd(2, possibles)];
    //     switch (mode)
    //     {
    //         case 0:
    //             break;
    //         case 1:
    //             var bs = ApplicableWeaponUpgrades(current.g);
    //             if (bs.Length == 0)
    //             {
    //                 for (int i = 1; i < 4; i++)
    //                 {
    //                     imgs2[i].color = Color.clear;
    //                 }
    //                 descr2.text = "No weapon upgrades currently available for this weapon...";
    //                 current2 = null;
    //             }
    //             else
    //             {
    //                 for (int i = 1; i < 4; i++)
    //                 {
    //                     imgs2[i].color = Color.white;
    //                     imgs2[i].sprite = bs[AlterInd(i - 1, bs)].s;
    //                 }
    //                 ind2 = AlterInd(1, bs);
    //                 current2 = bs[AlterInd(1, bs)];
    //             };
    //             break;
    //         case 2:
    //             for (int i = 0; i < 5; i++)
    //             {
    //                 imgs2[i].sprite = secondPossibles[AlterInd(i, secondPossibles)].s;
    //             }
    //             ind2 = AlterInd(2, secondPossibles);
    //             current2 = secondPossibles[AlterInd(2, secondPossibles)];
    //             break;
    //     }
    //     RefreshDetails();
    // }
    //
    // public void SwapMode(int i = -1)
    // {
    //     if (!gameObject.activeInHierarchy)
    //     {
    //         return;
    //     }
    //     if(i != -1)
    //     {
    //         modes[mode].SetBool("On", false);
    //         ClearInfo();
    //         mode = i;
    //         modes[i].SetBool("On", true);
    //         ind = 0;
    //     }
    //     switch (i)
    //     {
    //         case 0:
    //             possibles = BlueprintManager.stashed.ToArray();
    //             //position part
    //             SetTransform(imgs2[1].transform, -0.4f);
    //             SetTransform(imgs2[3].transform, 0.4f);
    //             foreach (Image img in imgs)
    //             {
    //                 img.transform.localPosition = new Vector3(img.transform.localPosition.x, 0, 0);
    //             }
    //             l1.transform.localPosition = new Vector3(l1.transform.localPosition.x, 0, 0);
    //             r1.transform.localPosition = new Vector3(r1.transform.localPosition.x, 0, 0);
    //             mainText.transform.localPosition = new Vector3(mainText.transform.localPosition.x, 200, 0);
    //             descr.transform.localPosition = new Vector3(descr.transform.localPosition.x, -200,1);
    //             //activate UI;
    //             mainText.text = "Choose a blueprint to research...";
    //             foreach (Image img in imgs2)
    //             {
    //                 img.gameObject.SetActive(false);
    //             }
    //             l2.SetActive(false);
    //             r2.SetActive(false);
    //             mainText2.text = "";
    //             break;
    //         case 1:
    //             List<WeaponBP> boffer = new List<WeaponBP>(BlueprintManager.GetWeapons(BlueprintManager.researched));
    //             List<Blueprint> bof1 = new List<Blueprint>();
    //             List<Blueprint> bof2 = new List<Blueprint>();
    //             foreach (WeaponBP b in boffer)
    //             {
    //                 if (b.lvl == 0)
    //                 {
    //                     bof1.Add((Blueprint)b);
    //                 }
    //                 else if (b.upgraded == false)
    //                 {
    //                     bof2.Add((Blueprint)b);
    //                 }
    //             }
    //             possibles = bof1.ToArray();
    //             secondPossibles = bof2.ToArray();
    //
    //             //position part
    //             SetTransform(imgs2[1].transform, -0.66666666f);
    //             SetTransform(imgs2[3].transform, 0.666666f);
    //             foreach (Image img in imgs)
    //             {
    //                 img.transform.localPosition = new Vector3(img.transform.localPosition.x, 400, 0);
    //             }
    //             l1.transform.localPosition = new Vector3(l1.transform.localPosition.x, 400, 0);
    //             r1.transform.localPosition = new Vector3(r1.transform.localPosition.x, 400, 0);
    //             mainText.transform.localPosition = new Vector3(mainText.transform.localPosition.x, 600, 0);
    //             descr.transform.localPosition = new Vector3(descr.transform.localPosition.x, 100, 1);
    //             //activate UI;
    //             mainText.text = "Choose a weapon blueprint to enhance...";
    //             mainText2.text = "Choose a weapon upgrade blueprint to combine and consume...";
    //             for (int x = 0; x < 5; x++)
    //             {
    //                 if(x != 0 && x != 4)
    //                 {
    //                     imgs2[x].gameObject.SetActive(true);
    //                 }
    //                 else
    //                 {
    //                     imgs2[x].gameObject.SetActive(false);
    //                 }
    //             }
    //             l2.SetActive(true);
    //             r2.SetActive(true);
    //             break;
    //         case 2:
    //             List<AbilityBP> buffer = new List<AbilityBP>(BlueprintManager.GetAbilities(BlueprintManager.researched));
    //             List<Blueprint> buf1 = new List<Blueprint>();
    //             List<Blueprint> buf2 = new List<Blueprint>();
    //             foreach (AbilityBP b in buffer)
    //             {
    //                 if (b.level == 1)
    //                 {
    //                     buf2.Add((Blueprint)b);
    //                 }
    //                 if(b.level < 3)
    //                 {
    //                     buf1.Add((Blueprint)b);
    //                 }
    //             }
    //             possibles = buf1.ToArray();
    //             secondPossibles = buf2.ToArray();
    //
    //             //position part
    //             SetTransform(imgs2[1].transform, -0.4f);
    //             SetTransform(imgs2[3].transform, 0.4f);
    //             foreach (Image img in imgs)
    //             {
    //                 img.transform.localPosition = new Vector3(img.transform.localPosition.x, 400, 0);
    //             }
    //             l1.transform.localPosition = new Vector3(l1.transform.localPosition.x, 400, 0);
    //             r1.transform.localPosition = new Vector3(r1.transform.localPosition.x, 400, 0);
    //             mainText.transform.localPosition = new Vector3(mainText.transform.localPosition.x, 600, 0);
    //             descr.transform.localPosition = new Vector3(descr.transform.localPosition.x, 100, 1);
    //             //activate UI;
    //             mainText.text = "Choose an ability blueprint to enhance...";
    //             mainText2.text = "Choose another ability blueprint to combine and consume...";
    //             l2.SetActive(true);
    //             r2.SetActive(true);
    //             foreach (Image img in imgs2)
    //             {
    //                 img.gameObject.SetActive(true);
    //             }
    //            
    //             break;
    //     }
    //     if(possibles.Length == 0 || (mode != 0 && secondPossibles.Length == 0))
    //     {
    //         SetOffMode();
    //     }
    //     else
    //     {
    //         SetOnMode();
    //         Set();
    //     }
    // }
    //
    // private void SetOffMode()
    // {
    //     if (mode == 1)
    //     {
    //         mainText.text = "Research more weapon blueprints first";
    //     }
    //     else if (mode == 2)
    //     {
    //         mainText.text = "Research more ability blueprints first";
    //     }
    //     else
    //     {
    //         mainText.text = "Find more blueprints first";
    //     }
    //     mainText.transform.localPosition = new Vector2(0, 0);
    //     mainText2.text = "";
    //     for(int i = 0; i < 5; i++)
    //     {
    //         imgs[i].gameObject.SetActive(false);
    //         imgs2[i].gameObject.SetActive(false);
    //     }
    //     l1.SetActive(false); l2.SetActive(false); r1.SetActive(false); r2.SetActive(false);
    // }
    //
    // private void SetOnMode()
    // {
    //     for (int i = 0; i < 5; i++)
    //     {
    //         imgs[i].gameObject.SetActive(true);
    //     }
    //     l1.SetActive(true); r1.SetActive(true);
    // }
    //
    // public void Do()
    // {
    //     if (!ResourceManager.instance.CanAfford(cost, false, false))
    //     {
    //         return;
    //     }
    //     if(Mathf.Max(cost) == 0)
    //     {
    //         gameObject.SetActive(false);
    //         return;
    //     }
    //     float t = GS.CostValue(cost,0.01f);
    //     switch (mode)
    //     {
    //         case 0:
    //             facility.Do(t,cost,current,0);
    //             break;
    //         case 1:
    //             facility.Do(t,cost,current, 1, current2);
    //             break;
    //         case 2:
    //             facility.Do(t,cost,current, 2, current2);
    //             break;
    //     }
    //     gameObject.SetActive(false);
    // }
}
