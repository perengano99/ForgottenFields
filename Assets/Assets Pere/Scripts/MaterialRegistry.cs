using Unity.Collections;
using UnityEngine;

[ExecuteAlways]
public class MaterialRegistry : MonoBehaviour {
    public static MaterialRegistry Instance { get; private set; }

    public int textureResolution = 32;

    private NativeArray<VoxelMaterialData> registryData;
    public NativeArray<VoxelMaterialData> RegistryData => registryData;

    private Texture2DArray textureArray;
    public Texture2DArray SplatTextureArray => textureArray;

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

        textureArray = new Texture2DArray(textureResolution, textureResolution, 256, TextureFormat.RGBA32, false);
        textureArray.filterMode = FilterMode.Point;
        textureArray.wrapMode = TextureWrapMode.Repeat;

        for (int i = 0; i < materials.Length; i++) {
            TerrainMaterial material = materials[i];

            VoxelMaterialData data;
            data.id = material.materialID;
            data.physicsFlags = material.packedPhysicsFlags;

            registryData[material.materialID] = data;

            if (material.diffuseTexture != null) {
                bool validResolution = material.diffuseTexture.width == textureResolution && material.diffuseTexture.height == textureResolution;
                bool validFormat = material.diffuseTexture.format == TextureFormat.RGBA32;
                bool validMipMaps = material.diffuseTexture.mipmapCount == 1;

                if (validResolution && validFormat && validMipMaps)
                    Graphics.CopyTexture(material.diffuseTexture, 0, 0, textureArray, material.materialID, 0);
                else Debug.LogError($"La textura '{material.diffuseTexture.name}' del material '{material.name}' debe ser RGBA32, de tamaño {textureResolution}, y sin MipMaps.", material);
            }
        }

        Shader.SetGlobalTexture("_TerrainSplatArray", textureArray);
    }

    private void OnDisable() {
        if (Instance == this) Instance = null;
        if (registryData.IsCreated) registryData.Dispose();
        if (textureArray != null) DestroyImmediate(textureArray);
    }
}
