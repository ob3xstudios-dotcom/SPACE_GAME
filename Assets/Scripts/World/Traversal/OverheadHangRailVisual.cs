using UnityEngine;

namespace Game.World.Traversal
{
    public enum OverheadRailVisualStyle
    {
        Bar,
        Rope,
        Chain
    }

    /// <summary>
    /// Replaceable procedural preview. OverheadHangRail never depends on this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OverheadHangRailVisual : MonoBehaviour
    {
        [SerializeField] private OverheadHangRail rail;
        [SerializeField] private OverheadRailVisualStyle visualStyle = OverheadRailVisualStyle.Bar;
        [SerializeField] private Color color = new Color(0.72f, 0.78f, 0.82f, 1f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.1f;
        [SerializeField, Min(0.02f)] private float markLength = 0.2f;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 5;

        private LineRenderer mainLine;
        private LineRenderer[] marks;
        private Transform[] markTransforms;
        private Material runtimeMaterial;
        private float drivenPhase;

        private void OnEnable()
        {
            EnsureVisual();
            SetRenderersEnabled(true);
        }

        private void OnDisable()
        {
            SetRenderersEnabled(false);
        }

        private void OnValidate()
        {
            lineWidth = Mathf.Max(0.01f, lineWidth);
            markLength = Mathf.Max(0.02f, markLength);
            if (rail == null)
                rail = GetComponentInParent<OverheadHangRail>();
        }

        private void Update()
        {
            EnsureVisual();
            if (rail == null || !rail.HasValidSegment)
                return;

            float length = Mathf.Max(0.05f, rail.Length);
            if (rail.TransportMode == OverheadRailTransportMode.Driven)
            {
                drivenPhase = Mathf.Repeat(
                    drivenPhase + rail.TransportSpeed / length * Time.deltaTime,
                    1f);
            }

            UpdateGeometry();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void EnsureVisual()
        {
            if (rail == null)
                rail = GetComponentInParent<OverheadHangRail>();

            EnsureMaterial();
            EnsureMainLine();
            EnsureMarks();
        }

        private void EnsureMaterial()
        {
            if (runtimeMaterial != null)
                return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            runtimeMaterial = new Material(shader)
            {
                name = "Overhead Hang Rail Visual (Runtime)"
            };
        }

        private void EnsureMainLine()
        {
            if (mainLine == null)
                mainLine = GetComponent<LineRenderer>();
            if (mainLine == null)
                mainLine = gameObject.AddComponent<LineRenderer>();

            ConfigureLine(mainLine, lineWidth);
            mainLine.positionCount = 2;
        }

        private void EnsureMarks()
        {
            if (marks != null)
                return;

            int markCount = GetMarkCount();
            marks = new LineRenderer[markCount];
            markTransforms = new Transform[markCount];

            for (int i = 0; i < markCount; i++)
            {
                GameObject markObject = new GameObject($"RailMark_{i + 1}");
                markObject.hideFlags = HideFlags.DontSave;
                markObject.layer = gameObject.layer;
                markObject.transform.SetParent(transform, false);

                LineRenderer line = markObject.AddComponent<LineRenderer>();
                ConfigureLine(line, Mathf.Max(0.02f, lineWidth * 0.55f));
                line.positionCount = 2;

                markTransforms[i] = markObject.transform;
                marks[i] = line;
            }
        }

        private void ConfigureLine(LineRenderer line, float width)
        {
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = visualStyle == OverheadRailVisualStyle.Rope ? 2 : 0;
            line.numCornerVertices = 2;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = sortingOrder;
            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;
        }

        private int GetMarkCount()
        {
            switch (visualStyle)
            {
                case OverheadRailVisualStyle.Rope:
                    return 9;
                case OverheadRailVisualStyle.Chain:
                    return 12;
                default:
                    return 5;
            }
        }

        private void UpdateGeometry()
        {
            Vector3 localA = transform.InverseTransformPoint(rail.PointA);
            Vector3 localB = transform.InverseTransformPoint(rail.PointB);
            mainLine.SetPosition(0, localA);
            mainLine.SetPosition(1, localB);

            Vector2 localTangent = transform.InverseTransformVector(rail.GetTangent()).normalized;
            Vector2 localNormal = new Vector2(-localTangent.y, localTangent.x);
            float halfMarkLength = markLength * 0.5f;

            for (int i = 0; i < marks.Length; i++)
            {
                float basePosition = (i + 0.5f) / marks.Length;
                float normalizedPosition = rail.TransportMode == OverheadRailTransportMode.Driven
                    ? Mathf.Repeat(basePosition + drivenPhase, 1f)
                    : basePosition;
                Vector3 worldPosition = rail.GetPositionAtNormalized(normalizedPosition);
                markTransforms[i].localPosition = transform.InverseTransformPoint(worldPosition);
                marks[i].SetPosition(0, -localNormal * halfMarkLength);
                marks[i].SetPosition(1, localNormal * halfMarkLength);
            }
        }

        private void SetRenderersEnabled(bool value)
        {
            if (mainLine != null)
                mainLine.enabled = value;
            if (marks == null)
                return;

            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] != null)
                    marks[i].enabled = value;
            }
        }
    }
}
