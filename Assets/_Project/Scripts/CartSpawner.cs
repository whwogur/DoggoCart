using DoggoCart;
using Unity.Cinemachine;
using UnityEngine;
using Util.Extension;
using Util;

namespace DoggoCart
{
    public class CartSpawner : MonoBehaviour
    {
        [SerializeField] Circuit circuit;
        [SerializeField] AIDriverData aiDriverData;
        [SerializeField] GameObject[] aiKartPrefabs;

        [SerializeField] GameObject playerKartPrefab;
        [SerializeField] CinemachineCamera playerCamera;

        void Start()
        {
            var playerKart = Instantiate(playerKartPrefab, circuit.SpawnPoints[0].position,
                                                            circuit.SpawnPoints[0].rotation);
            playerCamera.Follow = playerKart.transform;
            playerCamera.LookAt = playerKart.transform;

            for (int i = 1; i < circuit.SpawnPoints.Length; ++i)
            {
                new AICartBuilder(aiKartPrefabs[Random.Range(0, aiKartPrefabs.Length)])
                    .withCircuit(circuit)
                    .withDriverData(aiDriverData)
                    .withSpawnPoint(circuit.SpawnPoints[i])
                    .build();
            }
        }

        class AICartBuilder
        {
            GameObject prefab;
            AIDriverData data;
            Circuit circuit;
            Transform spawnPoint;

            public AICartBuilder(GameObject prefab)
            {
                this.prefab = prefab;
            }

            public AICartBuilder withDriverData(AIDriverData data)
            {
                this.data = data;
                return this;
            }

            public AICartBuilder withCircuit(Circuit circuit)
            {
                this.circuit = circuit;
                return this;
            }

            public AICartBuilder withSpawnPoint(Transform spawnPoint)
            {
                this.spawnPoint = spawnPoint;
                return this;
            }

            public GameObject build()
            {
                var instance = Object.Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                var aiInput = instance.GetOrAdd<AIInput>();
                aiInput.AddCircuit(circuit);
                aiInput.AddDriverData(data);
                instance.GetComponent<CartController>().SetInput(aiInput);

                return instance;
            }
        }
    }
}