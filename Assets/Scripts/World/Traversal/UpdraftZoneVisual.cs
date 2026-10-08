using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Game.World.Traversal
{
    [DisallowMultipleComponent]
    public sealed class UpdraftZoneVisual : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private UpdraftZone sourceZone;
        [SerializeField] private BoxCollider2D sourceCollider;
        [SerializeField] private bool visualEnabled = true;

        [Header("Air Streams")]
        [SerializeField, Range(4, 32)] private int streamCount = 14;
        [FormerlySerializedAs("riseSpeed")]
        [SerializeField, Min(0.01f)] private float flowSpeed = 1.8f;
        [SerializeField, Min(0.01f)] private float segmentLength = 0.38f;
        [SerializeField, Min(0.005f)] private float lineWidth = 0.035f;
        [SerializeField] private Color streamColor =
            new Color(0.25f, 0.9f, 1f, 0.32f);

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Environment Art";
        [SerializeField] private int sortingOrder;

        private LineRenderer[] streams;
        private Material streamMaterial;

        private void Awake()
        {
            ResolveSourceReferences();
            EnsureStreams();
        }

        private void OnEnable()
        {
            ResolveSourceReferences();
            EnsureStreams();
            SetStreamsEnabled(visualEnabled);
        }

        private void Update()
        {
            if (!visualEnabled)
            {
                SetStreamsEnabled(false);
                return;
            }

            ResolveSourceReferences();
            if (sourceZone == null || sourceCollider == null || !sourceCollider.enabled)
            {
                SetStreamsEnabled(false);
                return;
            }

            EnsureStreams();
            SetStreamsEnabled(true);
            AnimateStreams();
        }

        private void OnDisable()
        {
            SetStreamsEnabled(false);
        }

        private void OnDestroy()
        {
            if (streamMaterial != null)
                Destroy(streamMaterial);
        }

        private void ResolveSourceReferences()
        {
            if (sourceZone == null)
                sourceZone = GetComponentInParent<UpdraftZone>();

            if (sourceCollider == null)
                sourceCollider = GetComponentInParent<BoxCollider2D>();
        }

        private void EnsureStreams()
        {
            int desiredCount = Mathf.Clamp(streamCount, 4, 32);
            if (streams != null && streams.Length == desiredCount)
                return;

            if (streams != null)
            {
                for (int i = 0; i < streams.Length; i++)
                {
                    if (streams[i] != null)
                        Destroy(streams[i].gameObject);
                }
            }

            EnsureMaterial();
            if (streamMaterial == null)
                return;

            streams = new LineRenderer[desiredCount];
            for (int i = 0; i < streams.Length; i++)
            {
                GameObject streamObject = new GameObject($"AirStream_{i + 1:00}");
                streamObject.layer = gameObject.layer;
                streamObject.transform.SetParent(transform, false);

                LineRenderer stream = streamObject.AddComponent<LineRenderer>();
                stream.useWorldSpace = true;
                stream.positionCount = 2;
                stream.alignment = LineAlignment.View;
                stream.textureMode = LineTextureMode.Stretch;
                stream.numCapVertices = 2;
                stream.widthMultiplier = lineWidth;
                stream.startColor = new Color(
                    streamColor.r,
                    streamColor.g,
                    streamColor.b,
                    streamColor.a * 0.25f);
                stream.endColor = streamColor;
                stream.sharedMaterial = streamMaterial;
                stream.shadowCastingMode = ShadowCastingMode.Off;
                stream.receiveShadows = false;
                stream.sortingLayerName = sortingLayerName;
                stream.sortingOrder = sortingOrder;
                streams[i] = stream;
            }
        }

        private void EnsureMaterial()
        {
            if (streamMaterial != null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError("[UPDRAFT VISUAL] No compatible unlit shader was found.", this);
                enabled = false;
                return;
            }

            streamMaterial = new Material(shader)
            {
                name = "UpdraftZoneVisual_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void AnimateStreams()
        {
            Bounds bounds = sourceCollider.bounds;
            Vector2 direction = sourceZone.FlowDirection;
            Vector2 perpendicular = direction.x == 0f ? Vector2.right : Vector2.up;
            GetProjectionRange(bounds, direction, out float flowMin, out float flowMax);
            GetProjectionRange(
                bounds,
                perpendicular,
                out float perpendicularMin,
                out float perpendicularMax);
            float flowLength = Mathf.Max(0.01f, flowMax - flowMin);
            float perpendicularLength = Mathf.Max(0.01f, perpendicularMax - perpendicularMin);
            float travelLength = Mathf.Max(0.01f, flowLength + segmentLength);

            for (int i = 0; i < streams.Length; i++)
            {
                LineRenderer stream = streams[i];
                if (stream == null)
                    continue;

                float lane = (i + 0.5f) / streams.Length;
                float sway = Mathf.Sin(Time.time * 0.7f + i * 2.17f) *
                    perpendicularLength * 0.025f;
                float laneCoordinate = Mathf.Lerp(perpendicularMin, perpendicularMax, lane) + sway;
                float phase = Hash01(i) * travelLength;
                float centerCoordinate = flowMin - segmentLength * 0.5f +
                    Mathf.Repeat(phase + Time.time * flowSpeed, travelLength);
                float startCoordinate = Mathf.Max(
                    flowMin,
                    centerCoordinate - segmentLength * 0.5f);
                float endCoordinate = Mathf.Min(
                    flowMax,
                    centerCoordinate + segmentLength * 0.5f);
                Vector2 start = direction * startCoordinate + perpendicular * laneCoordinate;
                Vector2 end = direction * endCoordinate + perpendicular * laneCoordinate;

                stream.widthMultiplier = lineWidth;
                stream.SetPosition(0, new Vector3(start.x, start.y, transform.position.z));
                stream.SetPosition(1, new Vector3(end.x, end.y, transform.position.z));
            }
        }

        private static void GetProjectionRange(
            Bounds bounds,
            Vector2 axis,
            out float minimum,
            out float maximum)
        {
            if (axis.x > 0f)
            {
                minimum = bounds.min.x;
                maximum = bounds.max.x;
                return;
            }

            if (axis.x < 0f)
            {
                minimum = -bounds.max.x;
                maximum = -bounds.min.x;
                return;
            }

            minimum = bounds.min.y;
            maximum = bounds.max.y;
        }

        private void SetStreamsEnabled(bool value)
        {
            if (streams == null)
                return;

            for (int i = 0; i < streams.Length; i++)
            {
                if (streams[i] != null)
                    streams[i].enabled = value;
            }
        }

        private static float Hash01(int index)
        {
            return Mathf.Repeat(Mathf.Sin((index + 1) * 12.9898f) * 43758.5453f, 1f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            streamCount = Mathf.Clamp(streamCount, 4, 32);
            flowSpeed = Mathf.Max(0.01f, flowSpeed);
            segmentLength = Mathf.Max(0.01f, segmentLength);
            lineWidth = Mathf.Max(0.005f, lineWidth);
            ResolveSourceReferences();
        }
#endif
    }
}
