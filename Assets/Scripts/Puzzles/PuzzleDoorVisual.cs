using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Replaceable, collider-free procedural block for PuzzleDoor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleDoorVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BoxCollider2D sourceCollider;

        [Header("Appearance")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color color = new Color(0.2f, 0.58f, 0.9f, 1f);

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;

        private void Awake()
        {
            ResolveSourceCollider();
            EnsureRenderer();
            RefreshGeometry();
        }

        private void OnEnable()
        {
            ResolveSourceCollider();
            EnsureRenderer();
            RefreshGeometry();

            if (lineRenderer != null)
                lineRenderer.enabled = true;
        }

        private void OnDisable()
        {
            if (lineRenderer != null)
                lineRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void ResolveSourceCollider()
        {
            if (sourceCollider == null)
                sourceCollider = GetComponentInParent<BoxCollider2D>();
        }

        private void EnsureRenderer()
        {
            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
                lineRenderer = gameObject.AddComponent<LineRenderer>();

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "PuzzleDoorVisual_Runtime",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 0;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = sortingOrder;

            if (runtimeMaterial != null)
                lineRenderer.sharedMaterial = runtimeMaterial;
        }

        private void RefreshGeometry()
        {
            if (lineRenderer == null || sourceCollider == null)
                return;

            Vector3 worldCenter = sourceCollider.transform.TransformPoint(sourceCollider.offset);
            Vector3 center = transform.InverseTransformPoint(worldCenter);
            float width = Mathf.Max(0.02f, sourceCollider.size.x);
            float height = Mathf.Max(0.02f, sourceCollider.size.y);

            if (width >= height)
            {
                lineRenderer.widthMultiplier = height;
                lineRenderer.SetPosition(0, center + Vector3.left * (width * 0.5f));
                lineRenderer.SetPosition(1, center + Vector3.right * (width * 0.5f));
            }
            else
            {
                lineRenderer.widthMultiplier = width;
                lineRenderer.SetPosition(0, center + Vector3.down * (height * 0.5f));
                lineRenderer.SetPosition(1, center + Vector3.up * (height * 0.5f));
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveSourceCollider();
        }
#endif
    }
}
