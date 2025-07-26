using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace DoggoCart
{
    public class SkidMarkHandler : NetworkBehaviour
    {
        [SerializeField] float slipThreshold = 0.4f;
        [SerializeField] GameObject skidMarkPrefab; // TrailRenderer 포함한 프리팹
        [SerializeField] float minSkidInterval = 0.1f; // 최소 생성 간격(초)
        [SerializeField] float minSkidDistance = 0.5f; // 최소 생성 거리(m)
        [SerializeField] int poolSize = 20; // 오브젝트 풀 크기

        private CartController cart;
        private WheelCollider[] wheelColliders;
        private Transform[] skidMarks = new Transform[4]; // Transform 배열
        private float[] lastSkidTimes = new float[4];
        private Vector3[] lastSkidPositions = new Vector3[4];
        private Queue<GameObject> skidMarkPool;

        void Start()
        {
            if (!IsClient) return; // 클라이언트에서만 스키드마크 생성

            cart = GetComponent<CartController>();
            wheelColliders = GetComponentsInChildren<WheelCollider>();
            if (4 != wheelColliders.Length)
            {
                Debug.LogWarning("Expected 4 wheel colliders!");
            }

            // 오브젝트 풀 초기화
            InitializeSkidMarkPool();
        }

        void InitializeSkidMarkPool()
        {
            skidMarkPool = new Queue<GameObject>();
            for (int i = 0; i < poolSize; ++i)
            {
                GameObject skidMark = Instantiate(skidMarkPrefab);
                skidMark.SetActive(false);
                skidMarkPool.Enqueue(skidMark);
            }
        }

        void FixedUpdate()
        {
            if (!IsClient || !cart.IsGrounded) return; // 클라이언트 및 접지 상태 확인

            for (int i = 0; i < wheelColliders.Length; ++i)
            {
                UpdateSkidMarks(i);
            }
        }

        void UpdateSkidMarks(int i)
        {
            if (!wheelColliders[i].GetGroundHit(out var hit))
            {
                EndSkid(i);
                return;
            }

            // 슬립 확인
            bool isSkidding = Mathf.Abs(hit.sidewaysSlip) > slipThreshold || Mathf.Abs(hit.forwardSlip) > slipThreshold;
            if (!isSkidding)
            {
                EndSkid(i);
                return;
            }

            // 시간/거리 간격 체크
            float currentTime = Time.time;
            Vector3 currentPos = wheelColliders[i].transform.position;
            if (currentTime - lastSkidTimes[i] < minSkidInterval ||
                Vector3.Distance(currentPos, lastSkidPositions[i]) < minSkidDistance)
            {
                return;
            }

            StartSkid(i);
            lastSkidTimes[i] = currentTime;
            lastSkidPositions[i] = currentPos;
        }

        void StartSkid(int i)
        {
            if (null != skidMarks[i]) return;

            if (skidMarkPool.Count == 0)
            {
                Debug.LogWarning("SkidMark pool is empty!");
                return;
            }

            GameObject skidMarkObject = skidMarkPool.Dequeue();
            skidMarkObject.SetActive(true);
            Transform skidMarkTransform = skidMarkObject.transform;
            skidMarks[i] = skidMarkTransform;
            skidMarkTransform.parent = wheelColliders[i].transform;
            skidMarkTransform.localPosition = -Vector3.up * wheelColliders[i].radius * 0.9f;
            skidMarkTransform.localRotation = Quaternion.identity;
            skidMarkObject.GetComponent<TrailRenderer>().Clear(); // Trail 초기화
        }

        void EndSkid(int i)
        {
            if (null == skidMarks[i]) return;

            GameObject skidMarkObject = skidMarks[i].gameObject;
            skidMarks[i] = null;
            ReturnToPool(skidMarkObject); // ReturnToPool 호출
        }

        void ReturnToPool(GameObject skidMark)
        {
            skidMark.transform.SetParent(null);
            skidMark.SetActive(false);
            skidMarkPool.Enqueue(skidMark);
            //Debug.Log($"SkidMark returned to pool. Pool size: {skidMarkPool.Count}");
        }

        public override void OnNetworkDespawn()
        {
            // 활성화된 스키드마크를 풀에 반환
            for (int i = 0; i < skidMarks.Length; ++i)
            {
                if (skidMarks[i] != null)
                {
                    ReturnToPool(skidMarks[i].gameObject); // ReturnToPool 호출
                    skidMarks[i] = null;
                }
            }

            // 풀 정리
            while (skidMarkPool.Count > 0)
            {
                Destroy(skidMarkPool.Dequeue());
            }
        }

        void OnDisable()
        {
            // 컴포넌트 비활성화 시 활성화된 스키드마크 반환
            for (int i = 0; i < skidMarks.Length; ++i)
            {
                if (skidMarks[i] != null)
                {
                    ReturnToPool(skidMarks[i].gameObject); // ReturnToPool 호출
                    skidMarks[i] = null;
                }
            }
        }
    }
}