using UnityEngine;

namespace Game.World.Pickups
{
    /// <summary>
    /// Replaceable procedural placeholder art for a health pickup.
    /// It has no collider and does not participate in pickup logic.
    /// </summary>
    public sealed class HealthPickupVisual : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color heartColor = new Color(1f, 0.18f, 0.28f, 1f);
        [SerializeField] private Color crossColor = new Color(1f, 0.9f, 0.9f, 1f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.08f;

        [Header("Motion")]
        [SerializeField] private bool enableMotion = true;
        [SerializeField, Min(0f)] private float floatAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float floatFrequency = 1.4f;
        [SerializeField, Range(0f, 0.25f)] private float pulseAmount = 0.06f;
        [SerializeField, Min(0f)] private float pulseFrequency = 2f;

        private Material runtimeMaterial;
        private Vector3 baseLocalPosition;
        private Vector3 baseLocalScale;

        private void Awake()
        {
            baseLocalPosition = transform.localPosition;
            baseLocalScale = transform.localScale;
            CreateVisuals();
        }

        private void LateUpdate()
        {
            if (!enableMotion)
                return;

            float floatOffset = Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
            float pulse = 1f + Mathf.Sin(Time.time * pulseFrequency) * pulseAmount;

            transform.localPosition = baseLocalPosition + Vector3.up * floatOffset;
            transform.localScale = baseLocalScale * pulse;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void CreateVisuals()
        {
            runtimeMaterial = CreateRuntimeMaterial();

            LineRenderer heart = CreateLine("HeartOutline", sortingOrder, 8, true);
            heart.startColor = heartColor;
            heart.endColor = heartColor;
            heart.SetPositions(new[]
            {
                new Vector3(0f, -0.42f, 0f),
                new Vector3(-0.44f, 0.02f, 0f),
                new Vector3(-0.42f, 0.28f, 0f),
                new Vector3(-0.2f, 0.43f, 0f),
                new Vector3(0f, 0.25f, 0f),
                new Vector3(0.2f, 0.43f, 0f),
                new Vector3(0.42f, 0.28f, 0f),
                new Vector3(0.44f, 0.02f, 0f)
            });

            LineRenderer horizontal = CreateLine("CrossHorizontal", sortingOrder + 1, 2, false);
            horizontal.startColor = crossColor;
            horizontal.endColor = crossColor;
            horizontal.SetPosition(0, new Vector3(-0.14f, 0.02f, 0f));
            horizontal.SetPosition(1, new Vector3(0.14f, 0.02f, 0f));

            LineRenderer vertical = CreateLine("CrossVertical", sortingOrder + 1, 2, false);
            vertical.startColor = crossColor;
            vertical.endColor = crossColor;
            vertical.SetPosition(0, new Vector3(0f, -0.12f, 0f));
            vertical.SetPosition(1, new Vector3(0f, 0.16f, 0f));
        }

        private LineRenderer CreateLine(string objectName, int order, int pointCount, bool loop)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = pointCount;
            line.loop = loop;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = lineWidth;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = order;

            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;

            return line;
        }

        private static Material CreateRuntimeMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");

            return shader != null ? new Material(shader) : null;
        }
    }
}
