using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IRoomUnit 
{
    public void RecieveRoom(Collider2D bounds, Vector2 pos);
}
