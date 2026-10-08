using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Replaceable collider-free procedural visual for PuzzleLever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleLeverVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PuzzleLever lever;

        [Header("Appearance")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color inactiveColor = new Color(0.95f, 0.55f, 0.12f, 1f);
        [SerializeField] private Color activeColor = new Color(0.2f, 0.95f, 0.35f, 1f);
        [SerializeField, Min(0.02f)] private float lineWidth = 0.12f;
        [SerializeField, Min(0.05f)] private float baseWidth = 0.9f;
        [SerializeField, Min(0.05f)] private float handleLength = 0.85f;
        [SerializeField, Range(0f, 80f)] private float handleAngle = 35f;
        [SerializeField, Min(0f)] private float rotationSpeed = 360f;

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;
        private float currentAngle;
        private float targetAngle;

        private void Awake()
        {
            ResolveLever();
            EnsureRenderer();
            SetVisualState(lever != null && lever.IsActive, true);
        }

        private void OnEnable()
        {
            ResolveLever();
            Subscribe();
            EnsureRenderer();
            SetVisualState(lever != null && lever.IsActive, true);

            if (lineRenderer != null)
                lineRenderer.enabled = true;
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (lineRenderer != null)
                lineRenderer.enabled = false;
        }

        private void Update()
        {
            if (Mathf.Approximately(currentAngle, targetAngle))
                return;

            currentAngle = rotationSpeed <= 0f
                ? targetAngle
                : Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            RefreshGeometry();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void ResolveLever()
        {
            if (lever == null)
                lever = GetComponentInParent<PuzzleLever>();
        }

        private void Subscribe()
        {
            if (lever == null)
                return;

            lever.StateChanged -= HandleStateChanged;
            lever.StateChanged += HandleStateChanged;
        }

        private void Unsubscribe()
        {
            if (lever != null)
                lever.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(PuzzleSignalSource source, bool active)
        {
            SetVisualState(active, false);
        }

        private void SetVisualState(bool active, bool immediate)
        {
            targetAngle = active ? -handleAngle : handleAngle;
            if (immediate || rotationSpeed <= 0f)
                currentAngle = targetAngle;

            if (lineRenderer != null)
            {
                Color color = active ? activeColor : inactiveColor;
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
            }

            RefreshGeometry();
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
                        name = "PuzzleLeverVisual_Runtime",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = false;
            lineRenderer.positionCount = 4;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.numCapVertices = 2;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = sortingOrder;

            if (runtimeMaterial != null)
                lineRenderer.sharedMaterial = runtimeMaterial;
        }

        private void RefreshGeometry()
        {
            if (lineRenderer == null)
                return;

            Vector3 pivot = Vector3.zero;
            Vector3 direction = Quaternion.Euler(0f, 0f, currentAngle) * Vector3.up;
            lineRenderer.SetPosition(0, Vector3.left * (baseWidth * 0.5f));
            lineRenderer.SetPosition(1, Vector3.right * (baseWidth * 0.5f));
            lineRenderer.SetPosition(2, pivot);
            lineRenderer.SetPosition(3, pivot + direction * handleLength);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lineWidth = Mathf.Max(0.02f, lineWidth);
            baseWidth = Mathf.Max(0.05f, baseWidth);
            handleLength = Mathf.Max(0.05f, handleLength);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            ResolveLever();
        }
#endif
    }
}
