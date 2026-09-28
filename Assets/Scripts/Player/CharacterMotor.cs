using UnityEngine;

namespace Networking
{
    public readonly struct CharacterMotorState
    {
        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public float VerticalVelocity { get; }

        public float ControllerHeight { get; }

        public CharacterMotorState(
            Vector3 position,
            Quaternion rotation,
            float verticalVelocity,
            float controllerHeight)
        {
            Position = position;
            Rotation = rotation;
            VerticalVelocity = verticalVelocity;
            ControllerHeight = controllerHeight;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetObject))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField]
        private float walkSpeed = 3f;

        [SerializeField]
        private float sprintMultiplier = 2f;

        [SerializeField]
        private float jumpSpeed = 5f;

        [SerializeField]
        private float gravity = 9.81f;

        [SerializeField]
        private float groundedVelocity = -0.5f;

        [Header("Crouching")]
        [SerializeField]
        [Range(0.1f, 1f)]
        private float crouchSpeedMultiplier = 0.5f;

        [SerializeField]
        private float crouchHeight = 1f;

        [SerializeField]
        private float heightTransitionSpeed = 10f;

        [SerializeField]
        [Min(0f)]
        private float crouchStepOffset = 0.25f;

        private CharacterController _controller;

        private float _standingHeight;
        private Vector3 _standingCenter;
        private float _standingStepOffset;

        private float _verticalVelocity;

        public bool SimulationEnabled =>
            _controller != null &&
            _controller.enabled;

        public bool IsCrouching =>
            CrouchAmount > 0.01f;

        public float CrouchAmount
        {
            get
            {
                if (_controller == null)
                    return 0f;

                float heightDifference =
                    _standingHeight -
                    crouchHeight;

                if (heightDifference <= 0.001f)
                    return 0f;

                return Mathf.Clamp01(
                    (_standingHeight -
                     _controller.height) /
                    heightDifference);
            }
        }

        private void Awake()
        {
            _controller =
                GetComponent<CharacterController>();

            _standingHeight =
                _controller.height;

            _standingCenter =
                _controller.center;

            _standingStepOffset =
                _controller.stepOffset;

            // Remote replicas do not participate in collision
            // simulation. Server/prediction enables it as needed.
            _controller.enabled = false;
        }

        public void SetSimulationEnabled(bool enabled)
        {
            if (_controller == null)
                return;

            _controller.enabled = enabled;

            if (!enabled)
                _verticalVelocity = 0f;
        }

        public void Simulate(
            PlayerInputMessage message,
            float deltaTime)
        {
            if (!SimulationEnabled ||
                deltaTime <= 0f)
            {
                return;
            }

            transform.rotation =
                Quaternion.Euler(
                    0f,
                    message.Yaw,
                    0f);

            UpdateCrouchState(
                message.Crouch,
                deltaTime);

            float speed =
                walkSpeed;

            if (message.Crouch)
            {
                speed *=
                    crouchSpeedMultiplier;
            }
            else if (message.Sprint)
            {
                speed *=
                    sprintMultiplier;
            }

            Vector3 localMovement =
                new Vector3(
                    message.Move.x,
                    0f,
                    message.Move.y);

            Vector3 horizontalVelocity =
                transform.rotation *
                localMovement *
                speed;

            if (_controller.isGrounded)
            {
                _verticalVelocity =
                    groundedVelocity;

                // Jumping is allowed while crouching.
                if (message.Jump)
                {
                    _verticalVelocity =
                        jumpSpeed;
                }
            }
            else
            {
                _verticalVelocity -=
                    gravity *
                    deltaTime;
            }

            Vector3 velocity =
                horizontalVelocity;

            velocity.y =
                _verticalVelocity;

            _controller.Move(
                velocity *
                deltaTime);
        }

        public CharacterMotorState CaptureState()
        {
            return new CharacterMotorState(
                transform.position,
                transform.rotation,
                _verticalVelocity,
                _controller.height);
        }

        public void RestoreState(
            CharacterMotorState state)
        {
            if (_controller == null)
                return;

            bool wasEnabled =
                _controller.enabled;

            if (wasEnabled)
            {
                _controller.enabled =
                    false;
            }

            transform.SetPositionAndRotation(
                state.Position,
                state.Rotation);

            SetControllerHeight(
                state.ControllerHeight);

            UpdateStepOffset();

            if (wasEnabled)
            {
                _controller.enabled =
                    true;
            }

            _verticalVelocity =
                state.VerticalVelocity;
        }

        private void UpdateCrouchState(
            bool crouching,
            float deltaTime)
        {
            float targetHeight =
                crouching
                    ? crouchHeight
                    : _standingHeight;

            float newHeight =
                Mathf.MoveTowards(
                    _controller.height,
                    targetHeight,
                    heightTransitionSpeed *
                    deltaTime);

            SetControllerHeight(
                newHeight);

            UpdateStepOffset();
        }

        private void UpdateStepOffset()
        {
            if (_controller == null)
                return;

            _controller.stepOffset =
                IsCrouching
                    ? crouchStepOffset
                    : _standingStepOffset;
        }

        private void SetControllerHeight(
            float height)
        {
            float minimumHeight =
                _controller.radius * 2f;

            height =
                Mathf.Max(
                    height,
                    minimumHeight);

            _controller.height =
                height;

            // Keep the bottom of the CharacterController fixed.
            float standingBottom =
                _standingCenter.y -
                (_standingHeight * 0.5f);

            Vector3 center =
                _standingCenter;

            center.y =
                standingBottom +
                (height * 0.5f);

            _controller.center =
                center;
        }
    }
}