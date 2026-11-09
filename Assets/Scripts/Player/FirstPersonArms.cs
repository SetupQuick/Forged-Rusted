using Gameplay;
using Items;
using UnityEngine;
using UnityEngine.Rendering;
using World.Rendering;

namespace Networking
{
    /// <summary>
    /// The local player's arms in first person, holding the selected item.
    /// Built from the same voxel parts and colours as the player's
    /// character, and animated in code:
    ///   walking/running - arms bob with the steps, lower while sprinting;
    ///   looking around  - arms lag slightly behind the camera (sway);
    ///   jumping/landing - lift while falling, dip on landing;
    ///   crouching       - a little lower;
    ///   breaking/placing- a quick swing;
    ///   swimming        - both arms do breaststrokes, the item is put away.
    ///
    /// The whole rig is scaled down and kept within ~0.25 m of the eye, so
    /// it never pokes into walls (the player is 0.6 m wide) and needs no
    /// extra camera or layer. Added to the local player by PlayerInventory.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonArms : MonoBehaviour
    {
        // Rig scale: modelled at real size, shown at this fraction close to the eye.
        private const float RigScale = 0.35f;

        // Rest pose (in unscaled rig space, metres).
        // Shoulder below and slightly behind the eye (off screen); the arm
        // points forward and a little up, so the hand sits in the lower right.
        private static readonly Vector3 RightShoulder = new Vector3(0.42f, -0.48f, 0.15f);
        private static readonly Vector3 LeftShoulder = new Vector3(-0.42f, -0.48f, 0.15f);
        private static readonly Vector3 RightArmRest = new Vector3(-110f, -14f, 0f);

        // Held items are shown a bit smaller than in the world so they do
        // not cover the view.
        private const float HeldScale = 0.55f;

        private const float SwingDuration = 0.28f;
        private const float WalkStride = 1.3f;

        private CharacterMotor _motor;
        private PlayerInventory _inventory;
        private Camera _camera;

        private Transform _rig;
        private Transform _rightArm;
        private Transform _leftArm;
        private Transform _hand;
        private GameObject _heldModel;
        private ItemData _heldItem;
        private Material _material;

        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private float _phase;
        private float _swingTime = 1f;
        private float _landDip;
        private bool _wasAirborne;
        private float _fallSpeed;
        private Quaternion _lastCameraRotation;
        private Vector2 _sway;
        private float _swimBlend;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _inventory = GetComponent<PlayerInventory>();
        }

        private void OnEnable()
        {
            if (_inventory != null)
                _inventory.ActionPerformed += OnActionPerformed;
        }

        private void OnDisable()
        {
            if (_inventory != null)
                _inventory.ActionPerformed -= OnActionPerformed;
        }

        private void OnDestroy()
        {
            if (_rig != null)
                Destroy(_rig.gameObject);
        }

        private void OnActionPerformed(PlayerActionType type)
        {
            _swingTime = 0f;
        }

        private void Build(Camera camera)
        {
            if (_rig != null)
                Destroy(_rig.gameObject);

            _camera = camera;
            _material = Resources.Load<Material>("World/ChunkMaterial");

            NetObject netObject = GetComponent<NetObject>();
            uint id = netObject != null && netObject.OwnerPeerId != 0 ? netObject.OwnerPeerId : 1u;
            AvatarModel.Palette palette = AvatarModel.PaletteFor(id);

            _rig = new GameObject("First Person Arms").transform;
            _rig.SetParent(camera.transform, false);
            _rig.localScale = Vector3.one * RigScale;

            _rightArm = Part("Right Arm", _rig, RightShoulder, AvatarModel.Arm(palette));
            _leftArm = Part("Left Arm", _rig, LeftShoulder, AvatarModel.Arm(palette));

            // The grip point at the bottom of the right arm.
            _hand = new GameObject("Hand").transform;
            _hand.SetParent(_rightArm, false);
            _hand.localPosition = new Vector3(0f, -7.5f * AvatarModel.Voxel, 0.06f);
            _hand.localRotation = Quaternion.Euler(110f, 14f, 0f);
            _hand.localScale = Vector3.one * HeldScale;

            _heldItem = null;
            _lastPosition = transform.position;
            _lastCameraRotation = camera.transform.rotation;
        }

        private Transform Part(string name, Transform parent, Vector3 position, World.Rendering.ChunkMeshData data)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var mesh = new Mesh { name = "FP " + name };
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0, true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; // the body's shadow is drawn by PlayerAvatar
            renderer.receiveShadows = true;

