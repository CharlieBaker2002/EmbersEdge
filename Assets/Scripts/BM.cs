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
    private Building rbb;
    public List<Building> buildings; //active
    public GameObject UIPrefab;
    public Transform[] UIspots;
    [HideInInspector] public int[] cost = new int[4];
    private GameObject redbuildingPrefab;
    private Action<InputAction.CallbackContext> clickAction;
    private Action<InputAction.CallbackContext> escape;
    [SerializeField] Transform mainDaddyT;
    [SerializeField] GameObject backButton;
    [SerializeField] DaddyBuildingTile[] daddies;
    [SerializeField] Transform[] daddyTs;
    private BuildingTile recent;
    // Stores the original colour for each building sprite so it can be restored later
    private readonly Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();
    private bool sampled = false;
    [HideInInspector] public bool planting = false;
    public bool added = false;
    public Action<InputAction.CallbackContext> goToDaddy;

    [SerializeField] Vector2Int gridSize = new Vector2Int(1,1); // size in cells
    Vector2Int anchorCell;                                      // where we’re hovering
    
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

    public void ChangeBuildingColour(bool on)
    {
        return;
        if (!on && sampled) return;
        sampled = !on;
        if(!on && buildings[0].GetComponentInChildren<SpriteRenderer>().color == new Color(1f,1f,1f,0.1f)) return; //if already off return
        if(on && buildings[0].GetComponentInChildren<SpriteRenderer>().color == new Color(1f,1f,1f,1f)) return; //if already on return
        foreach (SpriteRenderer s in buildings.SelectMany(x => x.hasExtraParent ? x.transform.parent.GetComponentsInChildren<SpriteRenderer>()  : x.gameObject.GetComponentsInChildren<SpriteRenderer>()))
        {
            if (!on)
            {
                // Remember the sprite's existing colour the first time we dim it
                if (!originalColors.ContainsKey(s))
                {
                    originalColors.Add(s, s.color);
                }
                s.color = new Color(1f, 1f, 1f, 0.2f);
            }
            else
            {
                // Revert to the stored colour, or white if we somehow never stored it
                if (originalColors.TryGetValue(s, out var original))
                {
                    s.color = original;
                }
            }
        }

        // Once colours are restored we can clear the cache
        if (on)
        {
            originalColors.Clear();
        }
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
        foreach (DaddyBuildingTile d in daddies)
        {
            d.gameObject.SetActive(false);
        }
    }
    
    public void BuildingFollowMouse(GameObject g, BuildingTile r)
    {
        recent = r;
        redbuildingPrefab = g;
        redBuilding = Instantiate(g);
        rbb = redBuilding.GetComponentInChildren<Building>(true);
        redbuildingPrefab.transform.position = new Vector3(redbuildingPrefab.transform.position.x,
            redbuildingPrefab.transform.position.y, 0f);
        foreach(SpriteRenderer s in redBuilding.GetComponentsInChildren<SpriteRenderer>(true))
        {
            s.color = new Color(0f, 0f, 0f, 0.2f);
        }
        GridManager.i.ActivateGrid();
        ChangeBuildingColour(false);
        Vector2 bSize = rbb.size;
        gridSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(bSize.x / GridManager.i.cellSize)),
            Mathf.Max(1, Mathf.RoundToInt(bSize.y / GridManager.i.cellSize)));
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
        while (redBuilding != null)
        {
            yield return new WaitForFixedUpdate();
            if (redBuilding == null) yield break;

            // Snap to grid and preview footprint
            Position(redBuilding.transform);

            // int[] orbs = new int[4];
            // GS.CopyArray(ref orbs, cost);

            bool gridClear   = GridManager.i.AreaClear(anchorCell, gridSize);
            bool boundClear = true;
            for (int x = -1; x <= 1; x+=2)
            {
                for(int y = -1; y <= 1; y+=2)
                {
                    boundClear = MapManager.InsideBounds(redBuilding.transform.position + new Vector3(x*rbb.size.x,y*rbb.size.y) * 0.49f);
                    if (boundClear == false) break;
                }
                if(boundClear == false) break;
            }
            // colour overlay & sprite tint
            GridManager.i.PreviewArea(anchorCell, gridSize, gridClear&&boundClear);
        }
    }

    public void Escape(bool activateGoToDaddy = true)
    {
        StopAllCoroutines();
        GridManager.i.DeactivateGrid();
        if (redBuilding != null)
        {
            ResourceManager.instance.CanAfford(cost, true);
            Destroy(redBuilding);
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

        if (!GridManager.i.AreaClear(anchorCell, gridSize))
            return;

        GridManager.i.SetArea(anchorCell, gridSize, true);
        GridManager.i.DeactivateGrid();
        
        foreach(SpriteRenderer s in redBuilding.GetComponentsInChildren<SpriteRenderer>(true))
        {
            s.color = new Color(1f,1f,1f,1f);
        }

        var ground = rbb.groundEdit;
        if (ground != null) ground.SetActive((true));
        foreach (FastSpriteDecompressor fsd in redBuilding.GetComponentsInChildren<FastSpriteDecompressor>(true))
        {
            fsd.enabled = true;
        }

        var bros = redBuilding.GetComponents<OrbMagnet>().Where(x => x.typ == OrbMagnet.OrbType.Task).ToArray();

        redBuilding.transform.parent = GS.FindParent(GS.Parent.buildings);
        buildings.Add(rbb);
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
                om.action = delegate
                {
                    if (rbb == null) return;
                    if (rbb.TryGetComponent<Collider2D>(out var col))
                    {
                        Destroy(rbb.GetComponent<Collider2D>());
                    }
                    rbb.physic.gameObject.SetActive(true);
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


    private void Position(Transform t)
        {
            Vector2 worldMouse = IM.controller
                ? IM.i.controllerCursor.position
                : IM.i.MousePosition();
            anchorCell = GridManager.i.WorldToGrid(worldMouse);                 
            Vector3 snapped = GridManager.i.GridToWorld(anchorCell) + new Vector3(-0.125f + 0.5f*rbb.size.x, -0.125f + 0.5f * rbb.size.y, 0f); //0.375 for 1, 0.875 for 2
            redBuilding.transform.position = snapped;
            bool clear = GridManager.i.AreaClear(anchorCell, gridSize);
            GridManager.i.PreviewArea(anchorCell, gridSize, clear);
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