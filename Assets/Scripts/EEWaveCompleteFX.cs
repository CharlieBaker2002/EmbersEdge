using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EEWaveCompleteFX : MonoBehaviour
{
    [SerializeField] LineRenderer lr;
    float t = 0.05f;
    Vector3[] vs = new Vector3[100];
    private Gradient grad;
    [SerializeField] Material[] mats;
    [SerializeField] bool setColour = true;

    [Header("Shockwave")]
    [SerializeField] bool spawnShockwave = true;
    [Tooltip("Peak screen-push at birth; fades to 0 as the ring expands.")]
    [SerializeField] float shockwaveStrength = 0.06f;
    [Tooltip("Crest thickness (local units) at birth and when fully expanded — thins with distance.")]
    [SerializeField] float shockwaveWidthStart = 2.5f;
    [SerializeField] float shockwaveWidthEnd = 0.5f;

    Shockwave shock;
    const float ringRadius = 15f;   // the LR expands to ringRadius * t (local units)

    // Start is called before the first frame update
    void Start()
    {
        if (setColour)
        {
            lr.material = mats[GS.era];
        }

        // Spawn a distortion ring we drive ourselves so it stays locked onto the line wave.
        if (spawnShockwave)
        {
            var prefab = Resources.Load<GameObject>("Shockwave");
            if (prefab != null)
            {
                shock = Instantiate(prefab).GetComponent<Shockwave>();
                shock.chroma = 0.2f;
                if (shock != null)
                {
                    shock.DriveBegin();                       // manual mode (don't self-animate)
                    shock.transform.position = transform.position;
                }
            }
        }

        LeanTween.value(gameObject, 0.05f, 1f, 5f).setOnUpdate(x => t = x).setEaseOutExpo();
    }

    // Update is called once per frame
    void Update()
    {
        if(t >= 1f)
        {
            if (shock != null) shock.Finish();
            Destroy(gameObject);
            return;
        }

        lr.GetPositions(vs);
        for(int i = 0; i < vs.Length; i++)
        {
            vs[i] = ringRadius * t * vs[i].normalized;
        }
        lr.SetPositions(vs);

        // Lock the distortion ring onto the visual ring: same centre, same world radius.
        if (shock != null)
        {
            float scale = transform.lossyScale.x;
            shock.transform.position = transform.position;
            shock.Drive(ringRadius * t * scale,
                        Mathf.Lerp(shockwaveWidthStart, shockwaveWidthEnd, t) * scale,
                        shockwaveStrength * (1f - t));
        }

        if (!setColour) return;
        grad = new Gradient();
        grad.SetKeys(new GradientColorKey[] { new(GS.ColFromEra(), 0) },new GradientAlphaKey[] { new(1 - t*t, 0) });
        lr.colorGradient = grad;

    }
}