            return go.transform;
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera == null)
                return;

            if (_rig == null || _camera != camera)
                Build(camera);

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            UpdateHeldItem();

            // Movement from the actual position (same as the third-person avatar).
            Vector3 position = transform.position;
            Vector3 frameVelocity = (position - _lastPosition) / dt;
            _lastPosition = position;

            if (frameVelocity.sqrMagnitude > 400f)
                frameVelocity = Vector3.zero;

            _velocity = Vector3.Lerp(_velocity, frameVelocity, 1f - Mathf.Exp(-12f * dt));

            float speed = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            bool swimming = _motor != null && _motor.IsSwimming;
            bool airborne = !swimming && Mathf.Abs(_velocity.y) > 1.2f;
            float crouch = _motor != null ? _motor.CrouchAmount : 0f;

            // Landing dip.
            if (_wasAirborne && !airborne && _fallSpeed > 3f)
                _landDip = Mathf.Clamp(_fallSpeed * 0.012f, 0f, 0.09f);

            _fallSpeed = airborne ? Mathf.Max(_fallSpeed, -_velocity.y) : 0f;
            _wasAirborne = airborne;
            _landDip = Mathf.MoveTowards(_landDip, 0f, dt * 0.4f);

            // Sway: arms lag behind camera turns.
            Quaternion cameraRotation = camera.transform.rotation;
            Quaternion delta = cameraRotation * Quaternion.Inverse(_lastCameraRotation);
            _lastCameraRotation = cameraRotation;

            Vector3 euler = delta.eulerAngles;
            float yawDelta = Mathf.DeltaAngle(0f, euler.y);
            float pitchDelta = Mathf.DeltaAngle(0f, euler.x);
            Vector2 targetSway = new Vector2(Mathf.Clamp(-yawDelta * 0.6f, -6f, 6f), Mathf.Clamp(-pitchDelta * 0.6f, -6f, 6f));
            _sway = Vector2.Lerp(_sway, targetSway, 1f - Mathf.Exp(-10f * dt));

            _swimBlend = Mathf.MoveTowards(_swimBlend, swimming ? 1f : 0f, dt * 4f);
            _swingTime += dt / SwingDuration;

            Animate(dt, speed, crouch, airborne);
        }

        private void Animate(float dt, float speed, float crouch, bool airborne)
        {
            float t = Time.time;
            float move = Mathf.Clamp01(speed / 3f);
            float run = Mathf.Clamp01((speed - 3f) / 3f);

            if (!airborne && _swimBlend < 0.5f)
                _phase += speed / WalkStride * Mathf.PI * 2f * dt;

            // Rig offset: step bob (figure eight), breathing, crouch, landing, falling.
            float bobX = Mathf.Sin(_phase) * (0.025f * move + 0.02f * run);
            float bobY = -Mathf.Abs(Mathf.Cos(_phase)) * (0.03f * move + 0.03f * run);
            float breathe = Mathf.Sin(t * 1.6f) * 0.006f * (1f - move);
            float fall = airborne ? Mathf.Clamp(-_velocity.y * 0.01f, -0.04f, 0.06f) : 0f;

            Vector3 offset = new Vector3(bobX, bobY + breathe - crouch * 0.03f - run * 0.04f - _landDip + fall, -run * 0.03f);
            _rig.localPosition = offset * RigScale;
            _rig.localRotation = Quaternion.Euler(_sway.y, _sway.x, -bobX * 60f);

            // Right arm: rest pose, sprint tilt, swing.
            Vector3 right = RightArmRest + new Vector3(18f * run, 0f, 0f);

            if (_swingTime < 1f)
            {
                // A quick jab in towards the crosshair, with a twist.
                float s = Mathf.Sin(_swingTime * Mathf.PI);
                right += new Vector3(-10f * s, -24f * s, -28f * s);
            }

            Quaternion rightLand = Quaternion.Euler(right);
            Quaternion leftLand = Quaternion.Euler(-60f, 10f, 0f); // hidden below the view

            // Swimming: breaststroke with both arms; the item is put away.
            float stroke = t * 3.2f;
            Quaternion rightSwim = Quaternion.Euler(-118f + Mathf.Sin(stroke) * 15f, -24f - Mathf.Cos(stroke) * 30f, 0f);
            Quaternion leftSwim = Quaternion.Euler(-118f + Mathf.Sin(stroke) * 15f, 24f + Mathf.Cos(stroke) * 30f, 0f);

            _rightArm.localRotation = Quaternion.Slerp(rightLand, rightSwim, _swimBlend);
            _leftArm.localRotation = Quaternion.Slerp(leftLand, leftSwim, _swimBlend);
            _leftArm.localPosition = LeftShoulder + new Vector3(0f, -0.45f * (1f - _swimBlend), 0f);

            if (_heldModel != null)
                _heldModel.SetActive(_swimBlend < 0.5f);
        }

        private void UpdateHeldItem()
        {
            ItemData item = _inventory != null ? _inventory.SelectedStack.Item : null;

            if (item == _heldItem && (_heldModel != null || item == null))
                return;

            _heldItem = item;

            if (_heldModel != null)
                Destroy(_heldModel);

            _heldModel = HeldItemModel.Create(item, _hand, _material, castShadows: false);

            // Swing the new item into view.
            if (_heldModel != null)
                _swingTime = 0.5f;
        }
    }
}
