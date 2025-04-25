using UnityEngine;

public class BaseFX : MonoBehaviour
{
    [SerializeField] private Material[] mats;
    private ParticleSystemRenderer rend;
    void Awake()
    {
        rend = GetComponent<ParticleSystemRenderer>();
        rend.material = mats[GS.era];
    }
}
