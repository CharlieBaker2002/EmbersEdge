using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Crop : Building
{

    [SerializeField] Sprite[] sprs;
    [SerializeField] SpriteRenderer frameSR;

    [SerializeField] Sprite[] emsprs;
    [SerializeField] SpriteRenderer emsr;

    [SerializeField] int[] cost;
    [SerializeField] int[] reward;

    int multiplier = 1;
    int multiplierBuffer = 1;

    float timer = 1f;
    int ind = 0;
    System.Action act;
    OrbMagnet mag;

    [SerializeField] TextMeshPro txt;

    // Start is called before the first frame update
    public override void Start()
    {
        base.Start();
        OnOpen += () => txt.gameObject.SetActive(true);
        OnClose += () => txt.gameObject.SetActive(false);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Increase Operations", null, false, IncreaseShift);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Decrease Operations", null, false, DecreaseShift);
        act = () =>
        {
            if (mag != null) { return; }
            emsr.sprite = emsprs[0];
            GS.CallSpawnOrbs(transform.position, GS.TimesArray(reward,multiplier), null);
            multiplier = multiplierBuffer;
            if(multiplier == 0)
            {
                return;
            }
            ResourceManager.instance.NewTask(gameObject, GS.TimesArray(cost, multiplier), null, false);
            mag = GetComponent<OrbMagnet>();
        };
        SpawnManager.instance.OnNewDay += act;

        Invoke(nameof(WaitForFirstMag), 1f);
        DecreaseShift();
    }

    private void WaitForFirstMag()
    {
        ResourceManager.instance.NewTask(gameObject, GS.TimesArray(cost, multiplier), null, false);
        mag = GetComponent<OrbMagnet>();
    }

    private void IncreaseShift()
    {
        if(multiplierBuffer == 0)
        {

        }

        multiplierBuffer += 1;
        if (multiplierBuffer > 3)
        {
            multiplierBuffer = 3;
        }
        txt.text = multiplierBuffer.ToString() + " / 3";
    }

    private void DecreaseShift()
    {
        multiplierBuffer -= 1;
        if (multiplierBuffer < 0)
        {
            multiplierBuffer = 0;
        }
        txt.text = multiplierBuffer.ToString() + " / 3";
    }

    // Update is called once per frame
    void Update()
    {
        if (timer <= 0f)
        {
            ind += 1;
            if (ind > 1)
            {
                ind = 0;
            }
            frameSR.sprite = sprs[ind];
            timer += Random.Range(5f, 6f);
        }
        timer -= Time.deltaTime;

        if (mag != null)
        {
            emsr.sprite = GS.PercentParameter(emsprs, (mag.orbs.Count * multiplier) / (mag.capacity * 3f));
        }
    }

    private void OnDestroy()
    {
        SpawnManager.instance.OnNewDay -= act;
    }
}
