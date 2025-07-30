using DoggoCart;
using Unity.Cinemachine;
using UnityEngine;
using Util.Extension;
using Util;
using Unity.Netcode;

namespace DoggoCart
{
    public class CartSpawner : MonoBehaviour
    {
        [SerializeField] Circuit circuit;
        [SerializeField] AIDriverData aiDriverData;
        [SerializeField] GameObject[] aiKartPrefabs;

        void Start()
        {
            for (int i = 1; i < circuit.spawnPoints.Length; ++i)
            {
                GameObject aiCart = new AIKartBuilder(aiKartPrefabs[Random.Range(0, aiKartPrefabs.Length)])
                    .withCircuit(circuit)
                    .withDriverData(aiDriverData)
                    .withSpawnPoint(circuit.spawnPoints[i])
                    .build();

                if (null != aiCart)
                {
                    aiCart.GetComponent<NetworkObject>().Spawn();
                }
            }
        }

        class AIKartBuilder
        {
            GameObject prefab;
            AIDriverData data;
            Circuit circuit;
            Transform spawnPoint;

            public AIKartBuilder(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public AIKartBuilder withDriverData(AIDriverData data)
            {
                this.data = data;
                return this;
            }

            public AIKartBuilder withCircuit(Circuit circuit)
            {
                this.circuit = circuit;
                return this;
            }

            public AIKartBuilder withSpawnPoint(Transform spawnPoint)
            {
                this.spawnPoint = spawnPoint;
                return this;
            }

            public GameObject build()
            {
                var instance = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                var aiInput = instance.GetOrAdd<AIInput>();
                aiInput.AddCircuit(circuit);
                aiInput.AddDriverData(data);
                instance.GetComponent<CartController>().SetInput(aiInput);

                return instance;
            }
        }
    }
}