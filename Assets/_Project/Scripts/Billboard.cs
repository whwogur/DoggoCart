using UnityEngine;

namespace DoggoCart
{
    public class Billboard : MonoBehaviour
    {
        [SerializeField] Transform cartCamera;

        void Update()
        {
            if (cartCamera)
            {
                transform.rotation = cartCamera.rotation;
            }
        }
    }
}