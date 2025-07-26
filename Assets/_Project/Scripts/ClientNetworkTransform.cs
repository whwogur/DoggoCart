using Unity.Netcode.Components;
using UnityEngine;

namespace DoggoCart
{
    public enum AuthorityMode
    {
        Server,
        Client
    }
    
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        public AuthorityMode authorityMode = DoggoCart.AuthorityMode.Client;

        protected override bool OnIsServerAuthoritative() => authorityMode == DoggoCart.AuthorityMode.Server;
    }
}