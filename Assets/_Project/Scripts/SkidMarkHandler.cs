using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DoggoCart
{
    public class SkidMarkHandler : NetworkBehaviour
    {
        [SerializeField] float slipThreshold = 0.7f;
        [SerializeField] GameObject skidMarkPrefab; // TrailRenderer 포함한 프리팹
        [SerializeField] float minSkidInterval = 1f; // 최소 생성 간격(초)
        [SerializeField] float minSkidDistance = 0.1f; // 최소 생성 거리(m)
        [SerializeField] int poolSize = 20; // 스키드마크 풀 크기
        [SerializeField] AudioClip tireSquealSound;
        [SerializeField] AudioSource tireSquealSource;

        private CartController cart;
        private WheelCollider[] wheelColliders;
        private Transform[] skidMarks = new Transform[4];
        private float[] lastSkidTimes = new float[4];
        private Vector3[] lastSkidPositions = new Vector3[4];
        private Queue<GameObject> skidMarkPool;
        private Transform poolParent;

        private const float SKIDMARK_DELAY = 1.0f;

        private void Awake()
        {
            // 풀 부모 생성
            poolParent = new GameObject("SkidMarkPool").transform;
            poolParent.gameObject.SetActive(false); // 비활성화하여 씬에 영향을 주지 않음
            DontDestroyOnLoad(poolParent.gameObject); // 씬 전환 시 유지
            tireSquealSource.clip = tireSquealSound;
            tireSquealSource.loop = true;
        }

        private void Start()
        {
            cart = GetComponent<CartController>();
            wheelColliders = GetComponentsInChildren<WheelCollider>();
            if (wheelColliders.Length != 4)
            {
                Debug.LogWarning("Expected 4 wheel colliders!");
            }

            // 오브젝트 풀 초기화
            InitializeSkidMarkPool();
        }

        public override void OnDestroy()
        {
            // 활성화된 스키드마크를 풀에 반환
            for (int i = 0; i < skidMarks.Length; ++i)
            {
                if (skidMarks[i] != null && !ReferenceEquals(skidMarks[i], null))
                {
                    ReturnToPool(skidMarks[i].gameObject);
                    skidMarks[i] = null;
                }
            }

            // 풀 정리
            while (skidMarkPool.Count > 0)
            {
                GameObject skidMark = skidMarkPool.Dequeue();
                if (null != skidMark && !ReferenceEquals(skidMark, null))
                {
                    Destroy(skidMark);
                }
            }

            if (null != poolParent)
            {
                Destroy(poolParent.gameObject);
            }

            base.OnDestroy();
        }

        private void OnDisable()
        {
            // 컴포넌트 비활성화 시 활성화된 스키드마크 반환
            for (int i = 0; i < skidMarks.Length; ++i)
            {
                if (skidMarks[i] != null && !ReferenceEquals(skidMarks[i], null))
                {
                    ReturnToPool(skidMarks[i].gameObject);
                    skidMarks[i] = null;
                }
            }
        }

        private void InitializeSkidMarkPool()
        {
            skidMarkPool = new Queue<GameObject>();
            for (int i = 0; i < poolSize; ++i)
            {
                if (skidMarkPrefab == null)
                {
                    Debug.LogError("SkidMarkPrefab is not assigned!");
                    return;
                }

                GameObject skidMark = Instantiate(skidMarkPrefab, poolParent);
                skidMark.SetActive(false);

                // NetworkObject가 있는 경우, 네트워크 스폰 준비
                NetworkObject networkObject = skidMark.GetComponent<NetworkObject>();
                if (null != networkObject && IsServer)
                {
                    networkObject.Spawn();
                }

                skidMarkPool.Enqueue(skidMark);
            }
        }

        private void FixedUpdate()
        {
            if (!IsOwner || !cart.IsGrounded) return;

            for (int i = 0; i < wheelColliders.Length; ++i)
            {
                if (null != wheelColliders[i])
                {
                    UpdateSkidMarks(i);
                }
            }
        }

        private void UpdateSkidMarks(int i)
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

        private void StartSkid(int i)
        {
            if (null != skidMarks[i])
                return;

            if (0 == skidMarkPool.Count)
            {
                //Debug.LogWarning("SkidMark pool is empty!");
                return;
            }

            GameObject skidMarkObject = skidMarkPool.Dequeue();
            if (null == skidMarkObject)
            {
                Debug.LogWarning("SkidMark object from pool is null or destroyed!");
                return;
            }

            tireSquealSource.Play();
            skidMarkObject.SetActive(true);
            Transform skidMarkTransform = skidMarkObject.transform;
            skidMarks[i] = skidMarkTransform;
            skidMarkTransform.SetParent(wheelColliders[i].transform, false);
            skidMarkTransform.localPosition = -Vector3.up * wheelColliders[i].radius * 0.9f;
            skidMarkTransform.localRotation = Quaternion.identity;

            TrailRenderer trail = skidMarkObject.GetComponent<TrailRenderer>();
            if (null != trail)
            {
                trail.Clear();
            }
            else
            {
                Debug.LogWarning("SkidMark object missing TrailRenderer!");
            }
        }

        private void EndSkid(int i)
        {
            if (null == skidMarks[i])
                return;

            tireSquealSource.Stop();

            GameObject skidMarkObject = skidMarks[i].gameObject;
            skidMarks[i] = null;
            if (null != skidMarkObject)
            {
                StartCoroutine(DelayedReturnToPool(skidMarkObject));
            }
        }

        private void ReturnToPool(GameObject skidMark)
        {
            if (null == skidMark)
                return;

            // NetworkObject가 있는 경우, 서버에서 디스폰
            NetworkObject networkObject = skidMark.GetComponent<NetworkObject>();
            if (null != networkObject && IsServer)
            {
                networkObject.Despawn(false); // 디스폰만 하고 즉시 파괴하지 않음
            }

            skidMark.transform.SetParent(poolParent);
            skidMark.SetActive(false);
            skidMarkPool.Enqueue(skidMark);
        }

        private IEnumerator DelayedReturnToPool(GameObject skidMark)
        {
            if (null == skidMark)
            {
                yield break;
            }

            yield return new WaitForSeconds(SKIDMARK_DELAY);

            if (null != skidMark)
            {
                ReturnToPool(skidMark);
            }
        }
    }
}