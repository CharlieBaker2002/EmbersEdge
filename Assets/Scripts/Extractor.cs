using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Extractor : Building
{
    // Start is called before the first frame update
    [SerializeField] private SpriteRenderer mesh;
    [SerializeField] private SpriteRenderer ring;
    private float omega = 0f;
    private float r = 0f;
    [SerializeField] private Sprite[] meshSprites;
    [SerializeField] private Sprite[] ringSprites;
    [SerializeField] private Sprite meshDefault;
    [SerializeField] private Sprite ringDefault;
    private bool animate = false;
    private float timer;
    [SerializeField] private Transform projFrom;
    [SerializeField] private ParticleSystem ps;
    
    public override void Start()
    {
        base.Start();
        GS.OnNewEra += i => { mesh.material = ring.material = GS.MatByEra(i, true, true); };
        Spin();
    }

    public void Spin()
    {
        LeanTween.cancel(gameObject);
        LeanTween.value(gameObject, 0f, 1f, 5f).setOnUpdate((float val) =>
        {
            omega = val * 270f;
        }).setEaseInSine();
        StartCoroutine(Animate());
    }

    public void StopSpinning()
    {
        LeanTween.value(gameObject, 1f, 0f, 5f).setOnUpdate((float val) =>
        {
            omega = val * 360f;
        }).setEaseInSine().setOnComplete(() => animate = false);
    }

    // Update is called once per frame
    void Update()
    {
        mesh.transform.Rotate(0f, 0f, omega * Time.deltaTime);
    }
    
    public IEnumerator Animate()           // mesh updates 8× faster than ring
    {
        animate = true;

        const float STEP_PER_FRAME = 0.25f;   // four steps → one full “mode” cycle
        float meshClock = 0f;
        float ringClock = 0f;

        int pairEvenMesh = 0;                 // current even index for the mesh
        int pairEvenRing = 0;                 // current even index for the ring
        int meshFrameInPair = 0;              // 0 → even, 1 → odd
        int ringFrameInPair = 0;

        int meshPairs = meshSprites.Length / 2;
        int ringPairs = ringSprites.Length / 2;

        while (animate)
        {
            // Scale by rotation speed so animation speeds up / slows down with ω.
            float step = Time.deltaTime * omega / 90f;
            meshClock += step;        // full speed
            ringClock += step / 8f;   // 8× slower

            /* ---------- mesh ------------ */
            if (meshClock >= STEP_PER_FRAME)
            {
                meshClock -= STEP_PER_FRAME;

                // advance within the current 2-frame pair
                meshFrameInPair ^= 1;

                if (meshFrameInPair == 0)     // we just wrapped → choose a new pair
                    pairEvenMesh = 2 * Random.Range(0, meshPairs);

                mesh.sprite = meshSprites[pairEvenMesh + meshFrameInPair];
            }

            /* ---------- ring ------------ */
            if (ringClock >= STEP_PER_FRAME)
            {
                ringClock -= STEP_PER_FRAME;

                ringFrameInPair ^= 1;

                if (ringFrameInPair == 0)
                    pairEvenRing = 2 * Random.Range(0, ringPairs);

                ring.sprite = ringSprites[pairEvenRing + ringFrameInPair];
            }

            yield return null;
        }
    }
}
