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
        [SerializeField] GameObject[] aiCartPrefabs;

        [SerializeField] GameObject playerCartPrefab;
        [SerializeField] CinemachineCamera playerCamera;

        void Start()
        {
            var playerCart = Instantiate(playerCartPrefab, circuit.SpawnPoints[0].position,
                                                            circuit.SpawnPoints[0].rotation);

            Debug.Log(circuit.SpawnPoints[0].position);
            Debug.Log(circuit.SpawnPoints[0].rotation);
            playerCamera.Follow = playerCart.transform;
            playerCamera.LookAt = playerCart.transform;

            for (int i = 1; i < circuit.SpawnPoints.Length; ++i)
            {
                new AICartBuilder(aiCartPrefabs[Random.Range(0, aiCartPrefabs.Length)])
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