using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class ArenaManager : MonoBehaviour
{
    [SerializeField] private MarauderSO[] ens1;
    [SerializeField] private MarauderSO[] ens2;
    [SerializeField] private MarauderSO[] ens3;
    private MarauderSO[][] ens;
    [FormerlySerializedAs("wave1s")]
    [Header("Waves:")]
    [SerializeField] private Wave[] ERA_ONES;
    [FormerlySerializedAs("wave2s")] [SerializeField] private Wave[] ERA_TWOS;
    [FormerlySerializedAs("wave3s")] [SerializeField] private Wave[] ERA_THREES;
    private Wave[][] waves;
    private Wave current;
  
    [Header("Reference:")]
    public int day = 0;
    public float rad = 6f;
    public List<GameObject> enemies;
    [SerializeField] private int startFrom = 0;

    private static List<MechanismSO> prevChoices = new List<MechanismSO>();
    [SerializeField] private MechanismSO[] mandatories;
    bool hasMandatoried = false;

    public static ArenaManager i;
    
    public IEnumerator Start()
    {
        CameraScript.QuickLeanDistort(0.22f,0.9f);
        i = this;
        prevChoices = new List<MechanismSO>();
        if (startFrom > 0)
        {
            yield return new WaitForSeconds(2f);
            GS.IncrementEra();
            yield return new WaitForSeconds(3f);
            if (startFrom == 2)
            {
                GS.IncrementEra();
                yield return new WaitForSeconds(3f);
            }
        }
        CM.Message("Arena Mode",false);
        ens = new[] {ens1,ens2,ens3};
        waves = new[] {ERA_ONES,ERA_TWOS,ERA_THREES};
        yield return new WaitForSeconds(3f);
        StartCoroutine(Spawn());
    }

    private IEnumerator Spawn()
    {
        ResourceManager.instance.ChangeFuels(ResourceManager.instance.maxFuel * 0.5f);
        if (GS.era == 1 && !hasMandatoried)
        {
            hasMandatoried = true;
            MechaSuit.m.AddParts(mandatories,true);
            CharacterScript.CS.healthSlider.InitialiseSlider(17.5f);
            CharacterScript.CS.ls.Change(999f,-1);
            CharacterScript.CS.healthSlider.UpdateMax(CharacterScript.CS.ls.maxHp);
        }
        current = waves[GS.era][day];
        if (day == 9 && GS.era == 1)
        {
            PortalScript.i.Win();
        }
        int[][] subwaves = { current.subwav1, current.subwav2, current.subwav3, current.subwav4, current.subwav5, current.subwav6, current.subwav7, current.subwav8, current.subwav9};
        for (int w = 0; w < 9; w++)
        {
            if (subwaves[w] == null || subwaves[w].Length == 0)
            {
                break;
            }
            for (int i = 0; i < subwaves[w].Length; i++)
            { 
                yield return StartCoroutine(SpawnEnemies(i,subwaves[w][i]));
            }
            if (current.waits.Length > w)
            {
                yield return new WaitForSeconds(current.waits[w]);
            }
        }
        while (enemies.Count > 0)
        {
            if (enemies[0] == null)
            {
                enemies.RemoveAt(0);
            }
            yield return null;
        }

        if (day != 0)
        {
            CM.Message("Wave " + GS.ToRoman(day) + " Complete",false);
        }
   
        if (MechaSuit.lastlife)
        {
            MechaSuit.MakeHappy();
            CameraScript.i.StopShake();
            CameraScript.ZoomPermanent(CameraScript.i.correctScale,0.02f);
        }
        yield return new WaitForSeconds(2f);
        if (current.rewards.Length > 0)
        {
            Vector2Int n = current.rewardN;
            if (n == new Vector2Int(0, 0))
            {
                n = new Vector2Int(3, 1);
            }
            var bps = current.rewards;
            bps.Shuffle();
            var loots = BlueprintManager.GetLoot(new float[n.x], n.y, false);
            for (int x = 0; x < n.x; x++)
            {
                loots[x].bp = Instantiate(bps[x]);
                loots[x].bp.name = loots[x].bp.name.RemClone();
            }
            yield return new WaitForSeconds(2f);
            while (BlueprintManager.lootChoices > 0)
            {
                yield return null;
            }
        }
        
        day++;
        UIManager.i.UpdateDayText(day+1);
        if (day >= waves[GS.era].Length)
        {
            if (GS.era == 2)
            {
                yield return new WaitForSeconds(3f);
                PortalScript.i.Win();
                yield break;
            }
            GS.IncrementEra();
            day = 0;
            CM.Message("ERA " + GS.era + " COMPLETE!",false);
            foreach(Part p in MechaSuit.m.parts)
            {
                if (p.innerinner == false)
                {
                    p.StopPart(MechaSuit.m);
                    MechaSuit.ByeByePart(p);
                }
            }
            yield return new WaitForSeconds(5f);
        }
        yield return new WaitForSeconds(2f);
        StartCoroutine(Spawn());
    }

    private IEnumerator SpawnEnemies(int index, int n)
    {
        MarauderSO m = ens[GS.era][index];
        float randRot = Random.Range(0f, 2 * Mathf.PI);
        for (int i = 0; i < n; i++)
        {
            float rot = randRot + (float)i * 2*Mathf.PI / n;
            float r = rad * 0.5f + (float)index / ens[GS.era].Length;
            enemies.Add(EmbersEdge.mainCore.SpawnEnemy(m.prefab,rad * new Vector3(Mathf.Sin(rot),Mathf.Cos(rot)),false));
            MapManager.i.OnTriggerExit2D(enemies[^1].GetComponentInChildren<Collider2D>());
            
            if (current.speed != 0f)
            {
                yield return new WaitForSeconds(m.price * current.speed);
            }
        }
    }
    
    //MapManager.BeginPlace(EE);

    [System.Serializable] 
    public struct Wave
    {
        public int[] subwav1;
        public int[] subwav2;
        public int[] subwav3;
        public int[] subwav4;
        public int[] subwav5;
        public int[] subwav6;
        public int[] subwav7;
        public int[] subwav8;
        public int[] subwav9;
        [Space(5)]
        [Range(0f,3f)]
        public float speed;

        public Vector2Int rewardN;
        public float[] waits;
        public Blueprint[] rewards;
        private bool increaseSize;
    }
}
