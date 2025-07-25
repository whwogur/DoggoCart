using DoggoCart;
using System;
using UnityEngine;
using Util.Extension;
using Util;

namespace DoggoCart
{
    public class AIInput : MonoBehaviour, IDrive
    {
        public Circuit circuit;
        public AIDriverData driverData;

        public Vector2 Move { get; private set; }
        public bool IsBraking { get; private set; }

        public void Enable()
        {
        }

        int currentWaypointIndex;
        int currentCornerIndex;

        CountdownTimer driftTimer;

        float previousYaw; // 카트의 이전 프레임 y축회전

        public void AddDriverData(AIDriverData data) => driverData = data;
        public void AddCircuit(Circuit circuit) => this.circuit = circuit;

        void Start()
        {
            if (null == circuit || null == driverData)
            {
                throw new ArgumentNullException($"AIInput requires a circuit and driver data to be set.");
            }
            previousYaw = transform.eulerAngles.y;
            driftTimer = new CountdownTimer(driverData.timeToDrift);
            driftTimer.OnTimerStart += () => IsBraking = true;
            driftTimer.OnTimerStop += () => IsBraking = false;
        }

        void Update()
        {
            driftTimer.Tick(Time.deltaTime);
            if (0 == circuit.Waypoints.Length)
            {
                return;
            }

            // 각속도 계산
            float currentYaw = transform.eulerAngles.y;
            float deltaYaw = Mathf.DeltaAngle(previousYaw, currentYaw);
            float angularVelocity = deltaYaw / Time.deltaTime;
            previousYaw = currentYaw;

            Vector3 toNextPoint = circuit.Waypoints[currentWaypointIndex].position - transform.position;
            Vector3 toNextCorner = circuit.Waypoints[currentCornerIndex].position - transform.position;
            var distanceToNextPoint = toNextPoint.magnitude;
            var distanceToNextCorner = toNextCorner.magnitude;

            // 다음 포인트 범위 내에 들어오면 다음 포인트로 이동 시작
            if (distanceToNextPoint < driverData.proximityThreshold)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % circuit.Waypoints.Length;
            }

            // 코너 처리
            if (distanceToNextCorner < driverData.updateCornerRange)
            {
                currentCornerIndex = currentWaypointIndex;
            }

            // 드리프트
            if (distanceToNextCorner < driverData.brakeRange && !driftTimer.IsRunning)
            {
                driftTimer.Start();
            }

            // 속도 조절
            Move = Move.With(y: driftTimer.IsRunning ? driverData.speedWhileDrifting : 1f);

            // yaw
            Vector3 desiredForward = toNextPoint.normalized;
            Vector3 currentForward = transform.forward;
            float turnAngle = Vector3.SignedAngle(currentForward, desiredForward, Vector3.up);

            // turnAngle 기반 이동
            Move = turnAngle switch
            {
                > 5f => Move.With(x: 1f),
                < -5f => Move.With(x: -1f),
                _ => Move.With(x: 0f)
            };

            // 코너링 핸들 반대로 돌리기
            if (Mathf.Abs(angularVelocity) > driverData.spinThreshold)
            {
                Move = Move.With(x: -Mathf.Sign(angularVelocity));
                IsBraking = true;
            }
            else
            {
                IsBraking = false;
            }
        }
    }
}