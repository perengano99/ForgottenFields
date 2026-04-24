using UnityEngine;

// === CONSTANTS ===
public static class TerrainTopologyData
{
    public const int HEIGHT_STEPS = 12;
}

// === TOPOLOGY ENUM ===
public enum BlockTopology : byte
{
    Air = 0,
    Solid = 1,
    SlopeN = 2,
    SlopeS = 3,
    SlopeE = 4,
    SlopeW = 5
}

// === CELL DATA ===
public struct CellData
{
    public byte topologyID;
    public byte heightLevel;
}

// === TOPOLOGY LOOKUP TABLE ===
public static class TopologyLUT
{
    public static readonly Vector3[] AirVertices =
    {
    };

    public static readonly Vector3[] SolidVertices =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 1f, 1f),
        new Vector3(0f, 1f, 1f)
    };

    public static readonly Vector3[] SlopeNVertices =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 1f, 1f),
        new Vector3(1f, 1f, 1f)
    };

    public static readonly Vector3[] SlopeSVertices =
    {
        new Vector3(0f, 0f, 1f),
        new Vector3(1f, 0f, 1f),
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f)
    };

    public static readonly Vector3[] SlopeEVertices =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(0f, 0f, 1f),
        new Vector3(1f, 0f, 1f),
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 1f, 1f)
    };

    public static readonly Vector3[] SlopeWVertices =
    {
        new Vector3(1f, 0f, 0f),
        new Vector3(1f, 0f, 1f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 1f, 1f)
    };

    public static readonly Vector3[][] VerticesByTopology =
    {
        AirVertices,
        SolidVertices,
        SlopeNVertices,
        SlopeSVertices,
        SlopeEVertices,
        SlopeWVertices
    };
}
