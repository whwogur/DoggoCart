using UnityEngine;

namespace DoggoCart
{
    [CreateAssetMenu(fileName = "CircuitData", menuName = "Cart/CircuitData")]
    public class Circuit : ScriptableObject
    {
        public Transform[] waypoints;
        public Transform[] spawnPoints;
    }
}
