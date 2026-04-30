using UnityEngine;

public interface ITerrainBrush {
    void Apply(TerrainChunkData chunkData, Vector3 brushWorldPosition, float brushRadius, float brushStrength, KeyCode modifier);
    BrushShape GetBrushShape();
    BrushMode GetBrushMode();
}
