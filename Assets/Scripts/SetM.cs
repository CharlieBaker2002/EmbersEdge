using System;
using UnityEngine;

public class SetM : MonoBehaviour
{
    public static SetM i;
    public static float OrbQuality = 1f;
    public static float FXQuality = 1f;
    public static float difficulty = 1f; //[1-3];
    public static bool quit = false;
    public static bool buildMechaShortcut = true;
    public static bool showKeys = true;
    public static bool quickTransition = true;

    private void Awake()
    {
        i = this;
        quit = false;
        
    }

    private void Start()
    {
        quickTransition = RefreshManager.i.QuickTeleport;
    }

    public static float Invert(float t, float coef = 1f, float reduceFrom = 1.02f)
    {
        return coef * (reduceFrom - t);
    }

    public void OnApplicationQuit()
    {
        quit = true;
    }

    public void RefreshHints()
    {
        foreach (Hint h in UIManager.i.hints)
        {
            h.doNotDisplay = false;
            h.shown = false;
        }
    }

    
    //ASSUMPTIONS: 
    //ALL ENEMIES ARE DEAD
    //THE PLAYER IS AT BASE
    //IT IS THE START OF A NEW DAY
    //ALL BUILDING OPERATIONS ARE COMPLETED
    
    //THEREFORE SAVING CAN BE CONDENSED TO:
    
    //HOW MANY ORBS DO YOU HAVE?
    //JUST SPAWN W,X,Y,Z ORBS AND SET THE PARENTS TO WHAT THEY WERE. 
    
    //YOUR MECHA SUIT COMPOSITION
    //HEALTH, ENERGY, FUEL, MUNITIONS, ENERGY CORES, SHIELD, (POSITION = VECTOR3.ZERO)
    //BLUEPRINTS
    //ALLY UNITS & LEVEL & HEALTH
    //BUILDINGS AND RESPECTIVE LEVELS / UPGRADES & AMMO? 
    
    //ITERATE THROUGH ALL ISAVEABLES, SAVE THEM
    //THEN ON PLAY, ITERATE THROUGH ALL ILOADABLES, INSTANTIATE THEM AND CALL THE LOAD FUNCTION
    
    
    
    
    void SaveTheGame()
    {
        
    }
}
