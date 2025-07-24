using UnityEngine;

namespace Util.Extension
{
    public static class GameObjectExtensions
    {
        public static T GetOrAdd<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return null != component ? component : gameObject.AddComponent<T>();
        }
    }
}