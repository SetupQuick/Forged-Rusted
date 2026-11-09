using UnityEngine;
using UnityEngine.Rendering;
using World.Chunks;

namespace World.Rendering
{
    /// <summary>
    /// Scene object for one chunk: an opaque mesh with a collider, plus
    /// a child object for the transparent water mesh (no collider, so
    /// players can walk into water).
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

        private GameObject _liquidObject;
        private MeshFilter _liquidFilter;
        private MeshRenderer _liquidRenderer;
        private Mesh _liquidMesh;

        public ChunkCoord Coord { get; private set; }

        /// <summary>Combined chunk versions the current mesh was built from.</summary>
        public long BuiltFromVersion { get; private set; }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();
        }

        public void Initialize(
            ChunkCoord coord,
            int sizeX,
            int sizeZ,
            Material solidMaterial,
            Material liquidMaterial,
            bool renderingEnabled)
        {
            Coord = coord;

            transform.localPosition = new Vector3(coord.X * sizeX, 0f, coord.Z * sizeZ);
            name = $"Chunk {coord.X}, {coord.Z}";

            _meshRenderer.sharedMaterial = solidMaterial;
            _meshRenderer.enabled = renderingEnabled;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.On;

            if (renderingEnabled && liquidMaterial != null)
            {
                _liquidObject = new GameObject("Water");
                _liquidObject.transform.SetParent(transform, false);

                _liquidFilter = _liquidObject.AddComponent<MeshFilter>();
                _liquidRenderer = _liquidObject.AddComponent<MeshRenderer>();
                _liquidRenderer.sharedMaterial = liquidMaterial;
                _liquidRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Uploads a finished mesh. Main thread only.</summary>
        public void ApplyMesh(ChunkMeshResult result)
        {
            BuiltFromVersion = result.SourceVersion;

            _mesh = Upload(_mesh, result.Solid, "Solid");
            _meshFilter.sharedMesh = _mesh;

            // Assigning null first forces the collider to re-cook.
            _meshCollider.sharedMesh = null;

            if (_mesh.vertexCount > 0)
                _meshCollider.sharedMesh = _mesh;

            if (_liquidFilter != null && result.Liquid != null)
            {
                _liquidMesh = Upload(_liquidMesh, result.Liquid, "Water");
                _liquidFilter.sharedMesh = _liquidMesh;
                _liquidObject.SetActive(_liquidMesh.vertexCount > 0);
            }
        }

        private Mesh Upload(Mesh mesh, ChunkMeshData data, string suffix)
        {
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = $"Chunk_{Coord.X}_{Coord.Z}_{suffix}"
                };

                mesh.MarkDynamic();
            }
            else
            {
                mesh.Clear();
            }

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
            if (_mesh != null)
                Destroy(_mesh);

            if (_liquidMesh != null)
                Destroy(_liquidMesh);
        }
    }
}
