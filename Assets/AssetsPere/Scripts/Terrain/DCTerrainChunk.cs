using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FF.Terrain {
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class DCTerrainChunk : MonoBehaviour {
        public const int logicalSize = 32;
        public const int padding = 1;
        public const int physicalSize = logicalSize + (padding * 2);

        public NativeArray<VoxelData> voxels;
        public int3 chunkCoordinate;

        Mesh mesh;
        MeshCollider meshCollider;

        public void Initialize(Material material, int3 coordinate) {
            chunkCoordinate = coordinate;

            mesh = new Mesh();
            mesh.MarkDynamic();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = material;
            meshCollider = GetComponent<MeshCollider>();

            int totalVoxels = physicalSize * physicalSize * physicalSize;
            voxels = new NativeArray<VoxelData>(totalVoxels, Allocator.Persistent);
        }

        public void UpdateMesh() {
            int logicalTotal = logicalSize * logicalSize * logicalSize;
            int vertexGridSize = logicalSize + 1;
            int vertexTotal = vertexGridSize * vertexGridSize * vertexGridSize;

            NativeArray<Vertex> denseVertices = new NativeArray<Vertex>(vertexTotal, Allocator.TempJob);
            NativeList<Vertex> compactedVertices = new NativeList<Vertex>(vertexTotal / 4, Allocator.TempJob);
            NativeArray<int> vertexMap = new NativeArray<int>(vertexTotal, Allocator.TempJob);
            NativeStream indexStream = new NativeStream(logicalTotal, Allocator.TempJob);
            NativeList<int> indices = new NativeList<int>(logicalTotal * 3, Allocator.TempJob);

            GenerateVerticesJob generateVerticesJob = new GenerateVerticesJob {
                voxels = voxels,
                vertexGridSize = new int3(vertexGridSize, vertexGridSize, vertexGridSize),
                generatedVertices = denseVertices
            };
            var generateHandle = generateVerticesJob.Schedule(vertexTotal, 64);

            CompactVerticesJob compactVerticesJob = new CompactVerticesJob {
                denseVertices = denseVertices,
                compactedVertices = compactedVertices,
                vertexMap = vertexMap
            };
            var compactHandle = compactVerticesJob.Schedule(generateHandle);

            GenerateQuadsJob generateQuadsJob = new GenerateQuadsJob {
                voxels = voxels,
                vertexMap = vertexMap,
                vertexGridSize = new int3(vertexGridSize, vertexGridSize, vertexGridSize),
                logicalChunkSize = new int3(logicalSize, logicalSize, logicalSize),
                indexStream = indexStream.AsWriter()
            };
            var quadsHandle = generateQuadsJob.Schedule(logicalTotal, 64, compactHandle);
            quadsHandle.Complete();

            NativeStream.Reader streamReader = indexStream.AsReader();
            for (int i = 0; i < logicalTotal; i++) {
                int count = streamReader.BeginForEachIndex(i);
                for (int j = 0; j < count; j++)
                    indices.Add(streamReader.Read<int>());
                streamReader.EndForEachIndex();
            }

            VertexAttributeDescriptor[] attributes = new VertexAttributeDescriptor[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UInt32, 1)
            };

            mesh.SetVertexBufferParams(compactedVertices.Length, attributes);
            mesh.SetVertexBufferData(compactedVertices.AsArray(), 0, 0, compactedVertices.Length, 0, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices.AsArray(), 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length), MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            mesh.RecalculateBounds();

            Physics.BakeMesh(mesh.GetEntityId(), false);
            meshCollider.sharedMesh = mesh;

            denseVertices.Dispose();
            compactedVertices.Dispose();
            vertexMap.Dispose();
            indexStream.Dispose();
            indices.Dispose();
        }

        void OnDestroy() {
            if (voxels.IsCreated) voxels.Dispose();
        }
    }
}
