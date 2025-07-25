using UnityEngine;

namespace DoggoCart
{
    [CreateAssetMenu(fileName = "AIDriverData", menuName = "Cart/AIDriverData")]
    public class AIDriverData : ScriptableObject
    {
        public float proximityThreshold = 20.0f; // 경유 지점에서 얼마나 떨어져있을 때 방문 처리 할 것인지
        public float updateCornerRange = 50f; // 코너에서 얼마나 떨어져있을 때 코너 도착 처리 할 것인지
        public float brakeRange = 80f; // 코너에서 얼마나 떨어져있을 때 브레이크 밟을 것인지
        public float spinThreshold = 100f; // 반대 방향으로 스티어링 시작 할 각속도
        public float speedWhileDrifting = 0.5f;
        public float timeToDrift = 0.5f;
    }
}
