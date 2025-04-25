using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceClicker : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        SpawnManager.instance.CallSpawnOrbs(transform.position, new int[] {50,25,10,4});
    }
}
