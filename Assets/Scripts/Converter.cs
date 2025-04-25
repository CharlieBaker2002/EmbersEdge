using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Converter : Part
{
    private static readonly int EmptyFuel = Animator.StringToHash("emptyFuel");
    private static readonly int FullEnergy = Animator.StringToHash("fullEnergy");
    public float defaultEffiency = 1f;
    public float efficiency = 2f;
    private bool converting;
    [SerializeField] private Animator anim;
    public float power;

    [SerializeField] private bool cheatStart = false; //this is for the main hub (which has fuel energy and converter)

    public override void StartPart(MechaSuit mecha)
    {
        if(cheatStart)
        {
            return;
        }
        anim.enabled = true;
        enabled = true;
    }

    public override void RefreshInteractions(MechaSuit m)
    {
        efficiency = defaultEffiency * 1f;
        //Work on this...
    }

    public override void StopPart(MechaSuit m)
    {
        if (cheatStart)
        {
            return;
        }
        enabled = false;
    }

    private void Update()
    {
        converting = EnergyPart.energies.Any(x=>x.energy < x.maxEnergy) && EnergyPart.fuels.Any(x=>x.energy > 0);
        if (converting)
        {
            engagement = 1f;
            ConverterChangeEnergy(efficiency * 0.2f * power * Time.deltaTime);
            ConverterChangeFuel(-power * Time.deltaTime * 0.199f);
        }
        else if(!cheatStart)
        {
            engagement = 0f;
        }
    }

    private void ConverterChangeFuel(float change)
    {
        foreach (EnergyPart p in EnergyPart.fuels)
        {
            change = p.UpdateEnergy(change);
            if (change == 0)
            {
                return;
            }
        }
    }

    public void TurnConvertingOn()
    {
        converting = true;
    }

    public void TurnConvertingOff()
    {
        converting = false;
    }

    void ConverterChangeEnergy(float change)
    {
        foreach (EnergyPart j in EnergyPart.energies)
        {
            change = j.UpdateEnergy(change);
            if (change == 0f)
            {
                return;
            }
        }
    }
}
