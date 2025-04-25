using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class telesliderchanger : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] srs; 
    void Start()
    {
        GS.OnNewEra += ctx =>
        {
            foreach (var sr in srs)
            {
                sr.material = GS.MatByEra(ctx, true);
            }
        };
    }

    
}
