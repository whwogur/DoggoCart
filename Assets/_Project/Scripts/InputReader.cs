using UnityEngine;
using UnityEngine.InputSystem;
using static PlayerInputActions;

namespace DoggoCart
{
    [CreateAssetMenu(fileName = "InputReader", menuName = "Cart/Input Reader")]
    public class InputReader : ScriptableObject, IPlayerActions
    {
        public Vector3 Move => inputActions.Player.Move.ReadValue<Vector2>();
        public bool IsBraking => inputActions.Player.Brake.ReadValue<float>() > 0;

        PlayerInputActions inputActions;

        private void OnEnable()
        {
            if (null == inputActions)
            {
                inputActions = new PlayerInputActions();
                inputActions.Player.SetCallbacks(this);
            }
            inputActions.Enable();
        }

        public void Enable()
        {
            inputActions.Enable();
        }

        private void OnDisable()
        {
            if (null != inputActions)
            {
                inputActions.Disable();
                if (Application.isPlaying)
                {
                    inputActions.Dispose();
                }
            }
        }


        public void OnBrake(InputAction.CallbackContext context)
        {
            //
        }

        public void OnFire(InputAction.CallbackContext context)
        {
        }

        public void OnLook(InputAction.CallbackContext context)
        {
        }

        public void OnMove(InputAction.CallbackContext context)
        {
        }
    }
}
