using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
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
        public Vector3 inputVector;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref inputVector);
        }
    }

    public struct StatePayload : INetworkSerializable
    {
        public int tick;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
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
        [SerializeField] CinemachineCamera playerCamera;
        [SerializeField] AudioListener audioListener;

        IDrive input;
        private Rigidbody rigidBody;

        private Vector3 cartVelocity;
        private float brakeVelocity;
        private float driftVelocity;

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
        NetworkTimer timer;
        const float SERVER_TICK_RATE = 60f;
        const int BUFFER_SIZE = 1024;

        // Netcode Client
        CircularBuffer<StatePayload> clientStateBuffer;
        CircularBuffer<InputPayload> clientInputBuffer;
        StatePayload lastServerState;
        StatePayload lastProcessedState;

        // Netcode Server
        CircularBuffer<StatePayload> serverStateBuffer;
        Queue<InputPayload> serverInputQueue;

        [Header("Netcode")]
        [SerializeField] float reconciliationThreshold = 10f;
        [SerializeField] GameObject serverCapsule;
        [SerializeField] GameObject clientCapsule;

        private void Awake()
        {
            if (playerInput is IDrive driveInput)
            {
                input = driveInput;
            }

            rigidBody = GetComponent<Rigidbody>();
            input.Enable();

            rigidBody.centerOfMass = centerOfMass.localPosition;
            initialCenterOfMass = centerOfMass.localPosition;

            foreach (var axleInfo in axleInfos)
            {
                axleInfo.initialForwardFriction = axleInfo.leftWheel.forwardFriction;
                axleInfo.initialSidewaysFriction = axleInfo.leftWheel.sidewaysFriction;
            }

            timer = new NetworkTimer(SERVER_TICK_RATE);
            clientStateBuffer = new CircularBuffer<StatePayload>(BUFFER_SIZE);
            clientInputBuffer = new CircularBuffer<InputPayload>(BUFFER_SIZE);
            serverStateBuffer = new CircularBuffer<StatePayload>(BUFFER_SIZE);
            serverInputQueue = new Queue<InputPayload>();
        }

        public void SetInput(IDrive input)
        {
            this.input = input;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                audioListener.enabled = false;
                playerCamera.Priority = 0;
                return;
            }

            playerCamera.Priority = 100;
            audioListener.enabled = true;
        }

        private void Update()
        {
            timer.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!IsOwner)
                return;

            while (timer.ShouldTick())
            {
                HandleClientTick();
                HandleServerTick();
            }
        }

        private void HandleServerTick()
        {
            var bufferIndex = -1;
            while (serverInputQueue.Count > 0)
            {
                InputPayload inputPayload = serverInputQueue.Dequeue();
                bufferIndex = inputPayload.tick % BUFFER_SIZE;

                StatePayload statePayload = SimulateMovement(inputPayload);
                serverCapsule.transform.position = statePayload.position.With(y: 5);
                serverStateBuffer.Add(statePayload, bufferIndex);
            }

            if (-1 == bufferIndex)
                return;

            SendToClientRpc(serverStateBuffer.Get(bufferIndex));
        }

        StatePayload SimulateMovement(InputPayload inputPayload)
        {
            Physics.simulationMode = SimulationMode.Script;

            Move(inputPayload.inputVector);
            Physics.Simulate(Time.fixedDeltaTime);
            Physics.simulationMode = SimulationMode.FixedUpdate;

            return new StatePayload()
            {
                tick = inputPayload.tick,
                position = transform.position,
                rotation = transform.rotation,
                velocity = rigidBody.linearVelocity,
                angularVelocity = rigidBody.angularVelocity,
            };
        }

        [ClientRpc]
        void SendToClientRpc(StatePayload statePayload)
        {
            if (!IsOwner)
                return;

            lastServerState = statePayload;
        }

        private void HandleClientTick()
        {
            if (!IsClient)
                return;

            var currentTick = timer.CurrentTick;
            var bufferIndex = currentTick % BUFFER_SIZE;

            InputPayload inputPayload = new InputPayload()
            {
                tick = currentTick,
                inputVector = input.Move
            };

            clientInputBuffer.Add(inputPayload, bufferIndex);
            SendToServerRpc(inputPayload);

            StatePayload statePayload = ProcessMovement(inputPayload);
            clientCapsule.transform.position = statePayload.position.With(y: 5);
            clientStateBuffer.Add(statePayload, bufferIndex);

            // 조정
            HandleServerReconciliation();
        }

        private bool ShouldReconcile()
        {
            bool isNewServerState = !lastServerState.Equals(default);
            bool isLastStateUndefinedOrDifferent = lastProcessedState.Equals(default) || !lastProcessedState.Equals(lastServerState);

            return isNewServerState && isLastStateUndefinedOrDifferent;
        }

        void ReconcileState(StatePayload rewindState)
        {
            transform.position = rewindState.position;
            transform.rotation = rewindState.rotation;
            rigidBody.linearVelocity = rewindState.velocity;
            rigidBody.angularVelocity = rewindState.angularVelocity;

            if (!rewindState.Equals(lastServerState))
                return;

            clientStateBuffer.Add(rewindState, rewindState.tick);

            // rewind state -> current state
            int tickToReplay = lastServerState.tick;
            while (tickToReplay < timer.CurrentTick)
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
            StatePayload rewindState = default;

            bufferIndex = lastServerState.tick % BUFFER_SIZE;
            if (bufferIndex - 1 < 0)
                return;

            rewindState = IsHost ? serverStateBuffer.Get(bufferIndex - 1) : lastServerState;
            positionError = Vector3.Distance(rewindState.position, clientStateBuffer.Get(bufferIndex).position);

            if (positionError > reconciliationThreshold)
            {
                ReconcileState(rewindState);
            }

            lastProcessedState = lastServerState;
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
                position = transform.position,
                rotation = transform.rotation,
                velocity = rigidBody.linearVelocity,
                angularVelocity = rigidBody.angularVelocity,
            };
        }

        private void Move(Vector3 inputVector)
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
        private void HandleGroundedMovement(float verticalInput, float horizontalInput)
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
                float lerpFraction = timer.MinTimeBetweenTicks / (1f / Time.deltaTime);
                rigidBody.linearVelocity = Vector3.Lerp(rigidBody.linearVelocity, forwardWithOutY * targetSpeed, lerpFraction);
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

        private void UpdateBanking(float horizontalInput)
        {
            float targetBankAngle = horizontalInput * -maxBankAngle;
            Vector3 currentEuler = transform.localEulerAngles;
            currentEuler.z = Mathf.LerpAngle(currentEuler.z, targetBankAngle, Time.deltaTime * bankSpeed);
            transform.localEulerAngles = currentEuler;
        }

        private void HandleAirborneMovement(float verticalInput, float horizontalInput)
        {
            rigidBody.linearVelocity = Vector3.Lerp(
                rigidBody.linearVelocity,
                rigidBody.linearVelocity + Vector3.down * gravity,
                Time.deltaTime * gravity
            );
        }

        private void UpdateAxles(float motor, float steering)
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

        private void HandleBrakesAndDrift(AxleInfo axleInfo)
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

        private void ResetDriftFriction(WheelCollider wheel)
        {
            AxleInfo axleInfo = axleInfos.FirstOrDefault(axle => axle.leftWheel == wheel || axle.rightWheel == wheel);
            if (null != axleInfo)
            {
                wheel.forwardFriction = axleInfo.initialForwardFriction;
                wheel.sidewaysFriction = axleInfo.initialSidewaysFriction;
            }
        }

        private void ApplyDriftFriction(WheelCollider wheel)
        {
            if (wheel.GetGroundHit(out var Hit))
            {
                wheel.forwardFriction = UpdateFriction(wheel.forwardFriction);
                wheel.sidewaysFriction = UpdateFriction(wheel.sidewaysFriction);
                IsGrounded = true;
            }
        }

        private WheelFrictionCurve UpdateFriction(WheelFrictionCurve friction)
        {
            friction.stiffness = input.IsBraking 
                ? Mathf.SmoothDamp(friction.stiffness, .5f, ref driftVelocity, Time.deltaTime * 2f) : 1f;

            return friction;
        }

        private void UpdateWheelVisuals(WheelCollider collider)
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
