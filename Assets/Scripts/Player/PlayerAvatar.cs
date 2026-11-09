using GameAudio;
using UnityEngine;
using UnityEngine.Rendering;
using World;
using World.Blocks;
using World.Rendering;

namespace Networking
{
    /// <summary>
    /// The visible voxel character of a player, animated in code:
    /// walk/run cycle, idle breathing, jump/fall pose, swimming strokes and
    /// crouching. Also plays footsteps (by surface), splashes, swim
    /// strokes and landing sounds.
    ///
    /// Animation is driven by how the object actually moves (its replicated
    /// position), so it works the same for the local player and for
    /// everyone else without extra network messages. The local player only
    /// casts a shadow (first-person camera).
    ///
    /// Added automatically by <see cref="CharacterMotor"/>; not created on a
    /// dedicated server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private const float WalkStride = 1.3f;    // metres per full walk cycle
        private const float StepDistance = 0.65f; // metres between footsteps

        private CharacterMotor _motor;
        private NetObject _netObject;
        private AudioSource _audio;

        private Transform _root, _hips, _torso, _head, _leftArm, _rightArm, _leftLeg, _rightLeg, _leftKnee, _rightKnee;
        private Renderer[] _renderers;
        private Transform _hand;
        private GameObject _heldModel;
        private Items.ItemData _heldItem;
        private Material _material;
        private bool _built;
        private bool _isLocal;
        private bool _localApplied;

        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private float _phase;
        private float _stepDistance;
        private float _swimStrokeTimer;
        private bool _wasSwimming;
        private bool _wasAirborne;
        private float _fallSpeed;
        private float _airTime;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _netObject = GetComponent<NetObject>();
        }

        private void Start()
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            // A dedicated server renders nothing and plays nothing.
            if (runtime != null && runtime.IsRunning && !runtime.RunsClient)
            {
                enabled = false;
                return;
            }

            Build();
            _lastPosition = transform.position;
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root.gameObject);
        }

        private void Build()
        {
            uint id = _netObject != null && _netObject.OwnerPeerId != 0 ? _netObject.OwnerPeerId : 1u;
            AvatarModel.Palette palette = AvatarModel.PaletteFor(id);

            Material material = Resources.Load<Material>("World/ChunkMaterial");

            // Under the presentation root if there is one (it hides
            // network corrections for the local player).
            Transform parent = transform.Find("PresentationRoot") ?? transform;

            CharacterController controller = GetComponent<CharacterController>();
            float feet = controller != null ? controller.center.y - controller.height * 0.5f : -1f;

            _root = new GameObject("Avatar").transform;
            _root.SetParent(parent, false);
            _root.localPosition = new Vector3(0f, feet, 0f);

            float v = AvatarModel.Voxel;

            _hips = Pivot("Hips", _root, new Vector3(0, AvatarModel.HipHeight, 0));
            _leftLeg = Part("Left Leg", _hips, new Vector3(-1.5f * v, 0, 0), AvatarModel.Thigh(palette), material);
            _rightLeg = Part("Right Leg", _hips, new Vector3(1.5f * v, 0, 0), AvatarModel.Thigh(palette), material);
            _leftKnee = Part("Left Knee", _leftLeg, new Vector3(0, -AvatarModel.ThighLength * v, 0), AvatarModel.Shin(palette), material);
            _rightKnee = Part("Right Knee", _rightLeg, new Vector3(0, -AvatarModel.ThighLength * v, 0), AvatarModel.Shin(palette), material);

            _torso = Part("Torso", _hips, Vector3.zero, AvatarModel.Torso(palette), material);
            float shoulder = (AvatarModel.TorsoHeight - 0.5f) * v;
            _leftArm = Part("Left Arm", _torso, new Vector3(-4f * v, shoulder, 0), AvatarModel.Arm(palette), material);
            _rightArm = Part("Right Arm", _torso, new Vector3(4f * v, shoulder, 0), AvatarModel.Arm(palette), material);
            _head = Part("Head", _torso, new Vector3(0, AvatarModel.TorsoHeight * v, 0), AvatarModel.Head(palette), material);

            // Grip point at the bottom of the right arm, for held items.
            _hand = Pivot("Hand", _rightArm, new Vector3(0f, -7.5f * v, 0.04f));
            _hand.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _material = material;

            _renderers = _root.GetComponentsInChildren<Renderer>(true);

            _audio = gameObject.AddComponent<AudioSource>();
            GameSounds.Configure3D(_audio);

            _built = true;
            RefreshLocal();
        }

        private static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static Transform Part(string name, Transform parent, Vector3 position, ChunkMeshData data, Material material)
        {
            Transform pivot = Pivot(name, parent, position);

            var mesh = new Mesh { name = "Avatar " + name };
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0, true);

            pivot.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = pivot.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            return pivot;
        }

        private void RefreshLocal()
        {
            bool local = _netObject == null || NetworkRuntime.Current == null || _netObject.IsLocallyOwned;

            if (_localApplied && local == _isLocal)
                return;

            _localApplied = true;
            _isLocal = local;

            // First person: you see your shadow but not your own body.
            foreach (Renderer r in _renderers)
                r.shadowCastingMode = local ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            // Your own footsteps are not positional.
            _audio.spatialBlend = local ? 0f : 1f;
            _audio.volume = local ? 0.55f : 1f;
        }

        private void LateUpdate()
        {
            if (!_built)
                return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            RefreshLocal();

            // Movement measured from the actual (replicated) position.
            Vector3 position = transform.position;
            Vector3 frameVelocity = (position - _lastPosition) / dt;
            _lastPosition = position;

            if (frameVelocity.sqrMagnitude > 400f)
                frameVelocity = Vector3.zero; // teleport / correction

            _velocity = Vector3.Lerp(_velocity, frameVelocity, 1f - Mathf.Exp(-12f * dt));

            Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            float speed = horizontal.magnitude;
            bool swimming = _motor != null && _motor.IsSwimming;
            bool airborne = !swimming && Mathf.Abs(_velocity.y) > 1.2f;
            float crouch = _motor != null ? _motor.CrouchAmount : 0f;

            _airTime = airborne ? _airTime + dt : 0f;

            UpdateHeldItem(swimming);
            Animate(dt, speed, swimming, airborne && _airTime > 0.08f, crouch);
            PlaySounds(dt, speed, swimming, airborne);
        }

        private void Animate(float dt, float speed, bool swimming, bool airborne, float crouch)
        {
            float t = Time.time;
            float move = Mathf.Clamp01(speed / 3f);       // walking
            float run = Mathf.Clamp01((speed - 3f) / 3f); // sprinting

            _phase += speed / WalkStride * Mathf.PI * 2f * dt;
            float swing = Mathf.Sin(_phase);

            float legAngle = swing * (30f * move + 15f * run);
            float armAngle = -swing * (25f * move + 20f * run);
            float bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.04f * move;
            float lean = 6f * run;
            float breathe = Mathf.Sin(t * 1.6f) * (1f - move);

            Quaternion body = Quaternion.Euler(lean + crouch * 25f, 0f, 0f);
            float hipsY = AvatarModel.HipHeight + bob - crouch * 0.3f;

            Vector3 leftArm = new Vector3(armAngle + breathe * 2f, 0f, -4f - breathe);
            Vector3 rightArm = new Vector3(-armAngle + breathe * 2f, 0f, 4f + breathe);

            // Holding something: carry it in front, swinging less.
            if (_heldModel != null)
                rightArm = new Vector3(-30f - armAngle * 0.3f, 0f, 4f);

            // Thighs swing; knees bend on the back swing (and when crouching).
            Vector3 leftLeg = new Vector3(-legAngle - crouch * 70f, 0f, 0f);
            Vector3 rightLeg = new Vector3(legAngle - crouch * 70f, 0f, 0f);
            float leftKnee = Mathf.Max(0f, Mathf.Sin(_phase + 1.2f)) * (40f * move + 25f * run) + crouch * 85f;
            float rightKnee = Mathf.Max(0f, Mathf.Sin(_phase + 1.2f + Mathf.PI)) * (40f * move + 25f * run) + crouch * 85f;

            if (swimming)
            {
                // Body tilts forward when swimming along; arms stroke, legs kick.
                float stroke = t * (2.5f + speed);
                body = Quaternion.Euler(Mathf.Lerp(15f, 70f, move), 0f, 0f);
                hipsY = AvatarModel.HipHeight;
                leftArm = new Vector3(-(stroke * Mathf.Rad2Deg) % 360f, 0f, -10f);
                rightArm = new Vector3(-(stroke * Mathf.Rad2Deg + 180f) % 360f, 0f, 10f);
                float kick = Mathf.Sin(t * 9f) * 18f;
                leftLeg = new Vector3(kick, 0f, 0f);
                rightLeg = new Vector3(-kick, 0f, 0f);
                leftKnee = 10f + kick * 0.5f;
                rightKnee = 10f - kick * 0.5f;
            }
            else if (airborne)
            {
                // Jump/fall: arms up, one knee forward.
                leftArm = new Vector3(-35f, 0f, -25f);
                rightArm = new Vector3(-35f, 0f, 25f);
                leftLeg = new Vector3(-45f, 0f, 0f);
                rightLeg = new Vector3(10f, 0f, 0f);
                leftKnee = 60f;
                rightKnee = 25f;
            }

            float blend = 1f - Mathf.Exp(-14f * dt);

            _hips.localPosition = Vector3.Lerp(_hips.localPosition, new Vector3(0f, hipsY, 0f), blend);
            _hips.localRotation = Quaternion.Slerp(_hips.localRotation, body, blend);
            _leftArm.localRotation = Quaternion.Slerp(_leftArm.localRotation, Quaternion.Euler(leftArm), swimming ? 1f : blend);
            _rightArm.localRotation = Quaternion.Slerp(_rightArm.localRotation, Quaternion.Euler(rightArm), swimming ? 1f : blend);
            _leftLeg.localRotation = Quaternion.Slerp(_leftLeg.localRotation, Quaternion.Euler(leftLeg), blend);
            _rightLeg.localRotation = Quaternion.Slerp(_rightLeg.localRotation, Quaternion.Euler(rightLeg), blend);
            _leftKnee.localRotation = Quaternion.Slerp(_leftKnee.localRotation, Quaternion.Euler(leftKnee, 0f, 0f), blend);
            _rightKnee.localRotation = Quaternion.Slerp(_rightKnee.localRotation, Quaternion.Euler(rightKnee, 0f, 0f), blend);

            // Keep the head roughly upright when the body tilts.
            _head.localRotation = Quaternion.Slerp(_head.localRotation, Quaternion.Inverse(body) * Quaternion.Euler(swimming ? 20f : 0f, 0f, 0f), blend);
        }

        private void PlaySounds(float dt, float speed, bool swimming, bool airborne)
        {
            WorldManager world = WorldManager.Active;

            if (swimming && !_wasSwimming && Mathf.Abs(_velocity.y) > 0.5f)
                _audio.PlayOneShot(GameSounds.Splash(), GameSounds.Volume);

            _wasSwimming = swimming;

            if (swimming)
            {
                _swimStrokeTimer -= dt * Mathf.Clamp01(speed / 2f);
                if (_swimStrokeTimer <= 0f && speed > 0.5f)
                {
                    _swimStrokeTimer = 0.7f;
                    _audio.PlayOneShot(GameSounds.SwimStroke(), GameSounds.Volume * 0.6f);
                }

                return;
            }

            // Landing thump after a real fall.
            if (_wasAirborne && !airborne && _fallSpeed > 4f && world != null)
                _audio.PlayOneShot(GameSounds.Footstep(SurfaceUnderFeet(world)), GameSounds.Volume * Mathf.Clamp01(_fallSpeed / 8f) * 1.3f);

            _fallSpeed = airborne ? Mathf.Max(_fallSpeed, -_velocity.y) : 0f;
            _wasAirborne = airborne;

            if (airborne || world == null)
                return;

            _stepDistance += speed * dt;

            float interval = speed > 4f ? StepDistance * 1.3f : StepDistance;
            if (_stepDistance >= interval)
            {
                _stepDistance = 0f;
                BlockId surface = SurfaceUnderFeet(world);
                float volume = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(speed / 5f)) * (_motor != null && _motor.IsCrouching ? 0.4f : 1f);

                _audio.pitch = Random.Range(0.9f, 1.1f);
                _audio.PlayOneShot(GameSounds.Footstep(surface), GameSounds.Volume * volume);
            }
        }

        /// <summary>
        /// Shows the item this player holds: our own selection for the local
        /// player (only its shadow is visible to us), and what the server
        /// says for everyone else.
        /// </summary>
        private void UpdateHeldItem(bool swimming)
        {
            Items.ItemData item = null;

            if (_isLocal)
            {
                Items.PlayerInventory inventory = GetComponent<Items.PlayerInventory>();
                item = inventory != null ? inventory.SelectedStack.Item : null;
            }
            else if (_netObject != null)
            {
                ClientGameplayModule gameplay = NetworkRuntime.Current != null ? NetworkRuntime.Current.ClientGameplay : null;
                item = gameplay != null ? gameplay.GetHeldItem(_netObject.OwnerPeerId) : null;
            }

            if (item != _heldItem || (item != null && _heldModel == null))
            {
                _heldItem = item;

                if (_heldModel != null)
                    Destroy(_heldModel);

                _heldModel = HeldItemModel.Create(item, _hand, _material, castShadows: true);

                if (_heldModel != null && _isLocal)
                {
                    foreach (Renderer r in _heldModel.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }

            if (_heldModel != null)
                _heldModel.SetActive(!swimming);
        }

        private BlockId SurfaceUnderFeet(WorldManager world)
        {
            Vector3 feet = _root != null ? _root.position : transform.position;

            // Shallow water splashes; otherwise the block under the feet.
            if (world.TryGetLoadedBlockAt(feet + Vector3.up * 0.1f, out BlockId at) && BlockRegistry.IsLiquid(at))
                return BlockId.Water;

            world.TryGetLoadedBlockAt(feet + Vector3.down * 0.1f, out BlockId below);
            return below == BlockId.Air ? BlockId.Grass : below;
        }
    }
}
