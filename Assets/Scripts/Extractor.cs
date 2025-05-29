using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Extractor : Building
{
    // Start is called before the first frame update
    [SerializeField] private Transform mesh;
    [SerializeField] private SpriteRenderer ring;
    private float omega = 0f;
    [SerializeField] private Sprite[] ringSprites;
    [SerializeField] private Sprite meshDefault;
    [SerializeField] private Sprite ringDefault;
    [SerializeField] private EmberParticle[] statics;
    [SerializeField] private Ember emb;
    public float maxDistance = 7.5f;
    [SerializeField] ParticleSystem ps;
    private ParticleSystem.EmissionModule em;
    
    public override void Start()
    {
        base.Start();
        em = ps.emission;
        GS.OnNewEra += i => { ring.material = GS.MatByEra(i, true, false,true); };
        StartCoroutine(Animate());
    }


    public void StopSpinning()
    {
        LeanTween.value(gameObject, 1f, 0f, 5f).setOnUpdate((float val) =>
        {
            omega = val * 360f;
            em.rateOverTime = val * 40f;
        }).setEaseInSine();
    }

    // Update is called once per frame
    void Update()
    {
        mesh.Rotate(0f, 0f, omega * Time.deltaTime);
    }

    public IEnumerator Animate() //1) Spin, 2) Get embers from the edge, 3) ember goes to random space, 4) activate ember particles, 5) Animate the correct part of the ring, 6) incremement map manager
    {
        LeanTween.value(gameObject, 0f, 1f, 5f).setOnUpdate(x =>
        {
            omega = x;
            em.rateOverTime = x * 40f;
        }).setEaseOutSine();
        yield return new WaitForSeconds(2.5f);
        
        Vector3 p = MapManager.i.ProximityData(transform.position + (Vector3)Random.insideUnitCircle, 0f, false).Item1;
        Vector2 v = (p - transform.position).normalized;
        float dist = Vector2.Distance(transform.position, p);
        if (dist > maxDistance)
        {
            Debug.Log("Reached Max Distance For Extractor!");
            enabled = false;
            sr.color = Color.gray;
            yield break;
        }

        int n = Mathf.FloorToInt(Random.Range(0f, 1f) + maxDistance - dist);
        n *= 10;
        for (int i = 0; i < n; i++)
        {
            var e = Instantiate(emb, (Vector2)p +  v.Rotated(Random.Range(-20f,20f)) * Random.Range(0.5f,1f) + Random.insideUnitCircle * 0.15f, GS.RandRot(), GS.FindParent(GS.Parent.fx));
            e.to = transform.position + (Vector3)Random.insideUnitCircle * 0.25f;
            e.onComplete += OnComplete;
            Vector3 ep = e.transform.position;
            yield return new WaitForSeconds(1f);
            Vector3 np = MapManager.i.ProximityData((ep-transform.position) * 0.5f + transform.position, 0f, false).Item1;
            //MapManager.i.MapChange(np + (ep - transform.position).normalized * 0.3f, true);
            yield return new WaitForSeconds(0.5f);
            
            p = MapManager.i.ProximityData(transform.position + (Vector3)Random.insideUnitCircle, 0f, false).Item1;
            v = (p - transform.position).normalized;
        }
        StopSpinning();
    }


    private void OnComplete()
    {
        return;
    }
}
