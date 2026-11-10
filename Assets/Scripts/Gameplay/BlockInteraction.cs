using Items;
using Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Blocks;

namespace Gameplay
{
    /// <summary>
    /// Aiming at voxels, breaking (hold left mouse) and placing the
    /// selected hotbar block (hold right mouse). Draws an outline around
    /// the targeted voxel. Added at runtime to the local player by
    /// <see cref="PlayerInventory"/>.
    ///
    /// Edits go through <see cref="PlayerInventory"/>, which predicts them
    /// and sends them to the server for validation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockInteraction : MonoBehaviour
    {
        private PlayerInventory _inventory;
        private CharacterController _controller;
        private Camera _camera;
        private GameObject _outline;
        private float _breakCooldown;
        private float _placeCooldown;

        /// <summary>The voxel the player is looking at, if any.</summary>
        public bool HasTarget { get; private set; }

        /// <summary>0..1 while holding the button on a block (for the HUD and crack overlay).</summary>
        public float BreakProgress { get; private set; }

        /// <summary>Seconds the current block takes with the held tool (0 = instant).</summary>
        public float BreakSeconds { get; private set; }

        private Vector3Int _breakingVoxel;
        private bool _breaking;

        public VoxelHit Target { get; private set; }

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _controller = GetComponent<CharacterController>();
        }

        private void OnDestroy()
        {
            if (_crack != null)
                Destroy(_crack);
            if (_crackMaterial != null)
                Destroy(_crackMaterial);

            if (_outline != null)
                Destroy(_outline);
        }

