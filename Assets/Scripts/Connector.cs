using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Connector : MonoBehaviour
{
    [SerializeField] SpriteRenderer[] srs;
    [SerializeField] Sprite[] sprites;
    [SerializeField] UnityEngine.Rendering.Universal.Light2D l;
    [SerializeField] Rigidbody2D rb;
    [SerializeField] ParticleSystem p;
    ParticleSystem.EmissionModule ps;
    [SerializeField] GameObject Obelisk;

    readonly float tc = 0.2f;
    bool spin = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(TilemapCorruption.i.connectBase != null)
        {
            Play();
        }
      
    }

    private void Start()
    {
        ps = p.emission;
    }

    public void Play()
    {
        StartCoroutine(PlayAnim());
    }

    private void Update()
    {
        if (spin)
        {
            rb.angularVelocity += 90f * Time.deltaTime;
            if(rb.angularVelocity > 480f)
            {
                spin = false;
            }
        }
       
    }

    private IEnumerator AnimateRoom()
    {
        for(int i = 25; i < 29; i++)
        {
            srs[5].sprite = sprites[i];
            yield return new WaitForSeconds(8 * tc);
        }
    }

    private IEnumerator PlayAnim()
    {
        yield return new WaitForSeconds(0.5f);
        for(int i = 0; i < 10; i++)
        {
            UpdateSprite(i);
            yield return new WaitForSeconds(2 * tc);
        }
        yield return new WaitForSeconds(tc);

        UpdateSprite(24); //null
        StartCoroutine(AnimateRoom());

        for(int i = 10; i < 23; i++)
        {
            srs[4].sprite = sprites[i];
            l.intensity = Mathf.Lerp(l.intensity, 0.9f,0.06f);
            yield return new WaitForSeconds(tc);
            l.intensity = Mathf.Lerp(l.intensity, 0.9f, 0.06f);
            yield return new WaitForSeconds(0.5f * tc * i * 0.1f);
        }

        yield return new WaitForSeconds(1f);
        srs[4].sprite = sprites[23];
        yield return new WaitForSeconds(1f);
        spin = true;
        yield return new WaitForSeconds(3f);
        transform.LeanScale(Vector3.zero,2.5f).setEaseInElastic().setOnComplete(OnShrink);

        void UpdateSprite(int ind)
        {
            for (int s = 0; s < 4; s++)
            {
                srs[s].sprite = sprites[ind];
            }
        }
    }

    void OnShrink()
    {
        transform.position = new Vector3(-1000, -1000);
        transform.parent = GS.FindParent(GS.Parent.misc);
        ps.enabled = true;
        Instantiate(Obelisk, TilemapCorruption.i.connectBase.transform.position, Quaternion.identity, GS.FindParent(GS.Parent.misc));
        p.Play();
        StartCoroutine(Delight());
    }

    IEnumerator Delight()
    {
        while(ps.rateOverTime.constant > 0.75f)
        {
            l.intensity = Mathf.Lerp(l.intensity, 0.3f, Time.deltaTime * 3f);
            ps.rateOverTime = Mathf.Lerp(ps.rateOverTime.constant, 0f, Time.deltaTime * 0.65f);
            yield return null;
        }
        ps.enabled = false;
        Destroy(p.gameObject);
        Destroy(gameObject);
        Destroy(TilemapCorruption.i.connectBase.gameObject);
    }
}
