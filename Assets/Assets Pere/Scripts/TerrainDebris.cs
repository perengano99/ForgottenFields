using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(MeshCollider))]
public class TerrainDebris : MonoBehaviour {
    private VolumetricTerrainChunk originChunk;
    private float debrisVolume = 1f;
    private float debrisStrength = 1f;

    public void Initialize(VolumetricTerrainChunk chunk, float volume) {
        originChunk = chunk;
        debrisVolume = Mathf.Max(0.0001f, volume);
    }

    // === FÍSICA DE IMPACTO ===
    private void OnCollisionEnter(Collision collision) {
        if (collision.gameObject.CompareTag("Debris")) return;
        if (originChunk == null) return;

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;



        // === CÁLCULO DE BOUNDS Y OBB ===
        Vector3 extents = meshFilter.sharedMesh.bounds.extents;
        Vector3 worldCenter = transform.TransformPoint(meshFilter.sharedMesh.bounds.center);
        worldCenter += Vector3.down * 0.5f;

        // === DESPACHO CSG ===
        float maxRadius = extents.magnitude + 1.5f;
        originChunk.ModifyTerrain(worldCenter, maxRadius, 1f, BrushType.BoxAdd, extents, transform.rotation);

        Destroy(gameObject);
    }
}