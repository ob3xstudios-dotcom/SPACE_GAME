using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    public enum WoodMachineVisualKind
    {
        Turner,
        Disposal
    }

    /// <summary>
    /// Simple procedural placeholder for non-art machine prefabs. It has no physics
    /// role and can be removed when final artwork is assigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WoodMachineVisual : MonoBehaviour
    {
        [SerializeField] private WoodMachineVisualKind visualKind;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color frameColor = new Color(0.36f, 0.42f, 0.48f, 1f);
        [SerializeField] private Color accentColor = new Color(1f, 0.64f, 0.12f, 1f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.08f;

        private Material runtimeMaterial;

        private void Awake()
        {
            runtimeMaterial = CreateRuntimeMaterial();

            if (visualKind == WoodMachineVisualKind.Turner)
                CreateTurnerVisual();
            else
                CreateDisposalVisual();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void CreateTurnerVisual()
        {
            LineRenderer frame = CreateLine("TurnerFrame", sortingOrder, 6, false);
            frame.startColor = frameColor;
            frame.endColor = frameColor;
            frame.SetPositions(new[]
            {
                new Vector3(-0.95f, -1.05f, 0f),
                new Vector3(-0.95f, 0.95f, 0f),
                new Vector3(0.35f, 0.95f, 0f),
                new Vector3(0.85f, 0.45f, 0f),
                new Vector3(0.85f, -1.05f, 0f),
                new Vector3(-0.95f, -1.05f, 0f)
            });

            LineRenderer arm = CreateLine("TurnerArm", sortingOrder + 1, 3, false);
            arm.startColor = accentColor;
            arm.endColor = accentColor;
            arm.SetPositions(new[]
            {
                new Vector3(-0.45f, 0.45f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(0.5f, -0.35f, 0f)
            });
        }

        private void CreateDisposalVisual()
        {
            LineRenderer rim = CreateLine("DisposalRim", sortingOrder, 4, true);
            rim.startColor = frameColor;
            rim.endColor = frameColor;
            rim.SetPositions(new[]
            {
                new Vector3(-1.1f, -0.25f, 0f),
                new Vector3(1.1f, -0.25f, 0f),
                new Vector3(0.72f, 0.25f, 0f),
                new Vector3(-0.72f, 0.25f, 0f)
            });

            LineRenderer depth = CreateLine("DisposalDepth", sortingOrder - 1, 4, false);
            depth.startColor = accentColor;
            depth.endColor = accentColor;
            depth.SetPositions(new[]
            {
                new Vector3(-0.95f, -0.22f, 0f),
                new Vector3(-0.6f, -0.8f, 0f),
                new Vector3(0.6f, -0.8f, 0f),
                new Vector3(0.95f, -0.22f, 0f)
            });
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
