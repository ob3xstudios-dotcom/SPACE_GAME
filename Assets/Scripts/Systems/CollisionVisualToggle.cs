using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Systems
{
    [ExecuteAlways]
    public sealed class CollisionVisualToggle : MonoBehaviour
    {
        [SerializeField] private bool showCollisionVisuals;
        [SerializeField] private TilemapRenderer groundRenderer;
        [SerializeField] private TilemapRenderer wallRenderer;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Apply()
        {
            if (groundRenderer != null)
                groundRenderer.enabled = showCollisionVisuals;

            if (wallRenderer != null)
                wallRenderer.enabled = showCollisionVisuals;
        }
    }
}
