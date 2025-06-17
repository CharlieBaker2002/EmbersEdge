using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class Extractor : Building
{
    public static List<Extractor> extractors;
    // Start is called before the first frame update
    [SerializeField] private Transform mesh;
    [SerializeField] private SpriteRenderer ring;
    protected float omega = 0f;
    [SerializeField] private Sprite[] ringSprites;
    [SerializeField] private List<EmberParticle> statics;
    [SerializeField] protected Ember emb;
    public float maxDistance = 7.5f;
    [SerializeField] ParticleSystem ps;
    protected ParticleSystem.EmissionModule em;
    private float timer;
    public EmberConnector connect;
    private int queue = 0;
    private int ringind;
    
    public override void Start()
    {
        base.Start();
        em = ps.emission;
        GS.OnNewEra += UpdateColours;
        int n = statics.Count;
        while (statics.Count > n * 0.4f)
        {
            int r = Random.Range(0, statics.Count);
            Destroy(statics[r].gameObject);
            statics.RemoveAt(r);
        }
    }

    protected override void BEnable()
    {
        extractors.Add(this);
        EnergyManager.i.CreateCableConnections();
    }
    
    protected override void BDisable()
    {
        extractors.Remove(this);
        EnergyManager.i.CreateCableConnections();
    }
    
    void UpdateColours(int era)
    {
        ring.material = GS.MatByEra(era, true, true,true);
        foreach(EmberParticle p in statics)
        {
            p.sr.material = GS.MatByEra(GS.era, true, false, true);
        }
    }


    public void StopSpinning()
    {
        LeanTween.value(gameObject, 1f, 0f, 5f).setOnUpdate((float val) =>
        {
            omega = 0.5f * val;
            em.rateOverTime = val * 40f;
        }).setEaseInSine().setOnComplete(() => omega = 0f);
    }

    // Update is called once per frame
    void Update()
    {
        mesh.Rotate(0f, 0f, omega * 360f * Time.deltaTime);
        if (queue > 0)
        {
            if (timer > 0f)
            {
                timer -= Time.deltaTime;
                return;
            }
            timer += 2 * Time.fixedDeltaTime;
            ringind = ringind.Cycle(1, ringSprites.Length-1, 0);
            ring.sprite = ringSprites[ringind];
            if (ringind == 0) queue--;
        }
    }

    public IEnumerator Animate() //1) Spin, 2) Get embers from the edge, 3) ember goes to random space, 4) activate ember particles, 5) Animate the correct part of the ring, 6) incremement map manager
    {
        LeanTween.value(gameObject, 0f, 1f, 5f).setOnUpdate(x =>
        {
            omega = 0.5f*x;
            em.rateOverTime = x * 40f;
        }).setEaseOutSine();
        yield return new WaitForSeconds(2.5f);
        
        Vector3 p = MapManager.i.ProximityData(transform.position , 0f, false).Item1;
        Vector2 v = transform.position.normalized;
        float dist = Vector2.Distance(transform.position, p);
        if (dist > maxDistance)
        {
            Debug.Log("Reached Max Distance For Extractor!");
            enabled = false;
            sr.color = Color.gray;
            yield break;
        }
        int n = Mathf.FloorToInt(Random.Range(0f, 1f) + maxDistance - dist);
        MapManager.i.ChangeMapAsync((Vector2)p + v,true);
        for (int i = 0; i < n; i++)
        {
            var e = Instantiate(emb, (Vector2)p, GS.RandRot(), GS.FindParent(GS.Parent.fx));
            e.extract = this;
            e.to = transform.position + (Vector3)Random.insideUnitCircle * 0.25f;
            yield return new WaitForSeconds(1f);
            p = MapManager.i.ProximityData(transform.position + (Vector3)Random.insideUnitCircle, 0f, false).Item1;
        }
        StopSpinning();
    }
    
    public void SetParticle(Vector3 transformPosition, Vector2 dir)
    {
        connect.ember++;
        if (connect.ember > connect.maxEmber) connect.ember = connect.maxEmber;
        queue++;
        StartCoroutine(ActivateParticlesSequence(transformPosition,dir));
        EnergyManager.i.UpdateEmber();
    }
    
    private IEnumerator ActivateParticlesSequence(Vector3 hitPos, Vector2 v)
    {
        for(int i = 0; i < 8; i++)
        {
            Vector3 m = Random.insideUnitCircle * 0.05f + (Vector2)hitPos;
            var l = statics.OrderBy(x => Vector2.SqrMagnitude(x.transform.position - m)).Take(2).ToArray();
            for (int x = 0; x < 2; x++)
            {
                l[x].Light();
                l[x].gameObject.SetActive(true);
                yield return new WaitForFixedUpdate();
                hitPos += (Vector3)v;
            }
        }
        
    }

    private void OnDisable()
    {
        extractors.Remove(this);
    }
}
