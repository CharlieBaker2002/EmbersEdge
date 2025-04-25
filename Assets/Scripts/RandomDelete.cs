using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomDelete : MonoBehaviour
{
    [Header("Integer percentage out of 100 TO DESTROY:")]
    public int percentage = 50;

    private void Awake()
    {
        if (Random.Range(1, 101) < percentage)
        {
            Destroy(gameObject);
        }
    }
}
