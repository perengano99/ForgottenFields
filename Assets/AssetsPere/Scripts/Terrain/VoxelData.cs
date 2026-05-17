using Unity.Mathematics;
using UnityEngine;
using System.Runtime.InteropServices;

namespace FF.Terrain {
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData {
        public float density;   // SDF: < 0 aire, > 0 solido
        public uint material;   // Aligned with HLSL uint
        public uint flags;      
        public uint properties; 
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EdgeIntersection {
        public Vector3 position; // Posición del corte (0.0 a 1.0 local)
        public Vector3 normal;   // Normal en el punto de intersección
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex {
        public Vector3 position;
        public Vector3 normal;
        public uint material;
    }
}