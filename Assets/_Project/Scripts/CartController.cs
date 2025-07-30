using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using Util;
using Util.Extension;

namespace DoggoCart
{
    [System.Serializable]
    public class AxleInfo
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public bool motor;
        public bool steering;
        public WheelFrictionCurve initialForwardFriction;
        public WheelFrictionCurve initialSidewaysFriction;
    }

    public struct InputPayload : INetworkSerializable
    {
        public int tick;
        public DateTime timestamp;
        public ulong networkObjectID;
        public Vector3 inputVector;
        public Vector3 position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref timestamp);
            serializer.SerializeValue(ref networkObjectID);
            serializer.SerializeValue(ref inputVector);
            serializer.SerializeValue(ref position);
        }
    }

    public struct StatePayload : INetworkSerializable
    {
        public int tick;
        public ulong networkObjectID;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref networkObjectID);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
        }
    }

    public class CartController : NetworkBehaviour
    {
        [Header("Axle Info")]
        [SerializeField] AxleInfo[] axleInfos;

        [Header("Motor Attributes")]
        [SerializeField] float maxMotorTorque = 3000f;
        [SerializeField] float maxSpeed;

        [Header("Steering Attributes")]
        [SerializeField] float maxSteeringAngle = 30f;
        [SerializeField] AnimationCurve turnCurve;
        [SerializeField] float turnStrength = 1500f;

        [Header("Brake and Drift")]
        [SerializeField] float brakeTorque = 10000f;
        [SerializeField] float driftSteerMultiplier = 1.5f;

        [Header("Physics")]
        [SerializeField] Transform centerOfMass;
        [SerializeField] float downForce = 100f;
        [SerializeField] float gravity = Physics.gravity.y;
        [SerializeField] float lateralGScale = 10f;

        [Header("Banking")]
        [SerializeField] float maxBankAngle = 5f;
        [SerializeField] float bankSpeed = 2f;

        [Header("Refs")]
        [SerializeField] InputReader playerInput;
        [SerializeField] Circuit circuit;
        [SerializeField] AIDriverData aiDriverData;

        IDrive input;
        Rigidbody rigidBody;
        ClientNetworkTransform clientNetworkTransform;

        public Vector3 cartVelocity { get; private set; }
        float brakeVelocity;
        float driftVelocity;

        RaycastHit hit;

        const float THRESHOLD_SPEED = 10f;
        const float CENTER_OF_MASS_TARGET = -0.5f;
        Vector3 initialCenterOfMass;

        public bool IsGrounded = true;
        public Vector3 Velocity => cartVelocity;
        public float MaxSpeed => maxSpeed;

        //===========
        // Netcode
        //===========
        NetworkTimer networkTimer;
        const float SERVER_TICK_RATE = 60f;
        const int BUFFER_SIZE = 1024;
        const float RECONCILE_COOLDOWN = 1f;
        const float EXP_LIMIT = 0.5f;
        const float EXP_MULTIPLIER = 1.2f;

        // Extrapolation
        StatePayload expState;
        CountdownTimer expCooldownTimer;

        // Netcode Client
        CircularBuffer<StatePayload> clientStateBuffer;
        CircularBuffer<InputPayload> clientInputBuffer;
        StatePayload lastServerState;
        StatePayload lastProcessedState;

        // Netcode Server
        CircularBuffer<StatePayload> serverStateBuffer;
        Queue<InputPayload> serverInputQueue;

        [Header("Netcode")]
        [SerializeField] float reconciliationThreshold = 50f;
        [SerializeField] GameObject serverCapsule;
        [SerializeField] GameObject clientCapsule;

        CountdownTimer reconcileCooldownTimer;

        

        void Awake()
        {
            if (playerInput is IDrive driveInput)
            {
                input = driveInput;
            }

            rigidBody = GetComponent<Rigidbody>();
            clientNetworkTransform = GetComponent<ClientNetworkTransform>();
            input.Enable();

            rigidBody.centerOfMass = centerOfMass.localPosition;
            initialCenterOfMass = centerOfMass.localPosition;

            foreach (var axleInfo in axleInfos)
            {
                axleInfo.initialForwardFriction = axleInfo.leftWheel.forwardFriction;
                axleInfo.initialSidewaysFriction = axleInfo.leftWheel.sidewaysFriction;
            }

            networkTimer = new NetworkTimer(SERVER_TICK_RATE);
            clientStateBuffer = new CircularBuffer<StatePayload>(BUFFER_SIZE);
            clientInputBuffer = new CircularBuffer<InputPayload>(BUFFER_SIZE);
            serverStateBuffer = new CircularBuffer<StatePayload>(BUFFER_SIZE);
            serverInputQueue = new Queue<InputPayload>();
            reconcileCooldownTimer = new CountdownTimer(RECONCILE_COOLDOWN);
            expCooldownTimer = new CountdownTimer(EXP_LIMIT);

            reconcileCooldownTimer.OnTimerStart += () => expCooldownTimer.Stop();
            expCooldownTimer.OnTimerStart += () => reconcileCooldownTimer.Stop();

            expCooldownTimer.OnTimerStart += () => SwitchAuthorityMode(AuthorityMode.Server);
            expCooldownTimer.OnTimerStop += () => SwitchAuthorityMode(AuthorityMode.Client);
        }

        public void SetInput(IDrive input)
        {
            this.input = input;
        }

        protected virtual void Update()
        {
            networkTimer.Update(Time.deltaTime);
            reconcileCooldownTimer.Tick(Time.deltaTime);
            expCooldownTimer.Tick(Time.deltaTime);

            Extrapolate();
        }

        void FixedUpdate()
        {
            while (networkTimer.ShouldTick())
            {
                HandleClientTick();
                HandleServerTick();
            }
            Extrapolate();
        }

        void SwitchAuthorityMode(AuthorityMode authorityMode)
        {
            clientNetworkTransform.authorityMode = authorityMode;

            bool shouldSync = authorityMode == AuthorityMode.Server;
            clientNetworkTransform.SyncPositionX = shouldSync;
            clientNetworkTransform.SyncPositionY = shouldSync;
            clientNetworkTransform.SyncPositionZ = shouldSync;
        }

        void HandleServerTick()
        {
            if (!IsServer)
                return;

            InputPayload inputPayload = default;
            var bufferIndex = -1;

            while (serverInputQueue.Count > 0)
            {
                inputPayload = serverInputQueue.Dequeue();
                bufferIndex = inputPayload.tick % BUFFER_SIZE;

                StatePayload statePayload = ProcessMovement(inputPayload);
                serverCapsule.transform.position = statePayload.position;
                serverStateBuffer.Add(statePayload, bufferIndex);
            }

            if (-1 == bufferIndex)
                return;

            SendToClientRpc(serverStateBuffer.Get(bufferIndex));
            HandleExtrapolation(serverStateBuffer.Get(bufferIndex), CalculateLatencyInMilliseconds(inputPayload));
        }

        static float CalculateLatencyInMilliseconds(InputPayload inputPayload)
        {
            return (DateTime.Now - inputPayload.timestamp).Milliseconds / 1000f;
        }

        [ClientRpc]
        void SendToClientRpc(StatePayload statePayload)
        {
            if (!IsOwner)
                return;

            lastServerState = statePayload;
        }

        void HandleClientTick()
        {
            if (!IsClient || !IsOwner)
                return;

            var currentTick = networkTimer.CurrentTick;
            var bufferIndex = currentTick % BUFFER_SIZE;

            InputPayload inputPayload = new InputPayload()
            {
                tick = currentTick,
                timestamp = DateTime.Now,
                networkObjectID = NetworkObjectId,
                inputVector = input.Move,
                position = transform.position
            };

            clientInputBuffer.Add(inputPayload, bufferIndex);
            SendToServerRpc(inputPayload);

            StatePayload statePayload = ProcessMovement(inputPayload);
            clientCapsule.transform.position = statePayload.position;
            clientStateBuffer.Add(statePayload, bufferIndex);

            // 조정
            HandleServerReconciliation();
        }

        bool ShouldReconcile()
        {
            bool isNewServerState = !lastServerState.Equals(default);
            bool isLastStateUndefinedOrDifferent = lastProcessedState.Equals(default) || !lastProcessedState.Equals(lastServerState);

            return !reconcileCooldownTimer.IsRunning &&
                                    isNewServerState && 
                    isLastStateUndefinedOrDifferent && !expCooldownTimer.IsRunning;
        }

        void ReconcileState(StatePayload rewindState)
        {
            transform.position = rewindState.position;
            transform.rotation = rewindState.rotation;
            rigidBody.linearVelocity = rewindState.velocity;
            rigidBody.angularVelocity = rewindState.angularVelocity;

            if (!rewindState.Equals(lastServerState))
                return;

            clientStateBuffer.Add(rewindState, rewindState.tick % BUFFER_SIZE);

            // rewind state -> current state
            int tickToReplay = lastServerState.tick;
            while (tickToReplay < networkTimer.CurrentTick)
            {
                int bufferIndex = tickToReplay % BUFFER_SIZE;
                StatePayload statePayload = ProcessMovement(clientInputBuffer.Get(bufferIndex));
                clientStateBuffer.Add(statePayload, bufferIndex);
                ++tickToReplay;
            }
        }

        void HandleServerReconciliation()
        {
            if (!ShouldReconcile())
                return;

            float positionError;
            int bufferIndex;

            bufferIndex = lastServerState.tick % BUFFER_SIZE;

            if (bufferIndex - 1 < 0)
                return;

            StatePayload rewindState = IsHost ? serverStateBuffer.Get(bufferIndex - 1) : lastServerState; // 호스트 RPC는 바로 실행되기 때문에, 지난 서버 state를 쓰면 댐
            StatePayload clientState = IsHost ? clientStateBuffer.Get(bufferIndex - 1) : clientStateBuffer.Get(bufferIndex);
            positionError = Vector3.Distance(rewindState.position, clientState.position);

            if (positionError > reconciliationThreshold)
            {
                ReconcileState(rewindState);
                reconcileCooldownTimer.Start();
            }

            lastProcessedState = lastServerState;
        }

        void Extrapolate()
        {
            if (IsServer && expCooldownTimer.IsRunning)
            {
                transform.position += expState.position.With(y: 0);
            }
        }
        void HandleExtrapolation(StatePayload statePayload, float latency)
        {
            if (ShouldExtrapolate(latency))
            {
                float axisLength = latency * statePayload.angularVelocity.magnitude * Mathf.Rad2Deg;
                Quaternion angularRotation = Quaternion.AngleAxis(axisLength, statePayload.angularVelocity);

                if (default != expState.position)
                {
                    statePayload = expState;
                }

                var positionAdjustment = statePayload.velocity * (1 + latency * EXP_MULTIPLIER);
                expState.position = angularRotation * positionAdjustment;
                expState.rotation = angularRotation * statePayload.rotation;
                expState.velocity = statePayload.velocity;
                expState.angularVelocity = statePayload.angularVelocity;
                expCooldownTimer.Start();
            }
            else
            {
                expCooldownTimer.Stop();
            }
        }

        bool ShouldExtrapolate(float latency)
        {
            return latency < EXP_LIMIT && latency > Time.fixedDeltaTime/* 유니티 넷코드에서 처리해주는 범위*/;
        }

        [ServerRpc]
        void SendToServerRpc(InputPayload input)
        {
            serverInputQueue.Enqueue(input);
        }

        StatePayload ProcessMovement(InputPayload input)
        {
            Move(input.inputVector);

            return new StatePayload()
            {
                tick = input.tick,
                networkObjectID = input.networkObjectID,
                position = transform.position,
                rotation = transform.rotation,
                velocity = rigidBody.linearVelocity,
                angularVelocity = rigidBody.angularVelocity,
            };
        }

        void Move(Vector3 inputVector)
        {
            float verticalInput = AdjustInput(input.Move.y);
            float horizontalInput = AdjustInput(input.Move.x);

            float motor = maxMotorTorque * verticalInput;
            float steering = maxSteeringAngle * horizontalInput;

            UpdateAxles(motor, steering);
            UpdateBanking(horizontalInput);

            cartVelocity = transform.InverseTransformDirection(rigidBody.linearVelocity);

            if (IsGrounded)
            {
                HandleGroundedMovement(verticalInput, horizontalInput);
            }
            else
            {
                HandleAirborneMovement(verticalInput, horizontalInput);
            }
        }
        void HandleGroundedMovement(float verticalInput, float horizontalInput)
        {
            // 회전
            if (Mathf.Abs(verticalInput) > 0.1f ||
                Mathf.Abs(cartVelocity.z) > 1)
            {
                float turnMultiplier = Mathf.Clamp01(turnCurve.Evaluate(cartVelocity.magnitude / maxSpeed));
                rigidBody.AddTorque(Vector3.up * horizontalInput * Mathf.Sign(cartVelocity.z) * turnStrength * 100f * turnMultiplier);
            }

            // 가속
            if (!input.IsBraking)
            {
                float targetSpeed = verticalInput * maxSpeed;
                Vector3 forwardWithOutY = transform.forward.With(y: 0).normalized;
                rigidBody.linearVelocity = Vector3.Lerp(rigidBody.linearVelocity, forwardWithOutY * targetSpeed, networkTimer.MinTimeBetweenTicks);
            }

            // 아래로 주는 힘
            float speedFactor = Mathf.Clamp01(rigidBody.linearVelocity.magnitude / maxSpeed);
            float lateralG = Mathf.Abs(Vector3.Dot(rigidBody.linearVelocity, transform.right));
            float downForceFactor = Mathf.Max(speedFactor, lateralG / lateralGScale);

            rigidBody.AddForce(-transform.up * downForce * rigidBody.mass * downForceFactor);

            // 무게중심 옮기기
            float speed = rigidBody.linearVelocity.magnitude;
            Vector3 centerOfMassAdjustment = (speed > THRESHOLD_SPEED) 
                ? new Vector3(0f, 0f, Mathf.Abs(verticalInput) > 0.1f 
                    ? Mathf.Sign(verticalInput) * CENTER_OF_MASS_TARGET : 0f)
                : Vector3.zero;
        }

        void UpdateBanking(float horizontalInput)
        {
            float targetBankAngle = horizontalInput * -maxBankAngle;
            Vector3 currentEuler = transform.localEulerAngles;
            currentEuler.z = Mathf.LerpAngle(currentEuler.z, targetBankAngle, Time.deltaTime * bankSpeed);
            transform.localEulerAngles = currentEuler;
        }

        void HandleAirborneMovement(float verticalInput, float horizontalInput)
        {
            rigidBody.linearVelocity = Vector3.Lerp(
                rigidBody.linearVelocity,
                rigidBody.linearVelocity + Vector3.down * gravity,
                Time.deltaTime * gravity
            );
        }

        void UpdateAxles(float motor, float steering)
        {
            foreach (var axleInfo in axleInfos)
            {
                HandleSteering(axleInfo, steering);
                HandleMotor(axleInfo, motor);
                HandleBrakesAndDrift(axleInfo);
                UpdateWheelVisuals(axleInfo.leftWheel);
                UpdateWheelVisuals(axleInfo.rightWheel);
            }
        }

        void HandleSteering(AxleInfo axleInfo, float steering)
        {
            if (axleInfo.steering)
            {
                float steeringMultiplier = input.IsBraking ? driftSteerMultiplier : 1f;
                axleInfo.leftWheel.steerAngle = steering * steeringMultiplier;
                axleInfo.rightWheel.steerAngle = steering * steeringMultiplier;
            }
        }

        void HandleMotor(AxleInfo axleInfo, float motor)
        {
            if (axleInfo.motor)
            {
                axleInfo.leftWheel.motorTorque = motor;
                axleInfo.rightWheel.motorTorque = motor;
            }
        }

        void HandleBrakesAndDrift(AxleInfo axleInfo)
        {
            if (axleInfo.motor)
            {
                if (input.IsBraking)
                {
                    rigidBody.constraints = RigidbodyConstraints.FreezeRotationX;

                    float newZ = Mathf.SmoothDamp(rigidBody.linearVelocity.z, 0, ref brakeVelocity, 1f);
                    rigidBody.linearVelocity = rigidBody.linearVelocity.With(z: newZ);

                    axleInfo.leftWheel.brakeTorque = brakeTorque;
                    axleInfo.rightWheel.brakeTorque = brakeTorque;

                    ApplyDriftFriction(axleInfo.leftWheel);
                    ApplyDriftFriction(axleInfo.rightWheel);
                }
                else
                {
                    rigidBody.constraints = RigidbodyConstraints.None;

                    axleInfo.leftWheel.brakeTorque = 0;
                    axleInfo.rightWheel.brakeTorque = 0;

                    ResetDriftFriction(axleInfo.leftWheel);
                    ResetDriftFriction(axleInfo.rightWheel);
                }
            }
        }

        void ResetDriftFriction(WheelCollider wheel)
        {
            AxleInfo axleInfo = axleInfos.FirstOrDefault(axle => axle.leftWheel == wheel || axle.rightWheel == wheel);
            if (null != axleInfo)
            {
                wheel.forwardFriction = axleInfo.initialForwardFriction;
                wheel.sidewaysFriction = axleInfo.initialSidewaysFriction;
            }
        }

        void ApplyDriftFriction(WheelCollider wheel)
        {
            if (wheel.GetGroundHit(out var Hit))
            {
                wheel.forwardFriction = UpdateFriction(wheel.forwardFriction);
                wheel.sidewaysFriction = UpdateFriction(wheel.sidewaysFriction);
                IsGrounded = true;
            }
        }

        WheelFrictionCurve UpdateFriction(WheelFrictionCurve friction)
        {
            friction.stiffness = input.IsBraking 
                ? Mathf.SmoothDamp(friction.stiffness, .5f, ref driftVelocity, Time.deltaTime * 2f) : 1f;

            return friction;
        }

        void UpdateWheelVisuals(WheelCollider collider)
        {
            if (0 != collider.transform.childCount)
            {
                Transform visualWheel = collider.transform.GetChild(0);

                Vector3 position;
                Quaternion rotation;
                collider.GetWorldPose(out position, out rotation);

                visualWheel.transform.position = position;
                visualWheel.transform.rotation = rotation;
            }
        }

        float AdjustInput(float input)
        {
            return input switch
            {
                >= .7f => 1f,
                <= -.7f => -1f,
                _ => input
            };
        }
    }
}
