using DoggoCart;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.UI;

namespace DoggoCart
{
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] Button createLobbyButton;
        [SerializeField] Button joinLobbyButton;
        [SerializeField] SceneReference gameScene;

        void Awake()
        {
            createLobbyButton.onClick.AddListener(CreateGame);
            joinLobbyButton.onClick.AddListener(JoinGame);
        }

        async void CreateGame()
        {
            await Multiplayer.Instance.CreateLobby();
            Loader.LoadNetwork(gameScene);
        }

        async void JoinGame()
        {
            await Multiplayer.Instance.QuickJoinLobby();
        }
    }
}