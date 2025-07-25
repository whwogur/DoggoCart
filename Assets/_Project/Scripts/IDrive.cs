using UnityEngine;

namespace DoggoCart
{
    public interface IDrive
    {
        Vector2 Move { get; }
        bool IsBraking { get; }
        void Enable();
    }
}
