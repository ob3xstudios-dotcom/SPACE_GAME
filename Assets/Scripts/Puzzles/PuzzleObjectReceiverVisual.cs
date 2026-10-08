using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Replaceable collider-free frame visual for PuzzleObjectReceiver.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleObjectReceiverVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PuzzleObjectReceiver receiver;
        [SerializeField] private BoxCollider2D sourceCollider;

        [Header("Appearance")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color inactiveColor = new Color(0.95f, 0.55f, 0.12f, 0.75f);
        [SerializeField] private Color activeColor = new Color(0.2f, 0.95f, 0.35f, 0.8f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.06f;
        [SerializeField, Min(0f)] private float inset = 0.04f;

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;

        private void Awake()
        {
            ResolveReferences();
            EnsureRenderer();
            RefreshGeometry();
            SetVisualState(receiver != null && receiver.IsActive);
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            EnsureRenderer();
            RefreshGeometry();
            SetVisualState(receiver != null && receiver.IsActive);

            if (lineRenderer != null)
                lineRenderer.enabled = true;
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (lineRenderer != null)
                lineRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void ResolveReferences()
        {
            if (receiver == null)
                receiver = GetComponentInParent<PuzzleObjectReceiver>();

            if (sourceCollider == null && receiver != null)
                sourceCollider = receiver.GetComponent<BoxCollider2D>();
        }

        private void Subscribe()
        {
            if (receiver == null)
                return;

            receiver.StateChanged -= HandleStateChanged;
            receiver.StateChanged += HandleStateChanged;
        }

        private void Unsubscribe()
        {
            if (receiver != null)
                receiver.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(PuzzleSignalSource source, bool active)
        {
            SetVisualState(active);
        }

        private void SetVisualState(bool active)
        {
            if (lineRenderer == null)
                return;

            Color color = active ? activeColor : inactiveColor;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
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
                        name = "PuzzleObjectReceiverVisual_Runtime",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = 4;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.widthMultiplier = lineWidth;
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
            float halfWidth = Mathf.Max(0.01f, sourceCollider.size.x * 0.5f - inset);
            float halfHeight = Mathf.Max(0.01f, sourceCollider.size.y * 0.5f - inset);

            lineRenderer.SetPosition(0, center + new Vector3(-halfWidth, -halfHeight, 0f));
            lineRenderer.SetPosition(1, center + new Vector3(-halfWidth, halfHeight, 0f));
            lineRenderer.SetPosition(2, center + new Vector3(halfWidth, halfHeight, 0f));
            lineRenderer.SetPosition(3, center + new Vector3(halfWidth, -halfHeight, 0f));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lineWidth = Mathf.Max(0.01f, lineWidth);
            inset = Mathf.Max(0f, inset);
            ResolveReferences();
        }
#endif
    }
}
