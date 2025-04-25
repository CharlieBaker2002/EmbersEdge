using System.Collections;
using UnityEngine;

public class OnDieTutorial : MonoBehaviour, IOnDeath
{
    public LifeScript ls;
    public Room[] rs;

    public void OnDeath()
    {
        ls.hp = ls.maxHp;
        float t = -100f;
        Room a = null;
        foreach(Room r in rs)
        {
            if(r.hasCharacter >0f && r.hasCharacter > t)
            {
                a = r;
                t = r.hasCharacter;
            }
        }
        a.ResetRoom();
        foreach(Transform x in GS.FindParent(GS.Parent.enemies))
        {
            Destroy(x.gameObject);
        }
        StartCoroutine(Put(a.safeSpawn.position,a));
        
    }
    private IEnumerator Put(Vector3 pos, Room r)
    {
        for(int i = 0; i < 5; i++)
        {
            CharacterScript.CS.transform.position = pos;
            CharacterScript.CS.AS.Stop();
            yield return null;
        }

        int id = CharacterScript.CS.CreateShield(10f);
        RefreshManager.i.QA(() =>CharacterScript.CS.RemoveShield(id),5f);
        r.OnEnter();
    }
}
