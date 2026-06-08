using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public class RefreshManager : MonoBehaviour
{
    //THIS SCRIPT IS USED FOR SOME IENUMERATORS THAT NEED OFFLOADING
    //IT IS ALSO USED FOR DEVELOPMENT PURPOSES
    public static RefreshManager i;
    [SerializeField] ResearchUI rui;
    [SerializeField] private CameraScript cam;

    public bool CASUALNOTREALTIME = true;
    [Header("Development Variables")]
    public bool STARTSEQUENCE = true;
    [Space(5)] 
    public float STANDARDTIME = 0.9f;
    [Space(5)]
    public bool SPAWNTESTMODE = false;
    [Tooltip("Show the next-wave forecast (Directors + activity) BEFORE a dungeon run, for testing base defence.")]
    public bool TESTINGDEFENCE = false;
    [Space(5)]
    public int STARTDUNGEON = 1;
    public bool REVEALALLROOMS = false;
    [Space(5)]
    public bool LOSSPROTECTION = false;
    public bool DAMAGEPROTECTION = false;
    [Space(5)] 
    public bool DAILYORBBOUNTY = false;
    [Space(5)]
    public bool IGNOREEFFICIENCY = false;
    [Space(5)]
    public float DIFFICULTY = 3;
    [Space(5)]
    public bool ARENAMODE = false;

    [Space(5)] public bool INSTASPAWN = false;
    

    [Space(5)] public bool QuickTeleport = false;

    public static bool twentyFixFrame = false;
    private int count;
    
    //Set GS variables
    [Space(20)] [SerializeField] private GameObject manager;
    [SerializeField] private SpawnManager sp;
    [SerializeField] private GameObject portal;
    [SerializeField] Volume v;

    private void Awake()
    {
        GS.Manager = manager;
        GS.spawn = sp;
        GS.portal = portal;
        i = this;
        SoulHarvester.shs = new List<SoulHarvester>[4] {new List<SoulHarvester>(), new List<SoulHarvester>(), new List<SoulHarvester>(), new List<SoulHarvester>()};
        //rui.Awake();
        Building.buildings = new List<Building>();
        SpawnManager.day = 0;
        SpawnManager.daySinceNewEra = 0;
        GS.Manager = GameObject.FindGameObjectWithTag("Manager");
        GS.spawn = GS.Manager.GetComponent<SpawnManager>();
        LeanTween.init(3000);
        SpawnManager.instance = GS.spawn;
        GS.portal = GameObject.FindGameObjectWithTag("Portal");
        GS.OnNewEra = null;
        GS.isRaidPhase = false;
        GS.era = 0;
        GS.bounds = GS.Manager.GetComponent<Collider2D>();
        EmbersEdge.currentCores = 1;
        EmbersEdge.warmUpTime = 10;
        OrbScript.tot = 0;
        OrbManager.allOrbs = new List<OrbScript>();
        Jet.jets = new List<Jet>();
        DashPump.pumps = new List<DashPump>();
        Omnimove.omnimoves = new List<Omnimove>();
        AbilityRangeIndicator.srs = new SpriteRenderer[][] { new SpriteRenderer[] { }, new SpriteRenderer[] { }, new SpriteRenderer[] { } };
        AbilityRangeIndicator.turnOns = new bool[] { false, false, false };
        EnergyPart.energies = new List<EnergyPart>();
        EnergyPart.fuels = new List<EnergyPart>();
        DefensePart.hps = new List<SpriteRenderer>();
        DefensePart.shields = new List<SpriteRenderer>();
        DefensePart.shieldRegens = new List<SpriteRenderer>();
        MechanismSO.ns = new Dictionary<string, int>();
        LeanTween.reset();
        SetM.difficulty = DIFFICULTY;
        WiggleBossProj1.speed = 1f;
        Fish.f = new List<Fish>();
        Wheel.wheels = new List<Wheel>();
        Vessel.mat = Instantiate(Resources.Load<Material>("VesselMat"));
        UIManager.keyGuides = new();
        DaddyBuildingTile.open = false;
        //MechaSuit.coreBPS = new();
        //MechaSuit.outerBPS = new List<Blueprint>();
        MechaSuit.prevs = new List<(string, int)>();
        MechaSuit.boostsLeft = 3;
        MechaSuit.abilitiesLeft = 3;
        //cam.locked = true;
        CharacterScript.dead = false;
        MapManager.OnUpdateMap = () => { };
        Finder.turretsOn = IGNOREEFFICIENCY;
        Melee.melees = new List<Melee>();
        MechaSuit.lastlife = false;
        EmbersEdge.EEExplodeEvent = () => { };
        FXWormhole.i = null;
        Vessel.vessels = new List<Vessel>();
        DuoInputImage.duos = new List<DuoInputImage>();
        MechaSuit.level = 1;
        Fan.fans = new List<Fan>();
        Phasor.phasors = new List<Phasor>();
        Phasor.mitigators = new List<Phasor>();
        Copter.copters = new List<Copter>();
        Copter.coptersAvailable = 0;
        Extractor.extractors = new List<Extractor>();
        EmberCannon.ecs = new List<EmberCannon>();
        GS.qutting = false;
        Application.quitting += () => GS.qutting = true;
        EmberCable.lostjobs = new List<List<EmberConnector>>();
        EnergyManager.toBeBuilt = new List<Constructor>();
        EnergyManager.constructors = new List<Constructor>();
        SoulGenerator.gs = new List<SoulGenerator>();
        EEIcon.icons = new List<EEIcon>();
        PortalScript.goingHomeNow = false;
       
        //Set the bloom to white

        v.sharedProfile.TryGet(out Bloom bl);
        bl.tint.Override(Color.white);
        bl.intensity.Override(4f);
        bl.threshold.Override(1.04f);
        Accelerator.accels = new List<Accelerator>();
        if (STARTSEQUENCE)
        {
            cam.transform.position = new Vector3(1000f, 1000f, -10f);
        }
        else
        {
            cam.transform.position = new Vector3(0f, 0f, -10f);
        }
        
        // Frame-rate cap. vSync MUST be 0: when it's > 0 the display drives the rate and
        // Application.targetFrameRate is ignored, so the build would run at the monitor's refresh
        // rate (120/144Hz...) instead of our cap. We aim for 60 everywhere; in a player build the
        // Update monitor below settles on a stable 30 if the machine can't actually hold 60.
        QualitySettings.vSyncCount = 0;
        ApplyFrameRate(60);
    }

    // ---- Frame-rate cap: aim for 60, fall back to a stable 30 if the machine can't hold it ----
    int targetFps = 60;

    void ApplyFrameRate(int fps)
    {
        targetFps = fps;
        Application.targetFrameRate = fps;
    }

#if !UNITY_EDITOR
    float fpsAccum;        // summed unscaled frame-times in the current sampling window
    int   fpsFrames;       // frames counted in the current window
    float fpsGrace = 4f;   // skip the first seconds (scene-load spikes would false-trigger)
    int   slowStrikes;     // consecutive sub-target windows before we commit to 30

    void Update()
    {
        if (targetFps != 60) return;                       // one-way downgrade; nothing to watch at 30
        if (fpsGrace > 0f) { fpsGrace -= Time.unscaledDeltaTime; return; }

        // Unscaled time: the game drives Time.timeScale during transitions, so scaled dt would lie.
        fpsAccum += Time.unscaledDeltaTime;
        fpsFrames++;
        if (fpsAccum < 2f) return;

        float avgFps = fpsFrames / fpsAccum;
        fpsAccum = 0f; fpsFrames = 0;

        if (avgFps < 50f) { if (++slowStrikes >= 3) ApplyFrameRate(30); }   // ~6s sustained sub-50 -> 30
        else slowStrikes = 0;
    }
#endif

    private void Start()
    {
        GS.OnNewEra += OnNewEra;
    }

    private void OnNewEra(int era)
    {
        foreach (EEIcon e in EEIcon.icons)
        {
            e.SetColour();
        }
    }

    public void FixedUpdate()
    {
        count += 1;
        if (count == 20)
        {
            count -= 20;
            twentyFixFrame = true;
        }
        else
        {
            twentyFixFrame = false;
        }
    }

    public void ResetValues(bool publish)
    {
        if (publish)
        {
            STARTSEQUENCE = true;
            SPAWNTESTMODE = false;
            STARTDUNGEON = 1;
            REVEALALLROOMS = false;
            LOSSPROTECTION = false;
            DAMAGEPROTECTION = false;
            DAILYORBBOUNTY = false;
            STANDARDTIME = 1f;
            INSTASPAWN = false;
            QuickTeleport = false;
        }
        else
        {
            STARTSEQUENCE = false;
            REVEALALLROOMS = true;
            LOSSPROTECTION = true;
            INSTASPAWN = true;
            STANDARDTIME = 1f;
            QuickTeleport = true;
            TESTINGDEFENCE = false;
        }
        
    }
}
