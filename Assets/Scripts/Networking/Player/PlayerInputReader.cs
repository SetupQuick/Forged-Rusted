using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetObject))]
    [RequireComponent(typeof(PlayerCameraRig))]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField]
        private InputActionAsset playerControls;

        [SerializeField]
        private string actionMapName = "Player";

        [Header("Look")]
        [SerializeField]
        private PlayerCameraRig cameraRig;

        [SerializeField]
        private float mouseSensitivity = 2f;

        [SerializeField]
        private float verticalLookLimit = 80f;

        [SerializeField]
        private bool lockCursor = true;

        private InputActionAsset _controlsInstance;
        private InputActionMap _playerMap;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;

        private float _yaw;
        private float _pitch;

        private int _jumpSendTicks;
        private bool _active;

        public bool IsActive => _active;

        private void Awake()
        {
            if (cameraRig == null)
            {
                cameraRig =
                    GetComponent<PlayerCameraRig>();
            }
        }

        public void Activate()
        {
            if (_active)
                return;

            if (playerControls == null)
            {
                throw new InvalidOperationException(
                    "Assign an InputActionAsset to PlayerInputReader.");
            }

            if (cameraRig == null)
            {
                throw new InvalidOperationException(
                    "Assign PlayerCameraRig to PlayerInputReader.");
            }

            _controlsInstance =
                Instantiate(playerControls);

            _playerMap =
                _controlsInstance.FindActionMap(
                    actionMapName,
                    true);

            _moveAction =
                _playerMap.FindAction(
                    "Move",
                    true);

            _lookAction =
                _playerMap.FindAction(
                    "Look",
                    true);

            _jumpAction =
                _playerMap.FindAction(
                    "Jump",
                    true);

            _sprintAction =
                _playerMap.FindAction(
                    "Sprint",
                    true);

            _crouchAction =
                _playerMap.FindAction(
                    "Crouch",
                    true);

            _yaw =
                transform.eulerAngles.y;

            _pitch =
                cameraRig.Pitch;

            _playerMap.Enable();
            _active = true;

            cameraRig.Activate();

            if (lockCursor)
            {
                Cursor.lockState =
                    CursorLockMode.Locked;

                Cursor.visible = false;
            }
        }

        public void Deactivate()
        {
            if (!_active &&
                _controlsInstance == null)
            {
                cameraRig?.Deactivate();
                return;
            }

            _active = false;

            _playerMap?.Disable();

            if (_controlsInstance != null)
                Destroy(_controlsInstance);

            _controlsInstance = null;
            _playerMap = null;

            _moveAction = null;
            _lookAction = null;
            _jumpAction = null;
            _sprintAction = null;
            _crouchAction = null;

            _jumpSendTicks = 0;

            cameraRig?.Deactivate();

            if (lockCursor)
            {
                Cursor.lockState =
                    CursorLockMode.None;

                Cursor.visible = true;
            }
        }

        public PlayerInputMessage BuildMessage(
            uint networkId,
            uint inputSequence)
        {
            if (!_active)
            {
                return new PlayerInputMessage(
                    networkId,
                    inputSequence,
                    Vector2.zero,
                    _yaw,
                    PlayerInputButtons.None);
            }

            PlayerInputButtons buttons =
                PlayerInputButtons.None;

            if (_jumpSendTicks > 0)
            {
                buttons |=
                    PlayerInputButtons.Jump;

                _jumpSendTicks--;
            }

            if (_sprintAction.IsPressed())
            {
                buttons |=
                    PlayerInputButtons.Sprint;
            }

            if (_crouchAction.IsPressed())
            {
                buttons |=
                    PlayerInputButtons.Crouch;
            }

            return new PlayerInputMessage(
                networkId,
                inputSequence,
                _moveAction.ReadValue<Vector2>(),
                _yaw,
                buttons);
        }

        private void Update()
        {
            if (!_active)
                return;

            if (_jumpAction.WasPressedThisFrame())
                _jumpSendTicks = 3;

            Vector2 look =
                _lookAction.ReadValue<Vector2>();

            _yaw +=
                look.x *
                mouseSensitivity;

            _pitch -=
                look.y *
                mouseSensitivity;

            _pitch =
                Mathf.Clamp(
                    _pitch,
                    -verticalLookLimit,
                    verticalLookLimit);

            // Yaw rotates the simulation root.
            transform.rotation =
                Quaternion.Euler(
                    0f,
                    _yaw,
                    0f);

            // Pitch affects only the camera target.
            cameraRig.SetPitch(_pitch);
        }

        private void OnDestroy()
        {
            Deactivate();
        }
    }
}