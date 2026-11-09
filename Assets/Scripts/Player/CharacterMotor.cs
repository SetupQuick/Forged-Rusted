using UnityEngine;
using World;
using World.Blocks;

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

    /// <summary>
    /// Player movement, simulated once per network tick on the server
    /// (authoritative) and on the owning client (prediction).
    ///
    /// Everything that decides movement must give the same result on
    /// both sides. Water and headroom checks therefore read block data
    /// from the world (identical on server and client because both
    /// generate it from the same seed) instead of physics queries.
    /// </summary>
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

        [Header("Swimming")]
        [Tooltip("Horizontal speed in water, relative to walking.")]
        [SerializeField]
        [Range(0.1f, 1.5f)]
        private float swimSpeedMultiplier = 0.7f;

        [Tooltip("Extra multiplier while sprinting in water.")]
        [SerializeField]
        [Min(1f)]
        private float swimSprintMultiplier = 1.4f;

        [Tooltip("Upward speed while holding jump in water.")]
        [SerializeField]
        private float swimUpSpeed = 3f;

        [Tooltip("Downward speed while holding crouch in water.")]
        [SerializeField]
        private float diveSpeed = 3f;

        [Tooltip("Speed at which a submerged player floats up when idle.")]
        [SerializeField]
        private float floatSpeed = 1f;

        [Tooltip("How quickly vertical speed changes in water.")]
        [SerializeField]
        private float waterAcceleration = 10f;

        [Tooltip("Upward speed when climbing out onto a ledge.")]
        [SerializeField]
        private float waterExitSpeed = 5.5f;

        [Tooltip("Walking speed multiplier in shallow (knee-deep) water.")]
        [SerializeField]
        [Range(0.1f, 1f)]
        private float wadeSpeedMultiplier = 0.8f;

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

        /// <summary>Waist-deep or deeper; the player is swimming.</summary>
        public bool IsSwimming { get; private set; }

        /// <summary>Eyes below the water surface (for HUD/effects).</summary>
        public bool IsHeadUnderwater { get; private set; }

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

        private void Update()
        {
            // Remote replicas are not simulated, but the HUD and effects
            // still want to know about water. Cheap block lookups.
            if (!SimulationEnabled)
                SampleWater(out _, out _, out _);
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

            SampleWater(
                out bool feetInWater,
                out bool waistInWater,
                out bool eyesInWater);

            bool swimming = waistInWater;

            // Crouch means "dive" while swimming, so stay full height.
            UpdateCrouchState(
                message.Crouch && !swimming,
                deltaTime);

            float speed =
                walkSpeed;

            if (swimming)
            {
                speed *= swimSpeedMultiplier;

                if (message.Sprint)
                    speed *= swimSprintMultiplier;
            }
            else
            {
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

                if (feetInWater)
                    speed *= wadeSpeedMultiplier;
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

            if (swimming)
            {
                UpdateSwimmingVelocity(
                    message,
                    horizontalVelocity,
                    eyesInWater,
                    deltaTime);
            }
            else if (_controller.isGrounded)
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

        private void UpdateSwimmingVelocity(
            PlayerInputMessage message,
            Vector3 horizontalVelocity,
            bool eyesInWater,
            float deltaTime)
        {
            float target;

            if (message.Crouch)
                target = -diveSpeed;
            else if (message.JumpHeld || message.Jump)
                target = swimUpSpeed;
            else if (eyesInWater)
                target = floatSpeed;
            else
                target = 0f; // bob at the surface

            _verticalVelocity =
                Mathf.MoveTowards(
                    _verticalVelocity,
                    target,
                    waterAcceleration *
                    deltaTime);

            // Climb out: at the surface, holding jump and swimming into
            // a block with air above it - hop up onto the ledge.
            if (!eyesInWater &&
                (message.JumpHeld || message.Jump) &&
                horizontalVelocity.sqrMagnitude > 0.01f &&
                IsLedgeAhead(horizontalVelocity))
            {
                _verticalVelocity =
                    waterExitSpeed;
            }
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

        // ------------------------------------------------------------
        // World queries (block data, deterministic on server and client)
        // ------------------------------------------------------------

        private float BottomY =>
            transform.position.y +
            _controller.center.y -
            _controller.height * 0.5f;

        private void SampleWater(
            out bool feet,
            out bool waist,
            out bool eyes)
        {
            feet = false;
            waist = false;
            eyes = false;

            if (_controller == null)
                return;

            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady)
            {
                IsSwimming = false;
                IsHeadUnderwater = false;
                return;
            }

            Vector3 position = transform.position;
            float bottom = BottomY;
            float height = _controller.height;

            feet = IsWater(world, position.x, bottom + 0.2f, position.z);
            waist = IsWater(world, position.x, bottom + height * 0.5f, position.z);
            eyes = IsWater(world, position.x, bottom + height - 0.15f, position.z);

            IsSwimming = waist;
            IsHeadUnderwater = eyes;
        }

        private static bool IsWater(WorldManager world, float x, float y, float z)
        {
            world.TryGetLoadedBlock(
                Mathf.FloorToInt(x),
                Mathf.FloorToInt(y),
                Mathf.FloorToInt(z),
                out BlockId block);

            return BlockRegistry.IsLiquid(block);
        }

        private static bool IsSolid(WorldManager world, float x, float y, float z)
        {
            // Unloaded chunks count as solid: never move into the unknown.
            if (!world.TryGetLoadedBlock(
                    Mathf.FloorToInt(x),
                    Mathf.FloorToInt(y),
                    Mathf.FloorToInt(z),
                    out BlockId block))
            {
                return true;
            }

            return BlockRegistry.IsSolid(block);
        }

        /// <summary>
        /// A solid block just in front at water level with free space
        /// above it, i.e. somewhere to climb out of the water.
        /// </summary>
        private bool IsLedgeAhead(Vector3 horizontalVelocity)
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady)
                return false;

            Vector3 direction = horizontalVelocity;
            direction.y = 0f;
            direction = direction.normalized;

            Vector3 probe =
                transform.position +
                direction * (_controller.radius + 0.35f);

            float bottom = BottomY;

            bool blocked =
                IsSolid(world, probe.x, bottom + 0.5f, probe.z) ||
                IsSolid(world, probe.x, bottom + 1.0f, probe.z);

            bool spaceAbove =
                !IsSolid(world, probe.x, bottom + _standingHeight + 0.6f, probe.z);

            return blocked && spaceAbove;
        }

        /// <summary>
        /// True when the standing capsule would not overlap any solid
        /// block, so the player may stand up from a crouch.
        /// </summary>
        private bool HasHeadroom(float targetHeight)
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady)
                return true;

            const float epsilon = 0.01f;

            Vector3 position = transform.position;
            float radius = _controller.radius - epsilon;
            float from = BottomY + _controller.height;
            float to = BottomY + targetHeight;

            int minX = Mathf.FloorToInt(position.x - radius);
            int maxX = Mathf.FloorToInt(position.x + radius);
            int minZ = Mathf.FloorToInt(position.z - radius);
            int maxZ = Mathf.FloorToInt(position.z + radius);
            int minY = Mathf.FloorToInt(from - epsilon);
            int maxY = Mathf.FloorToInt(to - epsilon);

            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                if (world.TryGetLoadedBlock(x, y, z, out BlockId block) &&
                    BlockRegistry.IsSolid(block))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------
        // Crouching
        // ------------------------------------------------------------

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

            // Stay crouched under low ceilings instead of standing up
            // into the blocks above.
            if (newHeight > _controller.height &&
                !HasHeadroom(newHeight))
            {
                newHeight = _controller.height;
            }

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
