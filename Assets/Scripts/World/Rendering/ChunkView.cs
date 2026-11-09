using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using World.Chunks;

namespace World.Rendering
{
    /// <summary>
    /// Scene object for one chunk:
    ///   - opaque voxel mesh with a collider,
    ///   - "Water" child: transparent liquid mesh, no collider,
    ///   - "Details" child: grass tufts and flowers, no collider.
    ///
    /// Collision data for the opaque mesh is cooked on a worker thread
    /// (Physics.BakeMesh) and swapped in when ready, so building chunks
    /// does not stall the main thread. The previous collider stays active
    /// until then. Chunks players stand in are cooked immediately.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class ChunkView : MonoBehaviour
    {
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MeshCollider _meshCollider;

        private Mesh _mesh;
        private Mesh _colliderMesh;
        private Mesh _bakingMesh;
        private Task _bakeTask;

        // Meshes that are no longer shown but may still be baking.
        private readonly List<(Mesh Mesh, Task Task)> _retired = new List<(Mesh, Task)>();

        private Child _liquid;
        private Child _detail;

        public ChunkCoord Coord { get; private set; }

        /// <summary>Combined chunk versions the current mesh was built from.</summary>
        public long BuiltFromVersion { get; private set; }

        /// <summary>True once the collider matches the latest mesh.</summary>
        public bool ColliderUpToDate => _bakingMesh == null;

        private sealed class Child
        {
            public GameObject Object;
            public MeshFilter Filter;
            public Mesh Mesh;
        }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();
        }

        public void Initialize(
            ChunkCoord coord,
            Vector3 worldPosition,
            Material solidMaterial,
            Material liquidMaterial,
            bool renderingEnabled)
        {
            Coord = coord;

            transform.localPosition = worldPosition;
            name = $"Chunk {coord.X}, {coord.Z}";

            _meshRenderer.sharedMaterial = solidMaterial;
            _meshRenderer.enabled = renderingEnabled;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.On;

            if (!renderingEnabled)
                return;

            if (liquidMaterial != null)
                _liquid = CreateChild("Water", liquidMaterial, ShadowCastingMode.Off);

            _detail = CreateChild("Details", solidMaterial, ShadowCastingMode.On);
        }

        private Child CreateChild(string childName, Material material, ShadowCastingMode shadows)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;

            return new Child { Object = go, Filter = filter };
        }

        /// <summary>Uploads a finished mesh. Main thread only.</summary>
        public void ApplyMesh(ChunkMeshResult result, bool colliderImmediately)
        {
            BuiltFromVersion = result.SourceVersion;

            Mesh previous = _mesh;
            _mesh = CreateMesh(result.Solid, "Solid");
            _meshFilter.sharedMesh = _mesh;

            if (_mesh.vertexCount == 0 || colliderImmediately)
            {
                // Cook now on the main thread.
                SetCollider(_mesh.vertexCount == 0 ? null : _mesh);
                RetireBaking();
            }
            else
            {
                // Cook in the background; keep the old collider meanwhile.
                RetireBaking();
                _bakingMesh = _mesh;
                int meshId = _mesh.GetInstanceID();
                _bakeTask = Task.Run(() => Physics.BakeMesh(meshId, false));
            }

            Release(previous, null);

            UploadChild(_liquid, result.Liquid, "Water");
            UploadChild(_detail, result.Detail, "Details");
        }

        /// <summary>
        /// Finishes a pending background collider bake right now (waits
        /// for it). Used for chunks a player is standing in or next to.
        /// </summary>
        public void EnsureColliderNow()
        {
            if (_bakingMesh == null)
                return;

            _bakeTask?.Wait();

            Mesh baked = _bakingMesh;
            _bakingMesh = null;
            _bakeTask = null;

            SetCollider(baked);
        }

        private void Update()
        {
            if (_bakingMesh != null && _bakeTask != null && _bakeTask.IsCompleted)
            {
                Mesh baked = _bakingMesh;
                _bakingMesh = null;
                _bakeTask = null;

                // Uses the data cooked on the worker thread.
                SetCollider(baked);
            }

            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                if (_retired[i].Task == null || _retired[i].Task.IsCompleted)
                {
                    Release(_retired[i].Mesh, null);
                    _retired.RemoveAt(i);
                }
            }
        }

        private void SetCollider(Mesh mesh)
        {
            Mesh previous = _colliderMesh;
            _colliderMesh = mesh;

            // Assigning null first forces the collider to refresh.
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = mesh;

            Release(previous, null);
        }

        private void RetireBaking()
        {
            if (_bakingMesh != null)
                _retired.Add((_bakingMesh, _bakeTask));

            _bakingMesh = null;
            _bakeTask = null;
        }

        /// <summary>Destroys a mesh once nothing uses it any more.</summary>
        private void Release(Mesh mesh, Task task)
        {
            if (mesh == null || mesh == _mesh || mesh == _colliderMesh || mesh == _bakingMesh)
                return;

            foreach ((Mesh Mesh, Task Task) retired in _retired)
            {
                if (retired.Mesh == mesh && retired.Task != null && !retired.Task.IsCompleted)
                    return;
            }

            Destroy(mesh);
        }

        private void UploadChild(Child child, ChunkMeshData data, string suffix)
        {
            if (child == null || data == null)
                return;

            if (child.Mesh != null)
                Destroy(child.Mesh);

            child.Mesh = CreateMesh(data, suffix);
            child.Filter.sharedMesh = child.Mesh;
            child.Object.SetActive(child.Mesh.vertexCount > 0);
        }

        private Mesh CreateMesh(ChunkMeshData data, string suffix)
        {
            var mesh = new Mesh
            {
                name = $"Chunk_{Coord.X}_{Coord.Z}_{suffix}"
            };

            if (data == null || data.IsEmpty)
                return mesh;

            mesh.indexFormat = data.Vertices.Count > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetColors(data.Colors);
            mesh.SetTriangles(data.Triangles, 0, true);

            return mesh;
        }

        private void OnDestroy()
        {
            // Meshes still baking are left to the garbage collector of
            // the engine; destroying them mid-bake is not allowed.
            if (_mesh != null && _mesh != _bakingMesh)
                Destroy(_mesh);

            if (_colliderMesh != null && _colliderMesh != _mesh)
                Destroy(_colliderMesh);

            if (_liquid?.Mesh != null)
                Destroy(_liquid.Mesh);

            if (_detail?.Mesh != null)
                Destroy(_detail.Mesh);
        }
    }
}
