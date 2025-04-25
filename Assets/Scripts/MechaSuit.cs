using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

public class MechaSuit : MonoBehaviour
{
    public List<WeaponScript> weapons = new();
    public List<Part> parts = new();
    public static MechaSuit m; //NEVER SET TO THE MECHAPERSON
    public static List<(string, int)> prevs = new();
    public static int abilitiesLeft = 3;
    public static int boostsLeft = 3;
    private Vector3 lastPos;
    public List<Part> followers = new List<Part>();

    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] happysad;
    public static bool lastlife = false;
    private int ws;
    
    public Transform externalRingParent;
    public Transform followersParent;
    public Transform poweredParent;
    public Transform coreParent;

    public GameObject TabText;

    public static float poweredDist; 
    
    //TailCalc
    private int positions;
    private List<Vector3> lastFrames = new List<Vector3>() { };
    private int followersN;
    public static int level = 1;

    private void Awake()
    {
        m = this;
    }

    private void Update()
    { 
        for(int i = 0; i < parts.Count; i++)
        {
            parts[i].transform.localScale = Vector3.Lerp(parts[i].transform.localScale,(0.5f + parts[i].engagement * 0.5f) * Vector3.one,Time.deltaTime * 5f);
        }
        //TailCalc
        if((lastPos-GS.CS().position).sqrMagnitude < 0.01f)
        {
            return;
        }
        lastPos = GS.CS().position;
        lastFrames.Add(lastPos);
        if (lastFrames.Count > followersN + 4)
        {
            lastFrames.RemoveAt(0);
        }
    }

    private void LateUpdate()
    {
        DoFollowers();
    }

    public void ArrangePartsInRings()
    {
        var ringParts = new List<Part>[3]{new List<Part>(), new List<Part>(), new List<Part>()};
        foreach (var part in parts)
        {
            switch (part.ring)
            {
                case 0 or 10: //Added to followrs in addParts
                    continue;
                default:
                    ringParts[part.ring-1].Add(part);
                    break;
            }
        }
        float[] sizes = new float[]{0.225f + Mathf.Max(0,ringParts[0].Count - 6) * 0.05f, 0.15f + Mathf.Max(0,ringParts[1].Count - 8) * 0.05f, 0.3f + Mathf.Max(0,ringParts[2].Count - 10) * 0.05f};
        poweredDist = sizes[0] + sizes[1];
        ArrangePartsInRing(ringParts[0], sizes[0], 180f);
        ArrangePartsInRing(ringParts[1], sizes[0] + sizes[1], 0f);
        ArrangePartsInRing(ringParts[2], sizes[0] + sizes[1] + sizes[2], 0f, true);
    }

    void DoFollowers()
    {
        followersN = followers.Count;
        for (int i = 0; i < lastFrames.Count; i++)
        {
            if (i >= followers.Count) return;
            if (followers[i] != null)
            {
                followers[i].transform.position = Vector3.Lerp(followers[i].transform.position, lastFrames[i],Time.deltaTime * 2f * (1+(float)i/followersN));
            }
        }
    }

    private void ArrangePartsInRing(List<Part> ps, float radius, float initialOffset, bool faceOutwards = false)
    {
        int partCount = ps.Count;
        float angleStep = 360f / partCount;
        for (int i = 0; i < ps.Count; i++)
        {
            ps[i].transform.localPosition = CalculatePosition(initialOffset + 90f + angleStep * i,radius);
            if (faceOutwards)
            {
                ps[i].transform.localRotation = Quaternion.Euler(0f,0f,initialOffset + angleStep * i);
            }
        }
    }

    private Vector2 CalculatePosition(float angle, float radius)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius);
    }
    
    public void Start() //ACTIVATE
    {
        GS.SetParent(transform, GS.CS());
        CharacterScript.CS.SwapWeapons();
    }
    

    public void RefreshInteractions()
    {
        parts.RemoveAll(p => p == null);
        parts.ForEach(p => p.RefreshInteractions(this));
        parts.ForEach(p => p.sr.sortingLayerID = SortingLayer.NameToID("Character"));
        CharacterScript.CS.shieldSlider.InitialiseSlider(0f);
        CharacterScript.CS.shieldSlider.UpdateMax(CharacterScript.CS.ls.maxHp * 2f);
        CharacterScript.CS.healthSlider.UpdateMax(CharacterScript.CS.ls.maxHp);
        CharacterScript.CS.healthSlider.UpdateSlider(CharacterScript.CS.ls.hp);
        CharacterScript.CS.AS.mass = 0.05f + parts.Count(x=>x.Ring() != Part.RingClassifier.Follower) / 50f;
        for (int i = 0; i < 3; i++)   CharacterScript.CS.potionSlides[i].value = CharacterScript.CS.boostBools[i] == false ? 0 : 1;
        ResourceManager.instance.Refresh(false);
        ws = CharacterScript.CS.weapons.Count;
        TabText.gameObject.SetActive(ws > 1);
        CharacterScript.CS.dashesAvailable = DashPump.ReawakenPumps();
    }
    

    public static void Announce()
    {
        List<(string, int)> news = m.parts.Select(x => (x.name,x.ring)).ToList();
        var copy = news.ToArray();
        foreach (var old in prevs)
        {
            if (!news.Remove(old))
            {
                if (old.Item2 == 2)
                {
                    CM.Message("-"+old.Item1 + ": CORE Mechanism");
                }
                else if (old.Item2 <= 1)
                {
                    CM.Message("-"+old.Item1 + ": POWERED Mechanism");
                }
                else
                {
                    CM.Message("-"+old.Item1 + ": EXTERNAL Mechanism");
                }
            }
        }
        foreach (var left in news)
        {
            string adder;
            switch (left.Item2)
            {
                case 0:
                    continue;
                case 1:
                    adder = "CORE";
                    break;
                case 2:
                    adder = "POWERED";
                    break;
                case 3:
                    adder = "EXTERNAL";
                    break;
                default:
                    adder = "FOLLOWER";
                    break;
            }
            CM.Message("+" + left.Item1 + ": " + adder + " Mechanism" ,false);
        }
        prevs = copy.ToList();
    }

    public void AddParts(MechanismSO[] bps, bool permanent = false, bool temporary = false)
    {
        List<Part> newParts = new List<Part>();
        foreach (MechanismSO b in bps)
        {
            try
            {
                if (b.p.taip == Part.PartType.Ability)
                {
                    if (abilitiesLeft <= 0)
                    {
                        CM.Message(b.name + ": No slots avaiable for another ability!");
                        continue;
                    }
                }
                else if (b.p.taip == Part.PartType.Boost)
                {
                    if (boostsLeft <= 0)
                    {
                        Debug.LogWarning("No Boost Available Compadre");
                        CM.Message(b.name + ": No slots avaiable for another boost!");
                        continue;
                    }
                }

                newParts.Add(AddPart(b));
            }
            catch (Exception e)
            {
                Debug.Log(b.name + e.Message);
            }
           
        }
        if (newParts.Any(x => Part.Ring(x.taip) != Part.RingClassifier.Follower)) ArrangePartsInRings();
        newParts.ForEach(x=>x.StartPart(this));
        RefreshInteractions();
        return;
        
        Part AddPart(MechanismSO b)
        {
            var p = Instantiate(b.p, GS.CS().position, Quaternion.identity);
            if (p.Ring() == Part.RingClassifier.External)
            {
                p.transform.parent = externalRingParent;
               // p.gameObject.AddComponent<AntiRotate>();
            }
            else if (p.Ring() == Part.RingClassifier.Follower)
            {
                p.transform.parent = followersParent;
            }
            else if (p.Ring() == Part.RingClassifier.Powered)
            {
                p.transform.parent = poweredParent;
                if (p.taip != Part.PartType.Weapon)
                {
                    p.gameObject.AddComponent<AntiRotate>().local = true;
                }
            }
            else
            {
                p.transform.parent = coreParent;
                //p.gameObject.AddComponent<AntiRotate>();
            }
            p.level = b.level;
            p.transform.localRotation = Quaternion.Euler(0f,0f,0f);
            p.permanent = permanent;
            p.temporary = temporary;
            p.name = b.name;
            p.description = b.description;
            parts.Add(p);
            if (p is Spell sp)
            {
                sp.s = b.s;
            }
            if (b.p.innerinner)
            {
                p.ring = 0;
                p.sr.enabled = false;
                return p;
            }
            switch (Part.Ring(b.p.taip))
            {
                case Part.RingClassifier.Core:
                {
                    p.ring = 1;
                    break;
                }
                case Part.RingClassifier.Powered:
                    p.ring = 2;
                    break;
                case Part.RingClassifier.External:
                    p.ring = 3;
                    break;
                case Part.RingClassifier.Follower:
                    p.ring = 10;
                    followers.Add(p);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return p;
        }
    }
    
    //Invoke in each follower mechanism
    public static void UsedFollower(Part p)
    {
        p.StopPart(m);
        m.parts.Remove(p);
        m.followers.Remove(p);
        ByeByePart(p);
        Destroy(p.gameObject);
    }
    
    public static void ByeByePart(Part p)
    {
        if (p.sr.name.Contains("Main H"))
        {
            return;
        }
        var n = Instantiate(CharacterScript.CS.byebye, p.transform.position, p.transform.rotation);
        n.sprite = p.sr.sprite;
        n.transform.localScale = p.transform.lossyScale;
        p.gameObject.SetActive(false);
        LeanTween.scale(n.gameObject, Vector3.zero, 2.5f).setEaseInQuart().setOnComplete(() => Destroy(n.gameObject));
        LeanTween.move(n.gameObject, n.transform.position + (n.transform.position - GS.CS().position) * 1f  + GS.RandCircle(0.4f,0.75f), 2f).setEaseOutBack();
    }
    
    public void MurkPart(Part x, int i, bool byebye = false)
    {
        x.StopPart(this);
        if (x.ring == 10) followers.Remove(x);
        if (byebye)
        {
            ByeByePart(x);
        }
        parts.RemoveAt(i);
        Destroy(x.gameObject);
    }

    public void DieFR()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            if (!x.innerinner && !x.permanent) //destroy all but inner and permanent
            {
                MurkPart(x, i, true);
                i--;
            }
            else
            {
                x.StopPart(this);
                ByeByePart(x);
            }
        }
        enabled = false;
    }
    
    /// <summary>
    /// Called on teleport (prevents base only or dungeon only parts from continuing)
    /// </summary>
    public void RemoveTemporary()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            if (!x.temporary) continue; 
            MurkPart(x, i, true);
            i--;
        }
        RefreshInteractions();
        ArrangePartsInRings();
    }

    /// <summary>
    /// Called on vessel for clean slate.
    /// </summary>
    public void RemoveNonInnerInner()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            if (x.innerinner || x.permanent) continue;
            MurkPart(x, i, true);
            i--;
        }
        RefreshInteractions();
        ArrangePartsInRings();
    }
    
    /// <summary>
    ///  Called on first die.
    /// </summary>
    public void RemoveNonStubborn()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            if (x.stubborn || x.innerinner || x.permanent) continue;
            MurkPart(x,i,true);
            i--;
        }
        RefreshInteractions();
        ArrangePartsInRings();
    }

    /// <summary>
    /// Removes Permanents (for upgrading the MechaSuit To Next Era)
    /// </summary>
    public void RemovePerms()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            if (!x.permanent) continue;
            MurkPart(x,i,true);
            i--;
        }
    }

    public static void MakeHappy()
    {
        m.sr.sprite = m.happysad[0];
        lastlife = false;
        CameraScript.i.correctScale = 3.5f;
    }

    public static void MakeSad()
    {
        m.sr.sprite = m.happysad[1];
        lastlife = true;
        CameraScript.ZoomPermanent(2.5f, 0.02f);
    }

    public void RotatePowered(Transform t)
    {
        LeanTween.cancel(poweredParent.gameObject);
        LeanTween.rotateLocal(poweredParent.gameObject, new Vector3(0f,0f,GS.VTA(t.localPosition)), 1f).setEaseOutCubic();
    }
    public void Restart()
    {
        for(int i = 0; i < parts.Count; i++)
        {
            Part x = parts[i];
            x.gameObject.SetActive(true);
            x.StartPart(this);
        }
        RefreshInteractions();
        ArrangePartsInRings();
        ResourceManager.instance.ChangeFuels(999f);
        CharacterScript.CS.ls.Change(999f,0);
        enabled = true;
    }

    public void TPFollowers()
    {
        lastFrames.Clear();
        foreach (Part p in followers)
        {
            p.transform.position = CharacterScript.CS.transform.position +  (Vector3)Random.insideUnitCircle;
        }
    }
}