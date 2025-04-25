using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class BM : MonoBehaviour //Building Manager
{
    public static BM i;
    public GameObject UI;
    public GameObject redBuilding;
    public List<Building> buildings; //active
    public GameObject UIPrefab;
    public Transform[] UIspots;
    [HideInInspector]
    public int[] cost = new int[4];
    private GameObject redbuildingPrefab;
    private Action<InputAction.CallbackContext> clickAction;
    private Action<InputAction.CallbackContext> escape;
    [SerializeField] Transform mainDaddyT;
    [SerializeField] GameObject backButton;
    [SerializeField] DaddyBuildingTile[] daddies;
    [SerializeField] Transform[] daddyTs;
    private BuildingTile recent;
    private Vector2 size;
    [HideInInspector]
    public bool planting = false;
    public bool added = false;
    public Action<InputAction.CallbackContext> goToDaddy;

    private void Awake()
    {
        i = this;
    }

    public void AddDaddyDel()
    {
        if (added) return;
        IM.i.pi.Player.Escape.performed += goToDaddy;
        added = true;
    }

    public void RemoveDaddyDel()
    {
        IM.i.pi.Player.Escape.performed -= goToDaddy;
        added = false;
    }

    private void Start()
    {
        IM.i.pi.Player.Build.started += _ => AltUI();
        IM.i.pi.Player.Escape.Enable();
        IM.i.pi.Player.Build.Enable();
        clickAction = delegate { TryPlace(); };
        escape = delegate { Escape(); };
        goToDaddy = context =>
        {
            IM.i.pi.Player.Escape.performed += UIManager.i.escapeDel;
            IM.i.pi.Player.Escape.performed -= goToDaddy;
            BackItUpOffDaddy(false);
        };
    }

    public void AltUI() //inefficient but few lines so meh.
    {
        if (GS.CS().InDungeon() || CharacterScript.dead)
        {
            return;
        }
        bool wasActive = UI.activeInHierarchy;
        UIManager.CloseAllUIs();
        if (!wasActive)
        {
            if (!IM.i.CActive())
            {
                IM.i.OpenCursor();
            }
            UI.SetActive(true);
            IM.i.pi.Player.Interact.Enable();
            DetermineFitDaddies();
        }
        else
        {
            IM.i.CloseCursor();
        }
    }

    public void CloseUIs() //called from uimanager
    {
        UI.SetActive(false);
        Escape(false);
        DestroyChildren();
        foreach(DaddyBuildingTile d in daddies)
        {
            d.gameObject.SetActive(false);
        }
        foreach (OrbPylon p in ResourceManager.instance.pylons)
        {
            p.lr.enabled = false;
        }
    }

    public void TurnOnPylonsByCost(int[] cost)
    {
        foreach (OrbPylon p in ResourceManager.instance.pylons)
        {
            if (cost[p.orbType] != 0)
            {
                p.lr.enabled = true;
            }
        }
    }

    public void BuildingFollowMouse(GameObject g, BuildingTile r)
    {
        recent = r;
        redbuildingPrefab = g;
        redBuilding = Instantiate(g);
        redbuildingPrefab.transform.position = new Vector3(redbuildingPrefab.transform.position.x, redbuildingPrefab.transform.position.y, 0f);
        redBuilding.GetComponent<SpriteRenderer>().color = Color.red;
        size = redBuilding.GetComponentInChildren<Building>(true).size;
        IM.i.pi.Player.Interact.performed += clickAction;
        IM.i.pi.Player.Escape.performed += escape;
        IM.i.pi.Player.Escape.performed -= UIManager.i.escapeDel;
        IM.i.pi.Player.Interact.Disable();
        planting = true;
        StartCoroutine(BuildingFollowMouse());
    }
    
    private IEnumerator BuildingFollowMouse()
    {
        yield return null;
        IM.i.pi.Player.Interact.Enable();
        Collider2D col = redBuilding.GetComponentInChildren<Collider2D>(true);
        SpriteRenderer sr = redBuilding.GetComponent<SpriteRenderer>();
        Position(redBuilding.transform);
        int[] orbs = new int[] { 0, 0, 0, 0 };
        GS.CopyArray(ref orbs, cost);
        while (redBuilding != null)
        {
            yield return new WaitForFixedUpdate();
            if (redBuilding == null)
            {
                yield break;
            }
            Position(redBuilding.transform);
            sr.color = Color.red;
            bool red = false;
            GS.CopyArray(ref orbs, cost);
            foreach (OrbPylon m in ResourceManager.instance.pylons) //update for both pylons
            {
                if (orbs[m.orbType] > 0)
                {
                    if (m.mag.initialMag)
                    {
                        if (Vector2.Distance(Vector3.zero, redBuilding.transform.position) < m.radius)                                  //check in range of magnets
                        {
                            orbs[m.orbType] = 0;
                        }
                    }
                    else if (Vector2.Distance(m.transform.position, redBuilding.transform.position) < m.radius)                                  //check in range of magnets
                    {
                        orbs[m.orbType] = 0;
                    }
                }
            }
            if (Mathf.Max(orbs) > 0)
            {
                continue;
            }
            if (!MapManager.InsideBounds(redBuilding.transform.position))
            {
                continue;
            }

            Bounds two = new Bounds(col.transform.position, col is BoxCollider2D b? b.size : ((CircleCollider2D)col).radius * 2f * Vector2.one);
            foreach (Building x in buildings)                                                                                             //check for collisions
            {
                if(x ==null) continue;
                if (x.col == null) continue;
                if (two.Intersects(new Bounds(x.transform.position, x.col is BoxCollider2D z? z.size : ((CircleCollider2D)x.col).radius * 2f * Vector2.one)))
                {
                    red = true;
                    break;
                }
            }
            if (!red)
            {
                sr.color = Color.green;
            }
        }
    }

    public void Escape(bool activateGoToDaddy = true)
    {
        StopAllCoroutines();
        if (redBuilding != null)
        {
            ResourceManager.instance.CanAfford(cost, true);
            Destroy(redBuilding);
            foreach (OrbPylon p in ResourceManager.instance.pylons)
            {
                p.lr.enabled = false;
            }
            redBuilding = null;
        }
        IM.i.pi.Player.Interact.performed -= clickAction;
        IM.i.pi.Player.Escape.performed -= escape;
        if (activateGoToDaddy)
        {
            AddDaddyDel();
        }
        planting = false;
    }
   
    private void TryPlace()
    {
        if (redBuilding == null)
        {
            return;
        }

        if (redBuilding.GetComponent<SpriteRenderer>().color == Color.green)
        {
            var ground = redBuilding.GetComponentInChildren<Building>(true).groundEdit;
            if (ground != null) ground.SetActive((true));
            foreach (FastSpriteDecompressor fsd in redBuilding.GetComponentsInChildren<FastSpriteDecompressor>(true))
            {
                fsd.enabled = true;
            }

            var bros = redBuilding.GetComponents<OrbMagnet>().Where(x=> x.typ == OrbMagnet.OrbType.Task).ToArray();
            
            redBuilding.transform.parent = GS.FindParent(GS.Parent.buildings);
            buildings.Add(redBuilding.GetComponentInChildren<Building>(true));
            redBuilding.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f);
            var SD = redBuilding.GetComponentsInChildren<SpriteDecompressor>(true);
            foreach (OrbMagnet om in bros)
            {
                if (om.typ == OrbMagnet.OrbType.Task)
                {
                    foreach (var o in bros)
                    {
                        if (o != om)
                        {
                            om.siblingTs.Add(o);
                        }
                    }
                    var building = redBuilding.GetComponentInChildren<Building>(true);
                    om.action = delegate
                    {
                        Destroy(building.GetComponent<Collider2D>());
                        building.physic.gameObject.SetActive(true);
                    };
                    foreach (var spriteDecompressor in SD)
                    {
                        spriteDecompressor.oms.Add(om);
                    }
                }
            }

            foreach (OrbMagnet om in bros)
            {
                om.enabled = true;
            }
            
            foreach (var sd in SD)
            {
                sd.enabled = true;
            }
            planting = false;
            redBuilding = null;
            IM.i.pi.Player.Interact.performed -= clickAction;
            IM.i.pi.Player.Escape.performed -= escape;
            IM.i.pi.Player.Escape.performed += UIManager.i.escapeDel;
            GS.QA(() =>
            {
                if (ResourceManager.instance.CanAfford(recent.cost, false, false))
                {
                    recent.OnClick();
                }
            }, 2);
        }
    }
    
    private void Position(Transform t)
    {
        Vector2 v2;
        if (IM.controller)
        {
            if (IM.i.CActive())
            {
                v2 = IM.i.controllerCursor.position;
            }
            else
            {
                throw new Exception("Wants controller cursor but c cursor gone!");
            }
        }
        else
        {
            v2 = IM.i.MousePosition();
        }
        if (size == Vector2.one * 2)
        {
            t.position = new Vector3(Mathf.RoundToInt(2 * v2.x) / 2f, Mathf.RoundToInt(2 * v2.y) / 2f, 0);
        }
        else
        {
            t.position = new Vector3(Mathf.RoundToInt(2 * v2.x) / 2f, Mathf.RoundToInt(2 * v2.y) / 2f, 0);
        }
    }
    
    public void SetupDaddy(DaddyBuildingTile t)
    {
        t.transform.position = mainDaddyT.transform.position;
        backButton.SetActive(true);
        foreach (DaddyBuildingTile d in daddies)
        {
            if (d != t)
            {
                d.gameObject.SetActive(false);
            }
        }
        int adjust = 0;
        for (int i = 0; i < t.buildings.Length; i++)
        {
            bool has = false;
            foreach (GameObject g in GetAllBuildings())
            {
                if (t.buildings[i] == g)
                {
                    has = true;
                    break;
                }
            }
            if (!has)
            {
                adjust--;
                continue;
            }
            var a = Instantiate(UIPrefab, UIspots[i+4 + adjust].position, Quaternion.identity, UI.transform);
            BuildingTile tile = a.GetComponent<BuildingTile>();
            Building build = t.buildings[i].GetComponentInChildren<Building>(true);
            tile.img.sprite = build.icon == null ? build.sr.sprite : build.icon;
            int[] costB = new int[4] { 0, 0, 0, 0 };
            foreach (OrbMagnet om in t.buildings[i].GetComponents<OrbMagnet>())
            {
                if (om.typ == OrbMagnet.OrbType.Task)
                {
                    costB[om.orbType] += om.capacity;
                    om.init = true;
                }
            }
            tile.cost = costB;
            tile.txt.text = t.buildings[i].name;
            tile.UpdateCost();
            tile.ChangeBackground();
            tile.buildingPrefab = t.buildings[i];
        }
    }
    
    void DetermineFitDaddies()
    {
        int pos = 0;
        foreach (DaddyBuildingTile d in daddies)
        {
            if (d.buildings.Intersect(GetAllBuildings()).FirstOrDefault() != null)
            {
                d.transform.position = daddyTs[pos].position;
                d.gameObject.SetActive(true);
                pos++;
            }
            else
            {
                d.gameObject.SetActive(false);
            }
        }
        DaddyBuildingTile.current = null;
        backButton.SetActive(false);
    }
    
    void DestroyChildren()
    {
        foreach (BuildingTile t in UI.GetComponentsInChildren<BuildingTile>(true))
        {
            Destroy(t.gameObject);
        }
    }
    
    public void BackItUpOffDaddy(bool callEscape = true) // go back
    {
        DestroyChildren();
        DetermineFitDaddies();
        added = false;
        if (callEscape)
        {
            Escape();
        }
    }
    
    private List<GameObject> GetAllBuildings()
    {
        return BlueprintManager.GetBuildings(BlueprintManager.researched).Select((x => x.g)).Union(BlueprintManager.i.defaultBuildings).ToList();
    }
}