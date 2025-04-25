using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Blueprint", menuName = "ScriptableObjects/Blueprint")]
public class Blueprint : ScriptableObject
{
    public Sprite s;
    public GameObject g;
    public int[] cost = new int[4];
    public float shopCost = 0;
    public string description;
    public bool unique = false;
    public List<Blueprint> relevents;
    public enum Classifier { Mechanism, Bonus, Building, Science} //XP, Ember Core are both bonuses.
    
    //Remember, you DON'T need to buy a schematic, u just need enough mechanisms to use a combinator. But XP & cores together may enable u to purchase these out-game.
    //Remember, you DO need to buy a science to build a building upgrade. But XP & cores together may enable you to purchase these out-game.
    //In the mean-time, the researchinator may assist in buying sciences (2 x upgrade cost & duration) & the manifestor may assist in buying mechanisms (for increasing costs).
    
    //Buildings are recurring costs & have lots of upgrade costs. Mechanisms & combinators are one time pricey af costs, which may then be used to combine yo shit forever.
    
    public Classifier classifier = Classifier.Mechanism;
    
    public void SetUpInfo(TextMeshProUGUI txt, Image img)
    {
        img.sprite = s;
        txt.text = description;
    }

    public bool CheckDiscovered() // used to set up shop
    {
        if (SM.HasFile("DiscoveredBlueprints"))
        {
            string text = SM.Load<string>("DiscoveredBlueprints");
            foreach (string a in text.Split(" "))
            {
                if (a == name)
                {
                    return true;
                }
            }
        }
        else
        {
            Debug.LogError("DiscoveredBlueprints file doesnt exist yet..");
        }
        return false;
    }

    public bool CheckPurchased() //used for deckbuilding
    {
        if (SM.HasFile("PurchasedBlueprints"))
        {
            string text = SM.Load<string>("PurchasedBlueprints");
            foreach (string a in text.Split(" "))
            {
                if (a == name)
                {
                    return true;
                }
            }
        }
        else
        {
            Debug.Log("PurchasedBlueprints doesnt exist yet..");
        }
        return false;
    }

    public void DiscoveredBlueprint() //used to add to shop when first researched
    {
        if (CheckDiscovered())
        {
            return;
        }
        SM.Save<string>(SM.Load<string>("DicoveredBlueprints") + " " + name, "DiscoveredBlueprints");
    }

    public bool Purchased() //used to add to deck slot option
    {
        int embers = SM.Load<int>("Embers");
        if(embers - shopCost < 0)
        {
            return false;
        }
        SM.Save<string>(SM.Load<string>("PurchasedBlueprints") + " " + name, "PurchasedBlueprints");
        SM.Save<float>(embers - shopCost, "Embers");
        return true;
    }

}
