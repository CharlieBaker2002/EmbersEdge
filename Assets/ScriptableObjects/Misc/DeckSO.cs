using UnityEngine;

[CreateAssetMenu(fileName = "DeckSO", menuName = "ScriptableObjects/DeckSO")]
public class DeckSO : ScriptableObject
{
    public Blueprint[] buildings;
    public Blueprint[] weapons;
    public Blueprint[] abilities;
    public Blueprint[] boosts;
    public Blueprint[] automations;

    public void SetSO(Blueprint[] bs, Blueprint[] wps, Blueprint[] abil, Blueprint[] bo, Blueprint[] autos)
    {
        buildings = bs;
        weapons = wps;
        abilities = abil;
        boosts = bo;
        automations = autos;
    }
    public void Duplicate()
    {
        for(int i = 0; i < buildings.Length; i++)
        {
            buildings[i] = Instantiate(buildings[i]);
        }
        for(int i = 0; i < weapons.Length; i++)
        {
            weapons[i] = Instantiate(weapons[i]);
        }
        for(int i = 0; i < abilities.Length; i++)
        {
            abilities[i] = Instantiate(abilities[i]);
        }
        for(int i = 0; i < boosts.Length; i++)
        {
            boosts[i] = Instantiate(boosts[i]);
        }
        for(int i = 0; i < automations.Length; i++)
        {
            automations[i] = Instantiate(automations[i]);
        }
    }
}
