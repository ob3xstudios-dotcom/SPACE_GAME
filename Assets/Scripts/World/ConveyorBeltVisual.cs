using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Replaceable, dependency-free provisional rendering for ConveyorBelt.
    /// Moving chevrons communicate surface direction without affecting physics.
    /// </summary>
    public sealed class ConveyorBeltVisual : MonoBehaviour
    {
        private const int ArrowCount = 6;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color beltColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        [SerializeField] private Color outlineColor = new Color(0.5f, 0.56f, 0.62f, 1f);
        [SerializeField] private Color arrowColor = new Color(0.2f, 0.85f, 1f, 1f);

        private float beltLength = 4f;
        private float beltHeight = 0.75f;
        private float signedSurfaceSpeed = 3f;
        private LineRenderer beltLine;
        private LineRenderer outlineLine;
        private LineRenderer[] arrowLines;
        private Material runtimeMaterial;
        private bool initialized;

        private void Awake()
        {
            EnsureVisuals();
            UpdateStaticGeometry();
        }

        private void LateUpdate()
        {
            if (initialized)
                UpdateArrowGeometry(Time.time);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void Configure(float length, float height, float signedSpeed)
        {
            beltLength = Mathf.Max(0.1f, length);
            beltHeight = Mathf.Max(0.1f, height);
            signedSurfaceSpeed = signedSpeed;

            if (initialized)
                UpdateStaticGeometry();
        }

        private void EnsureVisuals()
        {
            if (initialized)
                return;

            runtimeMaterial = CreateRuntimeMaterial();
            beltLine = CreateLine("Belt", sortingOrder, 2);
            outlineLine = CreateLine("Outline", sortingOrder + 1, 4);
            arrowLines = new LineRenderer[ArrowCount];

            for (int i = 0; i < arrowLines.Length; i++)
                arrowLines[i] = CreateLine($"Direction_{i + 1}", sortingOrder + 2, 3);

            beltLine.startColor = beltColor;
            beltLine.endColor = beltColor;
            outlineLine.startColor = outlineColor;
            outlineLine.endColor = outlineColor;
            outlineLine.loop = true;

            for (int i = 0; i < arrowLines.Length; i++)
            {
                arrowLines[i].startColor = arrowColor;
                arrowLines[i].endColor = arrowColor;
            }

            initialized = true;
            UpdateStaticGeometry();
        }

        private LineRenderer CreateLine(string objectName, int order, int pointCount)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = pointCount;
            line.numCapVertices = 0;
            line.numCornerVertices = 2;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = order;

            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;

            return line;
        }

        private void UpdateStaticGeometry()
        {
            if (!initialized)
                return;

            float halfLength = beltLength * 0.5f;
            float halfHeight = beltHeight * 0.5f;
            beltLine.widthMultiplier = beltHeight;
            beltLine.SetPosition(0, new Vector3(-halfLength, 0f, 0f));
            beltLine.SetPosition(1, new Vector3(halfLength, 0f, 0f));

            outlineLine.widthMultiplier = Mathf.Clamp(beltHeight * 0.06f, 0.025f, 0.08f);
            outlineLine.SetPosition(0, new Vector3(-halfLength, -halfHeight, 0f));
            outlineLine.SetPosition(1, new Vector3(halfLength, -halfHeight, 0f));
            outlineLine.SetPosition(2, new Vector3(halfLength, halfHeight, 0f));
            outlineLine.SetPosition(3, new Vector3(-halfLength, halfHeight, 0f));

            float arrowWidth = Mathf.Clamp(beltHeight * 0.07f, 0.025f, 0.075f);
            for (int i = 0; i < arrowLines.Length; i++)
                arrowLines[i].widthMultiplier = arrowWidth;

            UpdateArrowGeometry(0f);
        }

        private void UpdateArrowGeometry(float time)
        {
            if (arrowLines == null || arrowLines.Length == 0)
                return;

            int directionSign = signedSurfaceSpeed < 0f ? -1 : 1;
            float halfLength = beltLength * 0.5f;
            float spacing = beltLength / arrowLines.Length;
            float travel = Mathf.Repeat(
                time * Mathf.Abs(signedSurfaceSpeed) * 0.45f,
                spacing);
            float arrowHalfWidth = Mathf.Min(spacing * 0.22f, beltHeight * 0.28f);
            float arrowHalfHeight = beltHeight * 0.22f;

            for (int i = 0; i < arrowLines.Length; i++)
            {
                float baseX = -halfLength + spacing * (i + 0.5f);
                float movingX = baseX + travel * directionSign;
                float wrappedX = Mathf.Repeat(movingX + halfLength, beltLength) - halfLength;
                float tailX = wrappedX - directionSign * arrowHalfWidth;
                float tipX = wrappedX + directionSign * arrowHalfWidth;

                arrowLines[i].SetPosition(0, new Vector3(tailX, arrowHalfHeight, 0f));
                arrowLines[i].SetPosition(1, new Vector3(tipX, 0f, 0f));
                arrowLines[i].SetPosition(2, new Vector3(tailX, -arrowHalfHeight, 0f));
            }
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
