using UnityEngine;

namespace ScriptableObjects.Blueprints.BPScripts
{
    [CreateAssetMenu(fileName = "BuildingBP", menuName = "ScriptableObjects/BuildingBP")]
    public class BuildingBP : Blueprint
    {
        public Blueprint[] Upgrades;
        public bool upgrade = false;
    }
}