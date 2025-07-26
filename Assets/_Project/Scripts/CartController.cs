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

        const float thresholdSpeed = 10f;
        const float centerOfMassOffset = -0.5f;
        Vector3 initialCenterOfMass;

        public bool IsGrounded = true;
        public Vector3 Velocity => cartVelocity;
        public float MaxSpeed => maxSpeed;

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

        private void FixedUpdate()
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
                rigidBody.linearVelocity = Vector3.Lerp(rigidBody.linearVelocity, forwardWithOutY * targetSpeed, Time.deltaTime);
            }

            // 아래로 주는 힘
            float speedFactor = Mathf.Clamp01(rigidBody.linearVelocity.magnitude / maxSpeed);
            float lateralG = Mathf.Abs(Vector3.Dot(rigidBody.linearVelocity, transform.right));
            float downForceFactor = Mathf.Max(speedFactor, lateralG / lateralGScale);

            rigidBody.AddForce(-transform.up * downForce * rigidBody.mass * downForceFactor);

            // 무게중심 옮기기
            float speed = rigidBody.linearVelocity.magnitude;
            Vector3 centerOfMassAdjustment = (speed > thresholdSpeed) 
                ? new Vector3(0f, 0f, Mathf.Abs(verticalInput) > 0.1f 
                    ? Mathf.Sign(verticalInput) * centerOfMassOffset : 0f)
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
