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

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null || mc.sharedMesh == null) return;

        originChunk.ReintegrateDebris(mc, debrisVolume * debrisStrength);
        Destroy(gameObject);
    }
}