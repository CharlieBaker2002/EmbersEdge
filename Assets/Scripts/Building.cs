using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using TMPro;
using UnityEngine.Events;
using Random = UnityEngine.Random;

public class Building : MonoBehaviour, IOnDeath, IClickable //functionality for rebuilding, and clicking.
{
    public List<Behaviour> buildingBehaviours = new List<Behaviour>();
    public SpriteRenderer sr;
    public GameObject groundEdit;
    public List<BaseTile> tiles = new List<BaseTile>();
    public static List<Building> buildings = new List<Building>();
    [HideInInspector]
    public GameObject UIParent;
    public Action OnClose;
    public Action OnOpen;
    public bool canOpen = true;
    public bool hasExtraParent = false;
    public Sprite icon;
    [Header("Size.X for circle physic DIAMETER")]
    public Vector2 size = Vector2.one;
    public LifeScript physic;
    public Collider2D col;
    private bool box = true;
    [Header("Times & Costs")] 
    public int builtBlasts = 2;
    public int[] rebuildCost = new int[4];
    [SerializeField] public List<EEIcon> icons = new();
    private bool subscribed = false;
    [HideInInspector]
    public int prevN;
    [HideInInspector]
    public int numIconsTrue = 0;
    private TextMeshPro numText;
    public float maxHealth = 10f;
    
    [Header("For init buildings set true")]
    public bool builtYet = false;
    
    private bool repairing;
    Action upgradeAction;

    private Action<int> numTextAction;
    
    public virtual void Start()
    {
        UIParent = Instantiate(UIManager.i.empty, transform.position, Quaternion.identity, UIManager.i.buildingsUI);
        numText = Instantiate(UIManager.i.numText, transform.position , Quaternion.identity, transform);
        numTextAction = _ => numText.color = GS.ColFromEra() * 1.25f;
        GS.OnNewEra += numTextAction;
        numTextAction.Invoke(0);
        numText.gameObject.SetActive(false);
        
        buildings.Add(this);
        UIParent.SetActive(false);
        if (!buildingBehaviours.Contains(this)) buildingBehaviours.Add(this);
        OnOpen += delegate { UIManager.CloseAllUIs(); UpdateUI(); UIParent.SetActive(true);};
        OnClose += delegate { UIParent.SetActive(false);  };
        
        if (physic != null)
        {
            box = col is BoxCollider2D;
        }
        if (!builtYet)
        {
            if (physic != null)
            {
                Destroy(physic.gameObject);
            }
            OnDeath();
        }
        else
        {
            if (physic != null)
            {
                physic.onDeaths.Add(this);
                physic.GetComponent<IClickableCarrier>().clickable = this;
            }
        }
        // Mark this building’s footprint on the grid if it already exists at game start.
        if (builtYet)
        {
            RegisterGridOccupancy();
        }
    }

