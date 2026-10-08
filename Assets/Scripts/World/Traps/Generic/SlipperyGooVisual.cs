using UnityEngine;

namespace Game.World.Traps.Generic
{
    /// <summary>
    /// Replaceable procedural outline for SlipperyGooZone. Gameplay never depends on it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlipperyGooVisual : MonoBehaviour
    {
        [SerializeField] private BoxCollider2D gooCollider;
        [SerializeField] private Color color = new Color(0.08f, 0.95f, 0.22f, 0.9f);
        [SerializeField, Min(0f)] private float waveAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float waveSpeed = 2f;
        [SerializeField, Min(0.1f)] private float waveCount = 2f;
        [SerializeField, Min(0.01f)] private float lineWidth = 0.12f;
        [SerializeField, Range(2, 32)] private int topSegments = 12;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 5;

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;

        private void OnEnable()
        {
            EnsureRenderer();
            if (lineRenderer != null)
            {
                lineRenderer.enabled = true;
                UpdateGeometry();
            }
        }

        private void OnDisable()
        {
            if (lineRenderer != null)
                lineRenderer.enabled = false;
        }

        private void OnValidate()
        {
            waveAmplitude = Mathf.Max(0f, waveAmplitude);
            waveSpeed = Mathf.Max(0f, waveSpeed);
            waveCount = Mathf.Max(0.1f, waveCount);
            lineWidth = Mathf.Max(0.01f, lineWidth);
            topSegments = Mathf.Clamp(topSegments, 2, 32);
        }

        private void Update()
        {
            EnsureRenderer();
            UpdateGeometry();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void EnsureRenderer()
        {
            if (gooCollider == null)
                gooCollider = GetComponentInParent<BoxCollider2D>();

            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
                lineRenderer = gameObject.AddComponent<LineRenderer>();

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "SlipperyGooVisual (Runtime)"
                    };
                    lineRenderer.sharedMaterial = runtimeMaterial;
                }
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.numCornerVertices = 3;
            lineRenderer.numCapVertices = 3;
            lineRenderer.sortingLayerName = sortingLayerName;
            lineRenderer.sortingOrder = sortingOrder;
        }

        private void UpdateGeometry()
        {
            if (lineRenderer == null || gooCollider == null)
                return;

            Vector2 offset = gooCollider.offset;
            Vector2 size = gooCollider.size;
            float left = offset.x - size.x * 0.5f;
            float right = offset.x + size.x * 0.5f;
            float bottom = offset.y - size.y * 0.5f;
            float top = offset.y + size.y * 0.5f;
            int segments = Mathf.Max(2, topSegments);

            lineRenderer.positionCount = segments + 4;
            lineRenderer.SetPosition(0, new Vector3(left, bottom, 0f));
            lineRenderer.SetPosition(1, new Vector3(right, bottom, 0f));
            lineRenderer.SetPosition(2, new Vector3(right, top, 0f));

            float phase = Time.time * waveSpeed;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float x = Mathf.Lerp(right, left, t);
                float edgeFade = Mathf.Sin(Mathf.PI * t);
                float wave = Mathf.Sin(phase + t * Mathf.PI * 2f * waveCount) *
                             waveAmplitude * edgeFade;
                lineRenderer.SetPosition(3 + i, new Vector3(x, top + wave, 0f));
            }
        }
    }
}
