using Unity.Collections;
using UnityEngine;

[ExecuteAlways]
public class MaterialRegistry : MonoBehaviour {
    public static MaterialRegistry Instance { get; private set; }

    private NativeArray<VoxelMaterialData> registryData;
    public NativeArray<VoxelMaterialData> RegistryData => registryData;

    private void OnEnable() {
        if (Instance == null) Instance = this;
        else if (Instance != this) return;

        InitializeIfNeeded();
    }

    // === INICIALIZACIÓN SEGURA ===
    public void InitializeIfNeeded() {
        if (registryData.IsCreated) return;

        TerrainMaterial[] materials = Resources.LoadAll<TerrainMaterial>("Terrain/Material");

        registryData = new NativeArray<VoxelMaterialData>(256, Allocator.Persistent);

        for (int i = 0; i < materials.Length; i++) {
            TerrainMaterial material = materials[i];

            VoxelMaterialData data;
            data.id = material.materialID;
            data.physicsFlags = material.packedPhysicsFlags;

            registryData[material.materialID] = data;
        }
    }

    private void OnDisable() {
        if (Instance == this) Instance = null;
        if (registryData.IsCreated) registryData.Dispose();
    }
}
