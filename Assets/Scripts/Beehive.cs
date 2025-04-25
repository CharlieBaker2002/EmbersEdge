using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;
public class Beehive : Unit
{
    [SerializeField] private List<SpriteRenderer> bees;
    private Dictionary<SpriteRenderer, BeeData> beePaths;
    [SerializeField] private float radius = 0.2f;
    [SerializeField] private float baseSpeed = 1f; 
    [SerializeField] private Vector2 midPoint;
    [SerializeField] private SpriteRenderer beePrefab;
    [SerializeField] private Finder f;
    [SerializeField] private Transform t;
    private bool brightening = false;

    [Header("Max Bees To Reproduce Up To")] [SerializeField]
    private int maxbees = 4;
    private float reproduce = 1f;
    private float time;
    
    private void Awake()
    {
        ls = GetComponent<LifeScript>();
        ls.onDamageDelegate += DamageDelgate;
        beePaths = new Dictionary<SpriteRenderer, BeeData>();
        f.OnFound += transform1 => t = transform1;
        for (int i = 0; i < Mathf.FloorToInt(ls.maxHp / 2f); i++)
        {
            MakeBee();
        }
        UpdateLSLineList();
    }

    protected override void Start()
    {
        base.Start();
        transform.localRotation = Quaternion.identity;
    }
    
    private void FixedUpdate()
    {
        radius = Mathf.Lerp(radius,Mathf.Pow(Mathf.Sin(time) + actRate, 2f),Time.fixedDeltaTime);
        foreach (SpriteRenderer s in bees)
        {
            var bee = s.transform;
            BeeData data = beePaths[s];
            data.t += Time.fixedDeltaTime * data.moveSpeed;
            if (data.t >= 1f)
            {
                data.t = 0f;
                data.startPoint= bee.localPosition; 
                data.endPoint = Random.insideUnitCircle * (0.5f * actRate);
            }
            beePaths[s] = data;
            Vector2 newPos = GS.Bez(new[] { data.startPoint,data.endPoint, midPoint - data.endPoint}, data.t);
            Vector3 p3d = new Vector3(newPos.x, newPos.y, bee.localPosition.z);
            bee.transform.up = p3d - bee.localPosition; 
            bee.localPosition = Vector2.Lerp(bee.localPosition,p3d,5f*(1 + actRate) *Time.fixedDeltaTime);
        }
        if (t)
        {
            midPoint = Vector2.Lerp(midPoint, ((Vector2)(t.position - transform.position)).normalized * radius, Time.fixedDeltaTime * 2f);
            AS.TryAddForce(((Vector2)(t.position - transform.position)).normalized * actRate, true);
            sr.transform.up = Vector2.Lerp(sr.transform.up, t.position - sr.transform.position,Time.fixedDeltaTime);
        }
    }

    protected override void Update()
    {
        base.Update();
        time += Time.deltaTime;
        reproduce -= Time.deltaTime;
        if (reproduce < 0f)
        {
            if (bees.Count < maxbees)
            {
                MakeBee();
            }
        }
    }

    void MakeBee()
    {
        reproduce = 2f + 2*bees.Count;
        UpdateLSLineList();
        SpriteRenderer newBee = Instantiate(beePrefab, transform.position, transform.rotation, transform);
        bees.Add(newBee);
        beePaths[newBee] = new BeeData
        {
            startPoint = Vector2.zero,
            endPoint = Random.insideUnitCircle * radius,
            t = 0f,
            moveSpeed = baseSpeed * Random.Range(0.8f, 1.2f)
        };
    }
    
    private void DamageDelgate(float x)
    {
        if(x>0f) return;
        if (Mathf.FloorToInt(ls.hp / 2) < Mathf.FloorToInt(ls.hp - x / 2))
        {
            if (bees.Count > 0)
            {
                Destroy(bees[0].gameObject);
                beePaths.Remove(bees[0]);
                bees.RemoveAt(0);
                if (!brightening)
                {
                    GS.QA(() =>{
                        GS.Stat(this, "stim", 4f, 1.5f);
                        GS.Stat(this, "Weak Heal", 3f, 3f);
                    }, 1);
                    brightening = true;
                    StartCoroutine(Brighten());
                }
                
            }
            UpdateLSLineList();
        }
    }
    
    private void UpdateLSLineList()
    {
        ls.dmgsrs.Clear();
        ls.dmgsrs.Add(sr);
        foreach (SpriteRenderer s in bees)
        {
            ls.dmgsrs.Add(s);
        }
        UpdateLineColour(true);
    }

    IEnumerator Brighten()
    {
        time = 0.95f;
        Light2D[] lights = GetComponentsInChildren<Light2D>();
        for(float t = 0f; t < 1f; t+= Time.deltaTime)
        {
            foreach (Light2D l in lights)
            {
                l.intensity += Time.deltaTime * 0.4f;
            }
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        MakeBee();
        Light2D o = bees[^1].GetComponent<Light2D>();
        for(float t = 0f; t < 1f; t+= Time.deltaTime)
        {
            o.intensity += 0.4f * Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(3f);
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            foreach (Light2D l in lights)
            {
                l.intensity -= Time.deltaTime * 0.2f;
            }
            o.intensity -= Time.deltaTime * 0.2f;
            yield return null;
        }
        yield return new WaitForSeconds(3f);
        brightening = false;
    }
    
    private struct BeeData
    {
        public Vector2 startPoint;
        public Vector2 endPoint;
        public float t;
        public float moveSpeed;
    }
}