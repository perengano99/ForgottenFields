using Unity.Collections;
using UnityEngine;

public class MaterialRegistry : MonoBehaviour {
    public static MaterialRegistry Instance { get; private set; }

    private NativeArray<VoxelMaterialData> _registryData;
    public NativeArray<VoxelMaterialData> RegistryData => _registryData;

    private void Awake() {
        Debug.Log("Initializing MaterialRegistry...");
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            Debug.LogWarning("Multiple instances of MaterialRegistry detected. Destroying duplicate.");
            return;
        }

        Instance = this;

        TerrainMaterial[] materials = Resources.LoadAll<TerrainMaterial>("Terrain/Material");

        if (_registryData.IsCreated) _registryData.Dispose();
        _registryData = new NativeArray<VoxelMaterialData>(256, Allocator.Persistent);

        for (int i = 0; i < materials.Length; i++) {
            TerrainMaterial material = materials[i];

            VoxelMaterialData data;
            data.id = material.materialID;
            byte flags = 0;
            if (material.isSolid) flags |= 1;
            if (material.canFloat) flags |= 2;
            if (material.isModifiable) flags |= 4;
            data.physicsFlags = flags;

            _registryData[material.materialID] = data;
        }
    }

    private void OnDestroy() {
        if (Instance == this)
            Instance = null;

        if (_registryData.IsCreated)
            _registryData.Dispose();
    }
}
