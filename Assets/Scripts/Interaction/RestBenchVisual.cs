using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// Replaceable procedural placeholder for RestBench. It has no physics or interaction logic.
    /// </summary>
    public sealed class RestBenchVisual : MonoBehaviour
    {
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = -1;
        [SerializeField] private Color woodColor = new Color(0.48f, 0.27f, 0.12f, 1f);
        [SerializeField] private Color frameColor = new Color(0.18f, 0.2f, 0.23f, 1f);

        private Material runtimeMaterial;

        private void Awake()
        {
            CreateVisuals();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void CreateVisuals()
        {
            runtimeMaterial = CreateRuntimeMaterial();

            CreateSegment("Seat", new Vector2(-1.25f, 0.58f), new Vector2(1.25f, 0.58f), 0.22f, woodColor);
            CreateSegment("Back", new Vector2(-1.2f, 1.25f), new Vector2(1.2f, 1.25f), 0.2f, woodColor);
            CreateSegment("BackLeft", new Vector2(-1.08f, 0.67f), new Vector2(-1.08f, 1.2f), 0.1f, frameColor);
            CreateSegment("BackRight", new Vector2(1.08f, 0.67f), new Vector2(1.08f, 1.2f), 0.1f, frameColor);
            CreateSegment("LegLeft", new Vector2(-0.95f, 0.52f), new Vector2(-0.78f, 0f), 0.12f, frameColor);
            CreateSegment("LegRight", new Vector2(0.95f, 0.52f), new Vector2(0.78f, 0f), 0.12f, frameColor);
        }

        private void CreateSegment(string objectName, Vector2 start, Vector2 end, float width, Color color)
        {
            GameObject segmentObject = new GameObject(objectName);
            segmentObject.layer = gameObject.layer;
            segmentObject.transform.SetParent(transform, false);

            LineRenderer line = segmentObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = 2;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = sortingOrder;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;
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
