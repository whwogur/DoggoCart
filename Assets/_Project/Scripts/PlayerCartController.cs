using TMPro;
using Unity.Cinemachine;
using UnityEngine;

namespace DoggoCart
{
    public class PlayerCartController : CartController
    {
        [SerializeField] CinemachineCamera playerCamera;
        [SerializeField] AudioListener audioListener;

        [Header("Netcode Debug")]
        [SerializeField] TextMeshProUGUI NetworkStatusText;
        [SerializeField] TextMeshProUGUI PlayerStatusText;
        [SerializeField] TextMeshProUGUI ServerRPCDebugText;
        [SerializeField] TextMeshProUGUI ClientRPCDebugText;
        


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

        protected override void Update()
        {
            base.Update();

            PlayerStatusText.SetText($"Owner: {IsOwner} NetworkObjectID: {NetworkObjectId} Velocity: {cartVelocity.magnitude:F1}");
        }
    }
}
