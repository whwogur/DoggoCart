using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Util;

namespace DoggoCart
{
    [System.Serializable]
    public enum EncryptionType
    {
        DTLS, // Datagram Transport Layer Security
        WSS  // Web Socket Secure
    }

    public class Multiplayer : MonoBehaviour
    {
        [SerializeField] string lobbyName = "Lobby";
        [SerializeField] int maxPlayers = 4;
        [SerializeField] EncryptionType encryption = EncryptionType.DTLS;

        const string KEY_JOIN_CODE = "JoinCode";

        CountdownTimer heartbeatTimer = new CountdownTimer(20f);
        CountdownTimer pollTimer = new CountdownTimer(65f);

        Lobby currentLobby;
        public static Multiplayer Instance { get; private set; }
        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; }

        string connectionType => encryption == EncryptionType.DTLS ? "dtls" : "wss";

        async void Start()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            await UnityServices.InitializeAsync(new InitializationOptions().SetProfile("Player" + Random.Range(0, 1000)));
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            PlayerId = AuthenticationService.Instance.PlayerId;
            PlayerName = AuthenticationService.Instance.Profile;
            Debug.Log($"Signed in {PlayerName} ({PlayerId})");

            heartbeatTimer.OnTimerStop += async () => {
                await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
                heartbeatTimer.Start();
            };
            pollTimer.OnTimerStop += async () => {
                currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                pollTimer.Start();
            };
        }

        public async Task CreateLobby()
        {
            try
            {
                currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, new CreateLobbyOptions { Data = null });
                heartbeatTimer.Start();
                pollTimer.Start();

                var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject> {
                        { KEY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                    }
                });

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

                NetworkManager.Singleton.StartHost();
                Debug.Log($"Host started. JoinCode: {joinCode}");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"CreateLobby failed: {e}");
            }
        }

        public async Task QuickJoinLobby()
        {
            try
            {
                currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                pollTimer.Start();

                string joinCode = currentLobby.Data[KEY_JOIN_CODE].Value;
                var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

                NetworkManager.Singleton.StartClient();
                Debug.Log("Client started");
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"QuickJoinLobby failed: {e}");
            }
        }
    }
}
