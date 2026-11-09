using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace World.Rendering
{
    /// <summary>
    /// Blocky voxel clouds high above the terrain.
    ///
    /// The cloud field is a 2D noise pattern (from the world seed) cut into
    /// big cells; each cloudy cell is one box. Only the outside faces of
    /// connected cells are built, so a typical field is ~5-10k triangles
    /// in a single draw call. It drifts with the wind using the shared
    /// <see cref="WorldClock"/>, so all players see the same clouds.
    ///
    /// The mesh around the camera is rebuilt only when the camera (or the
    /// drift) crosses a cell - a fraction of a millisecond, every few
    /// seconds at most. No shadows, no physics. Can be switched off in the
    /// graphics settings. Added by the WorldManager on machines that render.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudLayer : MonoBehaviour
    {
        [SerializeField]
        [Min(20f)]
        private float height = 190f;

        [Tooltip("Size of one cloud cell (metres).")]
        [SerializeField]
        [Min(2f)]
        private float cellSize = 12f;

        [SerializeField]
        [Min(1f)]
        private float thickness = 4f;

        [Tooltip("Cells from the camera to the edge of the cloud field.")]
        [SerializeField]
        [Range(8, 48)]
        private int radiusCells = 26;

        [Tooltip("0 = clear sky, 1 = overcast.")]
        [SerializeField]
        [Range(0f, 1f)]
        private float coverage = 0.42f;

        [Tooltip("Drift speed in metres per second at normal wind.")]
        [SerializeField]
        [Min(0f)]
        private float driftSpeed = 2.5f;

        private static readonly int FadeDistanceId = Shader.PropertyToID("_FadeDistance");

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();

        private WorldManager _world;
        private Mesh _mesh;
        private Material _material;
        private GameObject _object;
        private Vector2Int _builtCell = new Vector2Int(int.MinValue, int.MinValue);
        private int _builtSeed;
        private bool[] _cells;

        /// <summary>Clouds on/off (graphics setting).</summary>
        public static bool Enabled { get; set; } = true;

        public int TriangleCount => _triangles.Count / 3;

        private void Awake()
        {
            _world = GetComponent<WorldManager>();
        }

        private void OnDestroy()
        {
            if (_object != null)
                Destroy(_object);

            if (_mesh != null)
                Destroy(_mesh);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            bool show = Enabled && camera != null && !UnderwaterEffect.CameraUnderwater;

            if (!show)
            {
                if (_object != null && _object.activeSelf)
                    _object.SetActive(false);
                return;
            }

            if (_object == null && !Create())
                return;

            if (!_object.activeSelf)
                _object.SetActive(true);

            // Drift: the whole field moves with the (shared) wind over time.
            Vector2 drift = DriftOffset();

            Vector3 cameraPosition = camera.transform.position;
            var cell = new Vector2Int(
                Mathf.FloorToInt((cameraPosition.x - drift.x) / cellSize),
                Mathf.FloorToInt((cameraPosition.z - drift.y) / cellSize));

            int seed = _world != null ? _world.Seed : 0;

            if (cell != _builtCell || seed != _builtSeed)
                Rebuild(cell, seed);

            // Mesh is built relative to its centre cell.
            _object.transform.position = new Vector3(
                cell.x * cellSize + drift.x,
                height,
                cell.y * cellSize + drift.y);
        }

        private Vector2 DriftOffset()
        {
            // Fixed prevailing direction so the offset is a pure function
            // of world time (identical for everyone, no accumulation).
            double t = WorldClock.WorldSeconds;
            return new Vector2((float)(t * driftSpeed * 0.8 % 100000.0), (float)(t * driftSpeed * 0.6 % 100000.0));
        }

        private bool Create()
        {
            _material = Resources.Load<Material>("World/CloudMaterial");
            if (_material == null)
                return false;

            _object = new GameObject("Clouds");
            _object.transform.SetParent(transform, false);

            _mesh = new Mesh { name = "Clouds" };
            _mesh.MarkDynamic();

            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _object.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _material.SetFloat(FadeDistanceId, radiusCells * cellSize);
            return true;
        }

        private void Rebuild(Vector2Int center, int seed)
        {
            _builtCell = center;
            _builtSeed = seed;

            int size = radiusCells * 2 + 1;
            if (_cells == null || _cells.Length != size * size)
                _cells = new bool[size * size];

            float seedX = (seed & 0xFFFF) * 0.137f;
            float seedZ = ((seed >> 16) & 0xFFFF) * 0.173f;

            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int dx = x - radiusCells, dz = z - radiusCells;
                bool inside = dx * dx + dz * dz <= radiusCells * radiusCells;

                float wx = center.x + dx + seedX;
                float wz = center.y + dz + seedZ;

                // Big shapes plus some breakup.
                float n = Mathf.PerlinNoise(wx * 0.08f, wz * 0.08f) * 0.7f +
                          Mathf.PerlinNoise(wx * 0.23f + 31.7f, wz * 0.23f - 12.3f) * 0.3f;

                _cells[x + z * size] = inside && n > 1f - coverage;
            }

            _vertices.Clear();
            _normals.Clear();
            _triangles.Clear();

            float half = thickness * 0.5f;

            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                if (!_cells[x + z * size])
                    continue;

                float x0 = (x - radiusCells) * cellSize, x1 = x0 + cellSize;
                float z0 = (z - radiusCells) * cellSize, z1 = z0 + cellSize;

                // Top and bottom are always visible.
                Quad(new Vector3(x0, half, z0), new Vector3(x0, half, z1), new Vector3(x1, half, z1), new Vector3(x1, half, z0), Vector3.up);
                Quad(new Vector3(x0, -half, z1), new Vector3(x0, -half, z0), new Vector3(x1, -half, z0), new Vector3(x1, -half, z1), Vector3.down);

                if (!Cell(x - 1, z, size))
                    Quad(new Vector3(x0, -half, z0), new Vector3(x0, -half, z1), new Vector3(x0, half, z1), new Vector3(x0, half, z0), Vector3.left);
                if (!Cell(x + 1, z, size))
                    Quad(new Vector3(x1, -half, z1), new Vector3(x1, -half, z0), new Vector3(x1, half, z0), new Vector3(x1, half, z1), Vector3.right);
                if (!Cell(x, z + 1, size))
                    Quad(new Vector3(x0, -half, z1), new Vector3(x1, -half, z1), new Vector3(x1, half, z1), new Vector3(x0, half, z1), Vector3.forward);
                if (!Cell(x, z - 1, size))
                    Quad(new Vector3(x1, -half, z0), new Vector3(x0, -half, z0), new Vector3(x0, half, z0), new Vector3(x1, half, z0), Vector3.back);
            }

            _mesh.Clear();
            _mesh.indexFormat = _vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetTriangles(_triangles, 0, true);
        }

        private bool Cell(int x, int z, int size)
        {
            return x >= 0 && z >= 0 && x < size && z < size && _cells[x + z * size];
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);

            for (int i = 0; i < 4; i++)
                _normals.Add(normal);

            _triangles.Add(start);
            _triangles.Add(start + 1);
            _triangles.Add(start + 2);
            _triangles.Add(start);
            _triangles.Add(start + 2);
            _triangles.Add(start + 3);
        }
    }
}
