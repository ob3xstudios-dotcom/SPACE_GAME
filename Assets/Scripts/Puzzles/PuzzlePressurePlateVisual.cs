using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Replaceable, collider-free procedural placeholder for a pressure plate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzlePressurePlateVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PuzzlePressurePlate plate;
        [SerializeField] private BoxCollider2D sourceCollider;

        [Header("Appearance")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color inactiveColor = new Color(0.95f, 0.58f, 0.12f, 1f);
        [SerializeField] private Color activeColor = new Color(0.2f, 0.95f, 0.35f, 1f);
        [SerializeField, Min(0f)] private float pressedOffset = 0.08f;
        [SerializeField, Min(0f)] private float moveSpeed = 0.6f;

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;
        private Vector3 restLocalPosition;
        private bool active;

        private void Awake()
        {
            restLocalPosition = transform.localPosition;
            ResolveReferences();
            EnsureRenderer();
            RefreshGeometry();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureRenderer();
            RefreshGeometry();

            if (plate != null)
            {
                plate.StateChanged += HandlePlateStateChanged;
                SetVisualState(plate.IsActive);
            }

            if (lineRenderer != null)
                lineRenderer.enabled = true;
        }

        private void OnDisable()
        {
            if (plate != null)
                plate.StateChanged -= HandlePlateStateChanged;

            if (lineRenderer != null)
                lineRenderer.enabled = false;

            transform.localPosition = restLocalPosition;
        }

        private void Update()
        {
            Vector3 target = restLocalPosition + Vector3.down * (active ? pressedOffset : 0f);
            transform.localPosition = moveSpeed > 0f
                ? Vector3.MoveTowards(transform.localPosition, target, moveSpeed * Time.deltaTime)
                : target;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void HandlePlateStateChanged(PuzzleSignalSource source, bool state)
        {
            SetVisualState(state);
        }

        private void SetVisualState(bool state)
        {
            active = state;
            if (lineRenderer == null)
                return;

            Color color = active ? activeColor : inactiveColor;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }

        private void ResolveReferences()
        {
            if (plate == null)
                plate = GetComponentInParent<PuzzlePressurePlate>();

            if (sourceCollider == null && plate != null)
                sourceCollider = plate.GetComponent<BoxCollider2D>();
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
                        name = "PuzzlePressurePlateVisual_Runtime",
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
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = sortingOrder;

            if (runtimeMaterial != null)
                lineRenderer.sharedMaterial = runtimeMaterial;

            SetVisualState(active);
        }

        private void RefreshGeometry()
        {
            if (lineRenderer == null || sourceCollider == null)
                return;

            Vector3 worldCenter = sourceCollider.transform.TransformPoint(sourceCollider.offset);
            Vector3 center = transform.InverseTransformPoint(worldCenter);
            float width = Mathf.Max(0.02f, sourceCollider.size.x);
            float height = Mathf.Max(0.02f, sourceCollider.size.y);

            lineRenderer.widthMultiplier = height;
            lineRenderer.SetPosition(0, center + Vector3.left * (width * 0.5f));
            lineRenderer.SetPosition(1, center + Vector3.right * (width * 0.5f));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            pressedOffset = Mathf.Max(0f, pressedOffset);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            ResolveReferences();
        }
#endif
    }
}
