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

        public VoxelHit Target { get; private set; }

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _controller = GetComponent<CharacterController>();
        }

        private void OnDestroy()
        {
            if (_outline != null)
                Destroy(_outline);
        }

        private void Update()
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady || _inventory == null || PlayerInputReader.GameplayInputBlocked)
            {
                SetTarget(false, default, world);
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

            _breakCooldown -= Time.deltaTime;
            _placeCooldown -= Time.deltaTime;

            Mouse mouse = Mouse.current;

            if (mouse == null || !HasTarget)
                return;

            if (mouse.leftButton.wasPressedThisFrame)
                _breakCooldown = 0f;

            if (mouse.rightButton.wasPressedThisFrame)
                _placeCooldown = 0f;

            if (mouse.leftButton.isPressed && _breakCooldown <= 0f)
            {
                _breakCooldown = BlockEditRules.RepeatInterval;
                _inventory.TryBreak(Target.Voxel);
            }
            else if (mouse.rightButton.isPressed && _placeCooldown <= 0f)
            {
                _placeCooldown = BlockEditRules.RepeatInterval;
                TryPlace(world);
            }
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
