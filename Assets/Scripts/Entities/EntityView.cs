using System.Collections.Generic;
using Items;
using Networking;
using UnityEngine;
using UnityEngine.Rendering;
using World.Rendering;

namespace Gameplay.Entities
{
    /// <summary>
    /// How a creature or dropped item looks on a client: built from voxel
    /// boxes by kind (pig, chicken, zombie) or from the item's own model,
    /// with walking legs, a red flash when hit, and spinning items.
    /// Also the list clients use to aim attacks at creatures.
    /// Does nothing on a dedicated server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityView : MonoBehaviour
    {
        private static readonly List<EntityView> Visible = new List<EntityView>();
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private EntityState _state;
        private NetObject _netObject;
        private Transform _model;
        private readonly List<Transform> _legs = new List<Transform>();
        private readonly List<Transform> _arms = new List<Transform>();
        private EntityKind _builtKind;
        private ushort _builtItem;
        private float _lastHealth = -1f;
        private float _flash;
        private float _phase;
        private Vector3 _lastPosition;
        private MaterialPropertyBlock _block;
        private Renderer[] _renderers;

        public EntityKind Kind => _state != null ? _state.Kind : EntityKind.None;
        public uint NetworkId => _netObject != null ? _netObject.NetworkId : 0;

        public Bounds Bounds
        {
            get
            {
                EntityDefinition def = EntityDefinitions.Get(Kind);
                Vector3 feet = transform.position;
                return new Bounds(feet + Vector3.up * (def.Height * 0.5f), new Vector3(def.Width, def.Height, def.Width));
            }
        }

        /// <summary>The nearest creature (not item) hit by a ray, within reach.</summary>
        public static EntityView Raycast(Ray ray, float reach, out float distance)
        {
            EntityView best = null;
            distance = reach;

            foreach (EntityView view in Visible)
            {
                if (view == null || view.Kind == EntityKind.None || view.Kind == EntityKind.DroppedItem)
                    continue;

                Bounds bounds = view.Bounds;
                bounds.Expand(0.15f); // a little forgiving
                if (bounds.IntersectRay(ray, out float hit) && hit < distance)
                {
                    distance = hit;
                    best = view;
                }
            }

            return best;
        }

        private void Awake()
        {
            _state = GetComponent<EntityState>();
            _netObject = GetComponent<NetObject>();
        }

        private void OnEnable() => Visible.Add(this);
        private void OnDisable() => Visible.Remove(this);

        private static bool Renders
        {
            get
            {
                if (Application.isBatchMode)
                    return false;
                NetworkRuntime runtime = NetworkRuntime.Current;
                return runtime == null || !runtime.IsRunning || runtime.RunsClient;
            }
        }

        private void LateUpdate()
        {
            if (_state == null || !Renders)
                return;

            if (_state.Kind != _builtKind || _state.ItemNetworkId != _builtItem)
                Build();

            if (_model == null)
                return;

            float dt = Time.deltaTime;

            if (_state.Kind == EntityKind.DroppedItem)
            {
                // Spin and bob.
                _model.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
                _model.localPosition = new Vector3(0f, 0.15f + Mathf.Sin(Time.time * 2.5f) * 0.05f, 0f);
                return;
            }

            // Walking animation from how fast it actually moves.
            Vector3 position = transform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;
            float speed = dt > 0f ? new Vector2(moved.x, moved.z).magnitude / dt : 0f;
            if (speed > 20f) speed = 0f; // teleport / spawn

            _phase += speed * dt * 4.5f;
            float swing = Mathf.Sin(_phase) * Mathf.Clamp01(speed / 2f) * 35f;

            for (int i = 0; i < _legs.Count; i++)
                _legs[i].localRotation = Quaternion.Euler(i % 2 == 0 ? swing : -swing, 0f, 0f);

            // Zombies hold their arms forward.
            for (int i = 0; i < _arms.Count; i++)
                _arms[i].localRotation = Quaternion.Euler(-80f + (i % 2 == 0 ? swing : -swing) * 0.2f, 0f, 0f);

            // Red flash when hurt.
            if (_lastHealth >= 0f && _state.Health < _lastHealth)
                _flash = 0.25f;
            _lastHealth = _state.Health;

            _flash = Mathf.Max(0f, _flash - dt);
            ApplyTint(_flash > 0f ? new Color(1f, 0.45f, 0.45f, 1f) : Color.white);
        }

        private void ApplyTint(Color tint)
        {
            if (_renderers == null)
                return;

            _block ??= new MaterialPropertyBlock();
            _block.SetColor(TintId, tint);
            foreach (Renderer renderer in _renderers)
                if (renderer != null)
                    renderer.SetPropertyBlock(_block);
        }

        // ------------------------------------------------------------
        // Models
        // ------------------------------------------------------------

        private void Build()
        {
            if (_model != null)
                Destroy(_model.gameObject);

            _legs.Clear();
            _arms.Clear();
            _builtKind = _state.Kind;
            _builtItem = _state.ItemNetworkId;
            _lastPosition = transform.position;

            var root = new GameObject("Model").transform;
            root.SetParent(transform, false);
            _model = root;

            Material material = Resources.Load<Material>("World/ChunkMaterial");

            switch (_state.Kind)
            {
                case EntityKind.DroppedItem:
                    ItemData item = ItemDatabase.GetByNetworkId(_state.ItemNetworkId);
                    GameObject model = HeldItemModel.Create(item, root, material, castShadows: true);
                    if (model != null)
                        model.transform.localScale = Vector3.one * 0.9f;
                    break;

                case EntityKind.Pig: BuildPig(root, material); break;
                case EntityKind.Chicken: BuildChicken(root, material); break;
                case EntityKind.Zombie: BuildZombie(root, material); break;
            }

            _renderers = root.GetComponentsInChildren<Renderer>();
        }

        private void BuildPig(Transform root, Material material)
        {
            Color32 pink = new Color32(236, 160, 160, 255), dark = new Color32(200, 120, 125, 255), snout = new Color32(250, 185, 185, 255);
            Part(root, "Body", new Vector3(0f, 0.3f, 0f), Box(new Vector3(-0.3f, 0f, -0.45f), new Vector3(0.6f, 0.45f, 0.9f), pink));
            Transform head = Part(root, "Head", new Vector3(0f, 0.45f, 0.45f), Box(new Vector3(-0.22f, -0.12f, 0f), new Vector3(0.44f, 0.4f, 0.36f), pink));
            Part(head, "Snout", new Vector3(0f, 0f, 0.36f), Box(new Vector3(-0.1f, -0.06f, 0f), new Vector3(0.2f, 0.14f, 0.06f), snout));
            Part(head, "Eyes", new Vector3(0f, 0.12f, 0.361f), Box(new Vector3(-0.15f, 0f, 0f), new Vector3(0.3f, 0.05f, 0.01f), new Color32(30, 25, 25, 255)));

            foreach (Vector3 at in new[] { new Vector3(-0.18f, 0.3f, 0.3f), new Vector3(0.18f, 0.3f, 0.3f), new Vector3(-0.18f, 0.3f, -0.3f), new Vector3(0.18f, 0.3f, -0.3f) })
                _legs.Add(Part(root, "Leg", at, Box(new Vector3(-0.08f, -0.3f, -0.08f), new Vector3(0.16f, 0.3f, 0.16f), dark)));
        }

        private void BuildChicken(Transform root, Material material)
        {
            Color32 white = new Color32(245, 245, 240, 255), yellow = new Color32(240, 190, 60, 255), red = new Color32(210, 50, 45, 255);
            Part(root, "Body", new Vector3(0f, 0.25f, 0f), Box(new Vector3(-0.18f, 0f, -0.22f), new Vector3(0.36f, 0.3f, 0.44f), white));
            Transform head = Part(root, "Head", new Vector3(0f, 0.5f, 0.18f), Box(new Vector3(-0.1f, 0f, -0.08f), new Vector3(0.2f, 0.22f, 0.16f), white));
            Part(head, "Beak", new Vector3(0f, 0.08f, 0.08f), Box(new Vector3(-0.05f, 0f, 0f), new Vector3(0.1f, 0.06f, 0.08f), yellow));
            Part(head, "Comb", new Vector3(0f, 0.22f, 0f), Box(new Vector3(-0.03f, 0f, -0.05f), new Vector3(0.06f, 0.06f, 0.12f), red));

            foreach (Vector3 at in new[] { new Vector3(-0.08f, 0.25f, 0f), new Vector3(0.08f, 0.25f, 0f) })
                _legs.Add(Part(root, "Leg", at, Box(new Vector3(-0.025f, -0.25f, -0.025f), new Vector3(0.05f, 0.25f, 0.05f), yellow)));
        }

        private void BuildZombie(Transform root, Material material)
        {
            var palette = new AvatarModel.Palette
            {
                Skin = new Color32(98, 150, 86, 255),
                Hair = new Color32(52, 70, 40, 255),
                Shirt = new Color32(54, 98, 140, 255),
                ShirtDark = new Color32(40, 72, 104, 255),
                Trousers = new Color32(60, 50, 90, 255),
                Boots = new Color32(45, 40, 35, 255),
                Belt = new Color32(50, 40, 30, 255),
                Buckle = new Color32(120, 120, 110, 255),
                Eyes = new Color32(20, 20, 20, 255)
            };

            float v = AvatarModel.Voxel;
            Transform hips = new GameObject("Hips").transform;
            hips.SetParent(root, false);
            hips.localPosition = new Vector3(0f, AvatarModel.HipHeight, 0f);

            Transform leftLeg = Part(hips, "Left Leg", new Vector3(-1.5f * v, 0, 0), AvatarModel.Thigh(palette));
            Transform rightLeg = Part(hips, "Right Leg", new Vector3(1.5f * v, 0, 0), AvatarModel.Thigh(palette));
            Part(leftLeg, "Left Shin", new Vector3(0, -AvatarModel.ThighLength * v, 0), AvatarModel.Shin(palette));
            Part(rightLeg, "Right Shin", new Vector3(0, -AvatarModel.ThighLength * v, 0), AvatarModel.Shin(palette));
            _legs.Add(leftLeg);
            _legs.Add(rightLeg);

            Transform torso = Part(hips, "Torso", Vector3.zero, AvatarModel.Torso(palette));
            float shoulder = (AvatarModel.TorsoHeight - 0.5f) * v;
            _arms.Add(Part(torso, "Left Arm", new Vector3(-4f * v, shoulder, 0), AvatarModel.Arm(palette)));
            _arms.Add(Part(torso, "Right Arm", new Vector3(4f * v, shoulder, 0), AvatarModel.Arm(palette)));
            Part(torso, "Head", new Vector3(0, AvatarModel.TorsoHeight * v, 0), AvatarModel.Head(palette));
        }

        private Transform Part(Transform parent, string name, Vector3 position, ChunkMeshData data)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var mesh = new Mesh { name = "Entity " + name };
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0, true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("World/ChunkMaterial");
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        // Box with outward faces (same winding as the chunk mesher).
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        private static readonly int[][] FaceCorners =
        {
            new[] { 0,1,0, 0,1,1, 1,1,1, 1,1,0 },
            new[] { 0,0,1, 0,0,0, 1,0,0, 1,0,1 },
            new[] { 0,0,0, 0,0,1, 0,1,1, 0,1,0 },
            new[] { 1,0,1, 1,0,0, 1,1,0, 1,1,1 },
            new[] { 0,0,1, 1,0,1, 1,1,1, 0,1,1 },
            new[] { 1,0,0, 0,0,0, 0,1,0, 1,1,0 },
        };

        public static ChunkMeshData Box(Vector3 min, Vector3 size, Color32 color)
        {
            var mesh = new ChunkMeshData();

            for (int f = 0; f < 6; f++)
            {
                int start = mesh.Vertices.Count;
                int[] c = FaceCorners[f];

                // Slightly darker sides and bottom for shape.
                float shade = f == 0 ? 1f : f == 1 ? 0.6f : 0.82f;
                var shaded = new Color32((byte)(color.r * shade), (byte)(color.g * shade), (byte)(color.b * shade), 255);

                for (int i = 0; i < 4; i++)
                {
                    mesh.Vertices.Add(new Vector3(min.x + c[i * 3] * size.x, min.y + c[i * 3 + 1] * size.y, min.z + c[i * 3 + 2] * size.z));
                    mesh.Normals.Add(FaceNormals[f]);
                    mesh.Colors.Add(shaded);
                }

                mesh.Triangles.Add(start); mesh.Triangles.Add(start + 1); mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start); mesh.Triangles.Add(start + 2); mesh.Triangles.Add(start + 3);
            }

            return mesh;
        }
    }
}
