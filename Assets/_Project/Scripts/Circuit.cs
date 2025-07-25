using System.Linq;
using UnityEngine;

namespace DoggoCart
{
    [CreateAssetMenu(fileName = "CircuitData", menuName = "Cart/CircuitData")]
    public class Circuit : ScriptableObject
    {
        public GameObject[] waypointObjects;
        public GameObject[] spawnpointObjects;

        public Transform[] Waypoints => waypointObjects
            .Where(go => go != null)
            .Select(go => go.transform)
            .ToArray();

        public Transform[] SpawnPoints => spawnpointObjects
            .Where(go => go != null)
            .Select(go => go.transform)
            .ToArray();
    }
}
