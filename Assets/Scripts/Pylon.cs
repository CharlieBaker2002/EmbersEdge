using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pylon : Building
{
    public float reachDistance;
    public List<Battery> batteries;
    public List<Pylon> connections;
}