    public void UpdateUI()
    {
        int alt = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            BaseTile t = tiles[i];
            if(t== null)
            {
                tiles.RemoveAt(i);
                i--;
                continue;
            }
            int ind = tiles.IndexOf(t);
            if (ind > 19) continue;
            t.transform.position = BM.i.UIspots[ind - alt].position;
            if(t.scienceRequisit)
            {
                if (BlueprintManager.sciences.Contains(t.txt.text.ToLower()))
                {
                    t.scienceRequisit = false; //Just been researched...
                    t.init = GS.ColourFromCost(t.cost);
                    t.background.color = t.init;
                }
            }
            if (t.showParam != null)
            {
                if (!t.showParam.Invoke())
                {
                    t.gameObject.SetActive(false);
                    alt++;
                    continue;
                }
            }
            t.gameObject.SetActive(true);
        }
    }

    protected void SwitchMonos(bool mode)
    {
        foreach(Behaviour beh in buildingBehaviours)
        {
            if (beh != null)
            {
                beh.enabled = mode;
            }
        }
        sr.color = mode ? Color.white : GS.ColFromEra();
        if (mode)
        {
            physic = Instantiate(Resources.Load<GameObject>(box?"Physic":"PhysicCircle"), transform.position, Quaternion.Euler(0f,0f,Random.Range(0f,360f)), transform).GetComponent<LifeScript>();
            physic.maxHp = maxHealth;
            physic.hp = maxHealth;
            physic.onDeaths.Add(this);
            col = physic.GetComponent<Collider2D>();
            if (box)
            {
                ((BoxCollider2D)col).size = size;
            }
            else
            {
                ((CircleCollider2D)col).radius = size.x * 0.5f;
            }
            physic.GetComponent<IClickableCarrier>().clickable = this;
            physic.gameObject.SetActive(true);
        }
        else
        {
            UIParent.gameObject.SetActive(false);
            repairing = true;
        }
    }

    public void AddSlot(int[] cost, string nam, Sprite spr, bool destroyOnUseP, Action act, bool science = false, Action instantAction = null, Func<bool> optionalParameter = null, Func<bool> showParameter = null, GameObject g = null)
    {
        if(g == null)
        {
            g = gameObject;
        }
        BaseTile tile = Instantiate(UIManager.i.baseTile, UIParent.transform).GetComponent<BaseTile>();
        tile.scienceRequisit = science;
        tile.Init(hasExtraParent? g.transform.parent.gameObject : g,cost, nam, spr, destroyOnUseP, act, instantAction, optionalParameter, showParameter);
        tiles.Add(tile);
    }

    //Same As AddSlot but makes it invoke upgrade() with n bursts necessary, and instantly invoke SwitchMonos(false) when clicked.
    public void AddUpgradeSlot(int[] cost, string nam, Sprite spr, bool destroyOnUseP, Action act, int n, bool science = false,
        Action instantAction = null, Func<bool> optionalParameter = null, Func<bool> showParameter = null,
        GameObject g = null)
    {
        if (g == null)
        {
            g = gameObject;
        }

        science = false;
        
        BaseTile tile = Instantiate(UIManager.i.baseTile, UIParent.transform).GetComponent<BaseTile>();
        tile.SetTextN(n);
        tile.scienceRequisit = science;
        if (instantAction == null)
        {
            instantAction = () =>
            {
                SwitchMonos(false);
                repairing = false;
            };
        }
        else
        {
            instantAction += () =>
            {
                SwitchMonos(false);
                repairing = false;
            };
        }
        tile.Init(hasExtraParent ? g.transform.parent.gameObject : g, cost, nam, spr, destroyOnUseP, () => {Upgrade(act,n);}, instantAction,
            optionalParameter, showParameter);
        tiles.Add(tile);
    }

    protected Func<bool> Science(string scienceName)
    {
        return () => BlueprintManager.sciences.Contains(scienceName);
    }

    public virtual void OnDeath()
    {
        if (UIParent.activeInHierarchy)
        {
            OnClose.Invoke();
        }
        SwitchMonos(false);
        if (builtYet)
        {
            LoadWithEEs(1, true);
        }
        else
        {
            LoadWithEEs(builtBlasts);
        }
    }

    protected virtual void Refund()
    {
        GS.OnNewEra -= numTextAction;
    }

    /// <summary>
    /// USED TO INITIATE THE BUILDING WITH EEICONS & HOLD OFF REPAIRS WITH A SINGULAR HIDDEN ONE.
    /// </summary>
    protected void LoadWithEEs(int n, bool hidden = false)
    {
        for(int i = 0; i < n; i++)
        {
            icons.Add(Instantiate(SpawnManager.instance.EEIcon,transform.position + 0.25f * size.x * (Vector3)PositionRegularly(i,n), Quaternion.identity, transform));
            if (hidden)
            {
                icons[^1].gameObject.SetActive(false);
            }
        }
        
        if (!subscribed)
        {
            subscribed = true;
            if (!hidden)
            {
                numText.text = icons.Count.ToString();
                numText.gameObject.SetActive(true);
                prevN = icons.Count;
            }
        }

        numIconsTrue = n;
        EnergyManager.i.AddBuilding(this);
    }
    
    public void RemoveIcon()
    {
        if (icons[0].gameObject.activeInHierarchy)
        {
            icons[0].StartCoroutine(icons[0].SetDone());
        }
        else
        {
            Destroy(icons[0].gameObject);
        }
        
        if (icons.Count == prevN) //This is to make only one coroutine, as this func is called multiple times
        {
            RefreshManager.i.StartCoroutine(CountDown());
        }
        
        icons.RemoveAt(0);
        if (icons.Count != 0) return;
        EmbersEdge.EEExplodeEvent -= RemoveIcon;
        subscribed = false;
        RefreshManager.i.QA(() =>
        {
            if (!builtYet)
            {
                builtYet = true;
                SwitchMonos(true);
            }
            else if(repairing)
            {
                repairing = false;
                sr.LeanSRColor( new Color(1f, 0.5f, 0.5f, 0.5f),0.2f).setEaseOutCubic();
                ResourceManager.instance.NewTask(gameObject, rebuildCost, () => SwitchMonos(true), false);
            }
            else
            {
                if (upgradeAction != null)
                {
                    upgradeAction.Invoke();
                    upgradeAction = null;
                }
                else
                {
                    Debug.LogWarning("NO UPGRADE ACTION SET");
                }
                SwitchMonos(true);
            }
        }, 0.8f);
    }

    protected void RemoveIcons()
    {
        if (subscribed)
        {
            EmbersEdge.EEExplodeEvent -= RemoveIcon;
            subscribed = false;
            for (int i = 0; i < icons.Count; i++)
            {
                Destroy(icons[i].gameObject);
                icons.RemoveAt(i);
                i--;
            }
        }
    }

    IEnumerator CountDown()
    {
        yield return new WaitForSeconds(0.2f);
        for(int i = prevN - 1; i >= icons.Count; i--)
        {
            for(float t = 0f; t < 1f; t += 3 * Time.deltaTime)
            {
                numText.color = new Color(numText.color.r,numText.color.g,numText.color.b,1f-t);
                yield return null;
            }
            numText.text = i.ToString();
            for(float t = 1f; t > 0f; t -=  3* Time.deltaTime)
            {
                numText.color = new Color(numText.color.r,numText.color.g,numText.color.b,1f-t);
                yield return null;
            }
            yield return new WaitForSeconds(0.1f);
        }
        prevN = icons.Count;
        if (prevN == 0)
        {
            numText.gameObject.SetActive(false);
        }
    }

    public virtual void OnClick()
    {
        if (enabled)
        {
            if (!UIParent.activeInHierarchy && canOpen)
            {
                OnOpen?.Invoke();
            }
            else
            {
                OnClose?.Invoke();
            }
        }
    }

    protected void Shut()
    {
        canOpen = false;
        OnClose.Invoke();
    }

    public static void CloseBuildingUIs()
    {
        foreach (Building b in buildings)
        {
            if (b.UIParent.activeInHierarchy)
            {
                b.UIParent.SetActive(false);
                b.OnClose?.Invoke();
            }
        }
    }

    public static bool CheckBuildingUIs()
    {
        foreach (Building b in buildings)
        {
            if (b.UIParent.activeInHierarchy)
            {
                return false;
            }
        }
        return true;
    }

    protected static Vector2 GetNearestEE(Transform pos)
    {
        Vector2 nearest = Vector2.zero;
        float dist = Mathf.Infinity;
        foreach (EmbersEdge ee in SpawnManager.instance.EEs)
        {
            float d = Vector2.SqrMagnitude(ee.transform.position-pos.position);
            if (!(d < dist)) continue;
            dist = d;
            nearest = ee.transform.position;
        }
        return nearest;
    }

    protected void Upgrade(Action upgradeAct, int n) //this is the action for the newtask when upgrading.
    {
        LoadWithEEs(n,false);
        if (physic != null)
        {
            Destroy(physic.gameObject);
        }
        repairing = false;
        upgradeAction = upgradeAct;
    }
    
    /// <summary>
    /// Marks the grid cells occupied by this building as filled in the GridManager.
    /// </summary>
    void RegisterGridOccupancy()
    {
        if (GridManager.i == null) return;

        Vector2Int sizeCells = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(size.x / GridManager.i.cellSize)),
            Mathf.Max(1, Mathf.RoundToInt(size.y / GridManager.i.cellSize)));

        // Anchor at the bottom‑left grid cell so the footprint matches the placement logic.
        Vector2Int anchor = GridManager.i.WorldToGrid(transform.position)
                             - new Vector2Int(sizeCells.x / 2, sizeCells.y / 2);

        GridManager.i.SetArea(anchor, sizeCells, true);
    }

    Vector2 PositionRegularly(int n, int max)
    {
        if (max == 1)
        {
            return Vector2.zero;
        }
        float angle = n * Mathf.PI * 2f / max;
        float y = Mathf.Cos(angle);
        float x = Mathf.Sin(angle);
        return new Vector2(x, y);
    }
}
