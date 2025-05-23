using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
public class BuildingTutorial : MonoBehaviour
{
    public TutorialText txt;
    public GameObject[] buildings;
    private int ind = 0;
    public GameObject[] enemies;
    public static bool defeated = false;
    public LifeScript ls;
    private readonly int skipKey = 0;
    private TurretScript t;
    //build a turret, build a cell

    private void Awake()
    {
        if(skipKey != 0)
        {
            txt.strs = new string[] { };
        }
    }

    //IEnumerator Start()
    //{
    //    defeated = false;
    //    IM.i.pi.Player.Portal.Disable();
    //    DM.i = GetComponent<DM>();
    //    DM.i.activeRoom = DM.i.initR[0];
    //    yield return new WaitForSeconds(0.5f);
    //    IM.i.pi.Player.Portal.Disable();
    //    if (skipKey == 0)
    //    {
    //        yield return StartCoroutine(NextStrs(new string[] { "Press B to Build", "You can build white buildings inside the white circle, green buildings in the green circle etc" }));
    //        yield return StartCoroutine(NextStrs(new string[] { "Build by left-clicking on a building icon, and then left-clicking again on a suitable position", "Protect your base by building a base turret", "Placing it infront of your base would be an ideal position" }));
    //        yield return StartCoroutine(HadBuilding("Base Turret"));
    //        t = GS.FindParent("AllyBuildings").GetComponentInChildren<TurretScript>(true);
    //        yield return StartCoroutine(NextStrs(new string[] { "Build a cell behind the base", "The cell will produce white orbs for you, one of four building blocks in Ember's Edge" }, true));
    //        NextB();
    //        yield return StartCoroutine(HadBuilding("Cell"));
    //        GS.FindParent("AllyBuildings").GetComponentInChildren<OrbManifester>().timePeriod /= 3;
    //        yield return StartCoroutine(NextStrs(new string[] { "Nice one. Your base can store resources, denoted by 'Stored Resources' on the right side of the screen", "You can also swap the resource interface to show the resources held on your person by left clicking on these resources", "Resources, dropped by enemies or manifestors (e.g. the cell) will travel towards you if you have space in your inventory", "It's important to remember that orbs will vanish after ninety seconds. Ember's Edge is all about maximising your efficiency" }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "Left click on the resources displayed on the right hand screen to display your inventory resources" }));
    //        while(ResourceManager.instance.showingGlobal == true)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "You're going to need a place to store all those new orbs generated by the cell", "Build a white store behind your base to store white resources safely" },true));
    //        NextB();
    //        yield return StartCoroutine(HadBuilding("Store White"));
    //        yield return StartCoroutine(NextStrs(new string[] { "Collect the resources produced by the cell and drop them off at base by moving into the teleporter", "When the white pylon has no more room for resources, the orbs will automatically relocate themselves to the storage" }));
    //        txt.speed = 1.5f;
    //        NextB();
    //        yield return StartCoroutine(NextStrs(new string[] { "Next you're going to build the last resource type building, the orb pylon", "Build a white orb pylon near the starting white pylon that's connected to your base", "Make sure you have enough resources by collecting the white orbs produced by the cell and dropping them off at base" }));
    //        yield return StartCoroutine(HadBuilding("Pylon White"));
    //        yield return StartCoroutine(NextStrs(new string[] { "Once it's finished being constructed, notice how when you build, there is an additional white area where you can build white buildings thanks to this new white pylon", "If you were to build in-between both these areas, the process would be much faster", "Tip: Buildings built further from pylons will be built slower" }));
    //        txt.speed = 1;
    //        yield return StartCoroutine(NextStrs(new string[] { "Resource infrastructure is not limited to white orbs: let's build a fell behind the base to produce some green orbs", "Green orbs, as well as supporting production of green type buildings, units and upgrades, enable healing... but more on that later", "Remember to drop off your resources at base to facilitate more building" }));
    //        NextB();
    //        yield return StartCoroutine(HadBuilding("Fell"));
    //        OrbManifester[] o = GS.FindParent("AllyBuildings").GetComponentsInChildren<OrbManifester>();
    //        foreach(OrbManifester om in o)
    //        {
    //            if(om.orbType == 1)
    //            {
    //                om.timePeriod /= 3;
    //            }
    //        }
    //    }
    //    else
    //    {
    //        NextB();
    //        NextB();
    //        NextB();
    //        NextB();
    //    }
    //    if(skipKey <= 1)
    //    {
    //        yield return StartCoroutine(NextStrs(new string[] { "ACTION TIME!", "Let's see how you hold up against a small enemy skirmish..."},true));
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        for (int i = -3; i < 4; i++)
    //        {
    //            Instantiate(enemies[0], new Vector3(i, 8, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //            yield return null;
    //        }
    //        while (GS.FindParent("Enemies").childCount > 0)
    //        {
    //            yield return null;
    //            if (defeated == true)
    //            {
    //                while (GS.FindParent("Enemies").childCount > 0)
    //                {
    //                    Destroy(GS.FindParent("Enemies").GetChild(0).gameObject);
    //                    yield return null;
    //                }
    //                yield return StartCoroutine(NextStrs(new string[] { "Good god... a cat on a keyboard would have performed better", "Try placing the tower in front of your base next time", "" }, true));
    //                yield return StartCoroutine(NextStrs(new string[] { "" }));
    //                ls.hp = ls.maxHp;
    //                ls.hasDied = false;
    //                defeated = false;
    //                break;
    //            }
    //        }
    //        txt.speed = 1.5f;
    //        yield return StartCoroutine(NextStrs(new string[] { "Easy right? The turret made your life easier didn't it?", "Well... let's see how you handle an actual skirmish..." }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "It's totally normal for you, the player, to die in Ember's Edge", "When you die you respawn at base, but lose all of the loot and resources you had in your inventory before you died", "But remember if your base is destroyed its GAME OVER..." }));
    //    }
    //    if (skipKey <= 2)
    //    {
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        for (int i = -3; i < 4; i += 3)
    //        {
    //            Instantiate(enemies[1], new Vector3(i, 8, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //            yield return null;
    //        }
    //        while (defeated == false)
    //        {
    //            if (GS.FindParent("Enemies").childCount == 0)
    //            {
    //                yield return StartCoroutine(NextStrs(new string[] { "WOW... guess I'll be seeing you at the next eSports convention... what are you doing in a tutorial?", "" }, true));
    //                yield return StartCoroutine(NextStrs(new string[] { "" }));
    //                break;
    //            }
    //            yield return null;
    //        }
    //        while (GS.FindParent("Enemies").childCount > 0)
    //        {
    //            Destroy(GS.FindParent("Enemies").GetChild(0).gameObject);
    //            yield return null;
    //        }
    //        ls.hp = ls.maxHp;
    //        ls.hasDied = false;
    //        defeated = false;
    //        yield return StartCoroutine(NextStrs(new string[] { "GAME OVER. Not so easy ay? You'll get em next time champ. The enemies have destroyed your base but let's continue...", "When playing Ember's Edge it's important to find a balance between bolstering your defences and your own offensive capabilities" }, true));
    //        txt.speed = 1;
    //    }
    //    if(skipKey <= 3)
    //    {
    //        NextB();
    //        NextB();
    //        NextB();
    //        NextB();
    //        NextB();
    //        yield return StartCoroutine(NextStrs(new string[] { "First let's focus on improving your own arsenal", "Build a weaponsmith and a library", "The weaponsmith and library require white, green, blue and red resources. This means they have to be placed where all colours of circle intersect", "If you can't get them to fit, try building some more pylons of different colours" }));
    //        yield return StartCoroutine(HadBuilding("Weaponsmith",true));
    //        yield return StartCoroutine(HadBuilding("Library",true));
    //        yield return StartCoroutine(NextStrs(new string[] { "Great, it can be a little difficult to position these buildings, so it's good to keep them in mind when positioning other buildings", "" },true));
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        bool cont = false;
    //        while (cont == false)
    //        {
    //            yield return null;
    //            if (Vender.weaponsmith.GetComponent<OrbMagnet>() != null)
    //            {
    //                if(Vender.weaponsmith.GetComponent<OrbMagnet>().capacity == 1)
    //                {
    //                    continue;
    //                }
    //            }
    //            if (Vender.library.GetComponent<OrbMagnet>() != null)
    //            {
    //                if (Vender.library.GetComponent<OrbMagnet>().capacity == 1)
    //                {
    //                    continue;
    //                }
    //            }
    //            cont = true;
    //        }
    //        yield return new WaitForSeconds(2f);
    //        yield return StartCoroutine(NextStrs(new string[] { "Left click on the weaponsmith to pull up the data on your weapons", "Left click on 'pistol' and choose the auto-pistol upgrade", "If you're lacking the resources, just gather some more and drop em off at base" },true));
    //        cont = false;
    //        while (Vender.weaponsmith.GetComponent<OrbMagnet>() == null && cont == false) 
    //        {
    //            for(int i = 0; i< 2; i++)
    //            {
    //                if(CharacterScript.CS.weapons.Count > i)
    //                {
    //                    if (CharacterScript.CS.weapons[i] != null)
    //                    {
    //                        if (CharacterScript.CS.weapons[i].GetComponent<WeaponLeveler>() == false)
    //                        {
    //                            cont = true;
    //                        }
    //                    }
    //                }
    //            }
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Once all the resources necessary have reached the weaponsmith, you will immediately be equipped with the new upgraded automatic pistol"},true));
    //        yield return StartCoroutine(NextStrs(new string[] { "Next we are going to upgrade an ability", "Bear with me on this one... upgrading abilities is a little bit complicated in Ember's Edge but the system allows endless replayability trying out different combinations and game-breaking fun" }));
    //        int[] b = new int[] { 0, Mathf.Max(0, 50 - ResourceManager.instance.orbs[1]), Mathf.Max(0, 20 - ResourceManager.instance.orbs[2]), 0 };
    //        SpawnManager.instance.CallSpawnOrbs(CharacterScript.CS.transform.position, b);
    //        yield return StartCoroutine(NextStrs(new string[] { "Left click on the library, select the key binding you want to upgrade (Z, X or C)", "Make sure the mode is set to 'upgrade', and not 'sell'", "Finally, click on blip" }));
    //        cont = false;
    //        while (Vender.library.GetComponent<OrbMagnet>() == null && cont == false)
    //        {
    //            for (int i =0; i < 3; i++)
    //            {
    //                if (CharacterScript.CS.spellLevels[i] > 1)
    //                {
    //                    cont = true;
    //                }
    //            }
    //            yield return null;
    //        }
    //        txt.speed = 0.5f;
    //        yield return StartCoroutine(NextStrs(new string[] { "Once all the resources necessary have reached the library, the ability you selected (via choice of key binding) will be upgraded to level two while simultaneously adding an extra ability to the same key binding!", "In this case you've added blip, which temporarily teleports you towards your mouse dealing damage in an area around you. Letting go of the key early will teleport you back faster" },true));
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        yield return StartCoroutine(NextStrs(new string[] { "Neat right? Let's test it out!", "Remember to press and hold the key binding to get the most from the ability" }));
    //        txt.speed = 1;
    //        yield return new WaitForSeconds(5f);
    //        ResourceManager.instance.ChangeFuels(100);
    //        for (int i = -3; i < 4; i++)
    //        {
    //            Instantiate(enemies[0], new Vector3(i, 8, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //            yield return null;
    //        }
    //        while (GS.FindParent("Enemies").childCount > 0)
    //        {
    //            yield return null;
    //            if (defeated == true)
    //            {
    //                while (GS.FindParent("Enemies").childCount > 0)
    //                {
    //                    Destroy(GS.FindParent("Enemies").GetChild(0).gameObject);
    //                    yield return null;
    //                }
    //                yield return StartCoroutine(NextStrs(new string[] { "Oops...", "" }, true));
    //                yield return StartCoroutine(NextStrs(new string[] { "" }));
    //                ls.hp = ls.maxHp;
    //                ls.hasDied = false;
    //                defeated = false;
    //                break;
    //            }
    //        }
    //    }
    //    else
    //    {
    //        NextB();
    //        NextB();
    //        NextB();
    //        NextB();
    //        NextB();
    //    }
    //    if (skipKey <= 4)
    //    {
    //        IM.i.pi.Player.Portal.Enable();
    //        yield return StartCoroutine(NextStrs(new string[] { "Next we're going to upgrade your defences, but first let's quickly go grab some resources from the dungeon!", "Teleport to the dungeon by holding down V" }, true));
    //        while (!PortalScript.i.inDungeon)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Go get some resources by clearing the room above", "Seeing the benefits of your upgraded weapon and ability?" }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        yield return new WaitForSeconds(5f);
    //        while (PortalScript.i.inDungeon)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Now let's spend these new resources!", "Left click on the turret and left click upgrade" }, true));
    //        while (t.GetComponentInParent<OrbMagnet>() == null && t.level == 0)
    //        {
    //            yield return null;
    //        }
    //        NextB();
    //        txt.speed = 0.5f;
    //        yield return StartCoroutine(NextStrs(new string[] { "Next let's build a healing platform to heal your buildings when they recieve damage", "If a building receieves too much damage, it is not destroyed but set inactive", "Once it receives sufficient orbs it will start working again" }, true));
    //        yield return StartCoroutine(HadBuilding("Healing Platform"));
    //        yield return StartCoroutine(NextStrs(new string[] { "Healing Platforms convert a singular green orb to a healing packet, whenever healing is needed near the building, the healing packet administers it", "Left click on the healing station to adjust its current max packets based on your needs and resource abundance", "Tip: Your base also acts as a healing station" }, true));
    //        NextB();
    //        txt.speed = 1.25f;
    //        yield return StartCoroutine(NextStrs(new string[] { "The last building we will be placing is certainly the most exciting", "It's all good going around killing baddies but why not have allies do it for you?", "Introducing the Fighter Ship Factory... build it... now" }));
    //        txt.speed = 1f;
    //        yield return StartCoroutine(HadBuilding("Fighter Ship Factory"));
    //        A0_1Building b = GS.FindParent("AllyBuildings").GetComponentInChildren<A0_1Building>();
    //        yield return StartCoroutine(NextStrs(new string[] { "Once it's built, left click on it and buy some fighter ships!" }));
    //        while (GS.FindParent("Allies").childCount == 0)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Aren't they adorable?", "If you fancied you could upgrade them in the factory to make them faster, smarter, and more deadly" }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "Set a rally point by left clicking the flag in the factory's interface and left clicking a spot to command where they should gather" }));
    //        while (Vector2.Distance(b.rallyPoint, b.transform.position) < 0.25f)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "There are two other ways to command your units", "The first is to make ALL units patrol along a horizontal line in PUSH MODE" }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "See that familiar looking flag in the bottom right corner?", "Click on the flag to swap to PUSH MODE and then left click again to drop a new push line" }));
    //        while (RallyAndPushScript.RP.isRally)
    //        {
    //            yield return null;
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Units will now patrol along this line as long as it's not out of range", "If it is out of range, they will patrol along the further possible line", "To return to RALLY MODE, click on the icon again" }, true));
    //        yield return StartCoroutine(NextStrs(new string[] { "The final mode is FOLLOW MODE", "Left click on an ally (fighter ship in this case) to add them to your group", "Allies in your group will follow you wherever you go including the dungeon!" }));
    //        while (CharacterScript.CS.groupCurrent == 0)
    //        {
    //            yield return null;
    //        }
    //        txt.speed = 0.5f;
    //        yield return StartCoroutine(NextStrs(new string[] { "You can remove allies from your group by pressing on 'Group UI' in the bottom corner and then left clicking the unit's image", "It's good practice to have allies from seperate buildings rallied to different places, but a push can be useful if defences are breached or to quickly get support to a line" }));
    //    }
    //    else
    //    {
    //        NextB();
    //        NextB();
    //    }
    //    if (skipKey <= 5)
    //    {
    //        txt.speed = 1;
    //        yield return StartCoroutine(NextStrs(new string[] { "Nicely done! You have just mastered the basics of Ember's Edge base-building", "With all these upgrades shall we try defending against the harder wave?", "Don't worry if you fail this time, it's all part of the learning process.." }));
    //        yield return StartCoroutine(NextStrs(new string[] { "" }));
    //        yield return new WaitForSeconds(1f);
    //        for (int i = -3; i < 4; i++)
    //        {
    //            Instantiate(enemies[0], new Vector3(i, 10, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //            yield return null;
    //        }
    //        for (int i = -4; i <= 4; i += 2)
    //        {
    //            Instantiate(enemies[2], new Vector3(i, 9, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //            yield return null;
    //        }
    //        yield return new WaitForSeconds(7.5f);
    //        Instantiate(enemies[1], new Vector3(0, 8, -1), Quaternion.identity, GS.FindParent("Enemies"));
    //        while (GS.FindParent("Enemies").childCount > 0)
    //        {
    //            yield return null;
    //            if (defeated == true)
    //            {
    //                while (GS.FindParent("Enemies").childCount > 0)
    //                {
    //                    Destroy(GS.FindParent("Enemies").GetChild(0).gameObject);
    //                    yield return null;
    //                }
    //                ls.hp = ls.maxHp;
    //                ls.hasDied = false;
    //                defeated = false;
    //                break;
    //            }
    //        }
    //        yield return StartCoroutine(NextStrs(new string[] { "Now you are ready to try the real thing. Remember to protect your base while you're in the dungeon with defences!" },true));
    //        yield return new WaitForSeconds(5);
    //    }
    //    SceneManager.LoadScene(0);

    //    //yield return StartCoroutine(NextStrs(new string[] { "" }));
    //}

    IEnumerator NextStrs(string[] strs, bool skip = false)
    {
        if (!skip)
        {
            if(txt.strs.Length > 0)
            {
                while (txt.txt.text != txt.strs[^1])
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);
            }
            txt.strs = strs;
        }
        else
        {
            txt.StopAllCoroutines();
            txt.strs = strs;
            txt.StartCoroutine(txt.Go());
        }
        
    }

    private IEnumerator HadBuilding(string s, bool ignoreclone = false)
    {
        while(GS.FindParent(GS.Parent.buildings).Find(s + (!ignoreclone? ("(Clone)") : "")) == null)
        {
            yield return null;
        }
    }

    private void NextB()
    {
        bool a = BM.i.UI.activeInHierarchy;
        UIManager.CloseAllUIs();
        //BM.i.buildingPrefabs.Add(buildings[ind]);
        ind++;
        if (a)
        {
            BM.i.AltUI();
        }
    }
}
