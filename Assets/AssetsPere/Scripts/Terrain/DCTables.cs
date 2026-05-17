using Unity.Mathematics;

namespace FF.Terrain {
    public static class DCTables {
        public static readonly int3[] VoxelVertices =
        {
            new int3(0, 0, 0),
            new int3(1, 0, 0),
            new int3(1, 1, 0),
            new int3(0, 1, 0),
            new int3(0, 0, 1),
            new int3(1, 0, 1),
            new int3(1, 1, 1),
            new int3(0, 1, 1)
        };

        public static readonly int2[] EdgeVertices =
        {
            new int2(0, 1),
            new int2(3, 2),
            new int2(4, 5),
            new int2(7, 6),

            new int2(0, 3),
            new int2(1, 2),
            new int2(4, 7),
            new int2(5, 6),

            new int2(0, 4),
            new int2(1, 5),
            new int2(2, 6),
            new int2(3, 7)
        };

        public static readonly int3[] QuadAdjacencyOffsets =
        {
            new int3(0, 0, 0),
            new int3(0, 0, -1),
            new int3(0, -1, -1),
            new int3(0, -1, 0),

            new int3(0, 0, 0),
            new int3(-1, 0, 0),
            new int3(-1, 0, -1),
            new int3(0, 0, -1),

            new int3(0, 0, 0),
            new int3(0, -1, 0),
            new int3(-1, -1, 0),
            new int3(-1, 0, 0)
        };

        public static readonly int[] BaseTriangulation =
        {
            0, 1, 2,
            0, 2, 3
        };

        public static readonly int[] FlippedTriangulation =
        {
            0, 2, 1,
            0, 3, 2
        };
    }
}
