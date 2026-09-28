using UnityEngine;
using World.Chunks;

namespace World.Rendering
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class ChunkView : MonoBehaviour
    {
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;
        private Mesh _mesh;

        public ChunkCoord Coord { get; private set; }

        private void Awake()
        {
            _meshFilter =
                GetComponent<MeshFilter>();

            _meshCollider =
                GetComponent<MeshCollider>();
        }

        public void Initialize(
            ChunkData chunk,
            WorldManager world,
            Material material)
        {
            Coord = chunk.Coord;

            transform.localPosition =
                new Vector3(
                    chunk.Coord.X *
                    chunk.SizeX,
                    0f,
                    chunk.Coord.Z *
                    chunk.SizeZ);

            name =
                $"Chunk {chunk.Coord.X}, {chunk.Coord.Z}";

            _mesh =
                ChunkMeshBuilder.BuildMesh(
                    chunk,
                    world);

            _meshFilter.sharedMesh =
                _mesh;

            MeshRenderer renderer =
                GetComponent<MeshRenderer>();

            renderer.sharedMaterial =
                material;

            _meshCollider.sharedMesh =
                _mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}