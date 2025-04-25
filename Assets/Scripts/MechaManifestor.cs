using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MechaManifestor : Building, IClickable
{
    public static MechaManifestor i;
    [SerializeField] private MechanismSO[] defaults;
    private bool hit;
    
    void Awake()
    {
        i = this;
    }
    
    public override void Start()
    { 
        MechaSuit.m.AddParts(Baron.current.inits, true);
        MechaSuit.m.AddParts(defaults,true);
        
        MechaSuit.m.RefreshInteractions();
        
        MechaSuit.m.Start();
        this.QA((() =>
        {
            if (!hit)
            {
                OnTriggerEnter2D(null);
            }
        }),2f);
    }


    static bool FullCheckCanBuild()
    {
        if (PortalScript.i.inDungeon) return false;
        if (Vessel.vessels.Any(x => x.engaged)) return false;
        return Vessel.vessels.Count != 0;
    }
    
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (Time.timeSinceLevelLoad < 90f)
        {
            hit = true;
            if (PortalScript.CanPortal())
            {
                this.QA(() =>UIManager.MakeKey("V", new Vector2(-2f, 0f), "Teleport To Dungeon"),0.1f);
            }
            if (FullCheckCanBuild())
            {
                this.QA(() =>UIManager.MakeKey("B", new Vector2(2f, 0f),"Build Defences & Infrastructure"),0.1f);
            }
        }
    }
    
    private void OnTriggerExit2D(Collider2D collision)
    {
        UIManager.DeleteKey("V");
        UIManager.DeleteKey("B");
        UIManager.DeleteKey("SELECT");
    }

    public override void OnClick()
    {
    }
}
