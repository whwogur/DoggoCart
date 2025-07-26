using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DoggoCart
{
    public class SkidMarkHandler : MonoBehaviour
    {
        [SerializeField] float slipThreshold = 0.7f;
        [SerializeField] GameObject skidMarkPrefab; // TrailRenderer 포함한 프리팹
        [SerializeField] float minSkidInterval = 1f; // 최소 생성 간격(초)
        [SerializeField] float minSkidDistance = 0.1f; // 최소 생성 거리(m)
        [SerializeField] int poolSize = 20; // 오브젝트 풀 크기

        private CartController cart;
        private WheelCollider[] wheelColliders;
        private Transform[] skidMarks = new Transform[4]; // Transform 배열
        private float[] lastSkidTimes = new float[4];
        private Vector3[] lastSkidPositions = new Vector3[4];
        private Queue<GameObject> skidMarkPool;

        private const float SKIDMARK_DELAY = 1.0f;
        private void Start()
        {
            cart = GetComponent<CartController>();
            wheelColliders = GetComponentsInChildren<WheelCollider>();
            if (4 != wheelColliders.Length)
            {
                Debug.LogWarning("Expected 4 wheel colliders!");
            }

            // 오브젝트 풀 초기화
            InitializeSkidMarkPool();
        }

        private void OnDestroy()
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

        private void OnDisable()
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

        private void InitializeSkidMarkPool()
        {
            skidMarkPool = new Queue<GameObject>();
            for (int i = 0; i < poolSize; ++i)
            {
                GameObject skidMark = Instantiate(skidMarkPrefab);
                skidMark.SetActive(false);
                skidMarkPool.Enqueue(skidMark);
            }
        }

        private void FixedUpdate()
        {
            if (!cart.IsGrounded) return;

            for (int i = 0; i < wheelColliders.Length; ++i)
            {
                UpdateSkidMarks(i);
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
            if (null != skidMarks[i]) return;

            if (0 == skidMarkPool.Count)
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

        private void EndSkid(int i)
        {
            if (null == skidMarks[i]) return;

            GameObject skidMarkObject = skidMarks[i].gameObject;
            skidMarks[i] = null;
            StartCoroutine(DelayedReturnToPool(skidMarkObject));
        }

        private void ReturnToPool(GameObject skidMark)
        {
            skidMark.transform.SetParent(null);
            skidMark.SetActive(false);
            skidMarkPool.Enqueue(skidMark);
        }

        private IEnumerator DelayedReturnToPool(GameObject skidMark)
        {
            yield return new WaitForSeconds(SKIDMARK_DELAY);

            ReturnToPool(skidMark);
        }
    }
}