        private void Update()
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady || _inventory == null || PlayerInputReader.GameplayInputBlocked)
            {
                SetTarget(false, default, world);
                StopBreaking();
                UpdateCrack(world);
                return;
            }

            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;

            if (_camera == null)
                return;

            Transform view = _camera.transform;

            bool hasHit = VoxelRaycast.Cast(
                view.position,
                view.forward,
                BlockEditRules.ReachMetres,
                world.VoxelSize,
                (x, y, z) => world.TryGetLoadedBlock(x, y, z, out BlockId b) ? b : BlockId.Air,
                out VoxelHit hit);

            SetTarget(hasHit, hit, world);
            UpdateCrack(world);

            _breakCooldown -= Time.deltaTime;
            _placeCooldown -= Time.deltaTime;

            Mouse mouse = Mouse.current;

            if (mouse == null || !HasTarget)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
                _placeCooldown = 0f;

            if (mouse.leftButton.isPressed && _breakCooldown <= 0f)
            {
                UpdateBreaking(Target);
            }
            else
            {
                StopBreaking();
            }

            if (!mouse.leftButton.isPressed && mouse.rightButton.isPressed && _placeCooldown <= 0f)
            {
                _placeCooldown = BlockEditRules.RepeatInterval;
                TryPlace(world);
            }
        }

        /// <summary>
        /// Holding the button fills the progress for the block under the
        /// cursor; looking at another block starts over. The time comes
        /// from Gameplay.Mining (block hardness and the held tool).
        /// </summary>
        private void UpdateBreaking(VoxelHit target)
        {
            ItemData tool = _inventory.SelectedStack.Item;
            float seconds = Mining.BreakSeconds(target.Block, tool);

            if (seconds < 0f || !BlockEditRules.CanBreak(target.Block))
            {
                StopBreaking();
                return;
            }

            if (!_breaking || target.Voxel != _breakingVoxel)
            {
                _breaking = true;
                _breakingVoxel = target.Voxel;
                BreakProgress = 0f;

                if (seconds > 0f)
                    _inventory.StartBreaking(target.Voxel);
            }

            BreakSeconds = seconds;
            BreakProgress = seconds <= 0f ? 1f : Mathf.Min(1f, BreakProgress + Time.deltaTime / seconds);

            if (BreakProgress >= 1f)
            {
                _inventory.TryBreak(target.Voxel);
                StopBreaking();

                // A short pause before the next block, like swinging again.
                _breakCooldown = seconds <= 0f ? BlockEditRules.RepeatInterval : 0.15f;
            }
        }

        private GameObject _crack;
        private Material _crackMaterial;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The block darkens as it breaks (a stand-in for crack textures).</summary>
        private void UpdateCrack(WorldManager world)
        {
            bool show = _breaking && BreakProgress > 0f && world != null;

            if (!show)
            {
                if (_crack != null && _crack.activeSelf)
                    _crack.SetActive(false);
                return;
            }

            if (_crack == null)
            {
                _crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _crack.name = "Break Progress";
                Destroy(_crack.GetComponent<Collider>());

                Material highlight = Resources.Load<Material>("World/HighlightMaterial");
                _crackMaterial = highlight != null ? new Material(highlight) : null;
                Renderer renderer = _crack.GetComponent<Renderer>();
                renderer.sharedMaterial = _crackMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            float size = world.VoxelSize;
            _crack.SetActive(true);
            _crack.transform.position = new Vector3((_breakingVoxel.x + 0.5f) * size, (_breakingVoxel.y + 0.5f) * size, (_breakingVoxel.z + 0.5f) * size);
            _crack.transform.localScale = Vector3.one * (size * 1.004f);

            if (_crackMaterial != null)
            {
                // Steps like cracks spreading, not a smooth fade.
                float stage = Mathf.Ceil(BreakProgress * 8f) / 8f;
                _crackMaterial.SetColor(ColorId, new Color(0f, 0f, 0f, stage * 0.55f));
            }
        }

        private void StopBreaking()
        {
            _breaking = false;
            BreakProgress = 0f;
        }

        private void TryPlace(WorldManager world)
        {
            VoxelHit hit = Target;

            // Clicking grass/flowers replaces them; otherwise place against the face.
            Vector3Int voxel = BlockEditRules.CanPlaceInto(hit.Block) ? hit.Voxel : hit.Voxel + hit.Normal;

            if (hit.Normal == Vector3Int.zero && !BlockEditRules.CanPlaceInto(hit.Block))
                return;

            // Do not place a solid block inside ourselves.
            if (_controller != null &&
                InventoryLogic.TryGetPlaceableBlock(_inventory.Container, _inventory.SelectedSlot, out BlockId block) &&
                BlockRegistry.IsSolid(block))
            {
                float size = world.VoxelSize;
                var voxelBounds = new Bounds(world.VoxelCenter(voxel), Vector3.one * (size * 0.98f));
                var body = new Bounds(
                    transform.position + _controller.center,
                    new Vector3(_controller.radius * 2f, _controller.height, _controller.radius * 2f));

                if (body.Intersects(voxelBounds))
                    return;
            }

            _inventory.TryPlace(voxel);
        }

        private void SetTarget(bool hasTarget, VoxelHit hit, WorldManager world)
        {
            HasTarget = hasTarget;
            Target = hit;

            if (!hasTarget || world == null)
            {
                if (_outline != null)
                    _outline.SetActive(false);

                return;
            }

            EnsureOutline();

            float size = world.VoxelSize;
            float grow = size * 0.01f;

            _outline.SetActive(true);
            _outline.transform.position = new Vector3(hit.Voxel.x * size - grow, hit.Voxel.y * size - grow, hit.Voxel.z * size - grow);
            _outline.transform.localScale = Vector3.one * (size + grow * 2f);
        }

        private void EnsureOutline()
        {
            if (_outline != null)
                return;

            _outline = new GameObject("Block Outline");

            var filter = _outline.AddComponent<MeshFilter>();
            var renderer = _outline.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("World/HighlightMaterial");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Unit cube edges as line segments.
            var vertices = new Vector3[8];
            for (int i = 0; i < 8; i++)
                vertices[i] = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);

            int[] lines =
            {
                0, 1, 2, 3, 4, 5, 6, 7,
                0, 2, 1, 3, 4, 6, 5, 7,
                0, 4, 1, 5, 2, 6, 3, 7
            };

            var mesh = new Mesh { name = "Block Outline" };
            mesh.vertices = vertices;
            mesh.SetIndices(lines, MeshTopology.Lines, 0);

            filter.sharedMesh = mesh;
        }
    }
}
