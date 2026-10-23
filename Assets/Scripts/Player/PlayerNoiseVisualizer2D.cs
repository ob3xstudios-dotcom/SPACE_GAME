using Game.Events;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerNoiseVisualizer2D : MonoBehaviour
    {
        private const int MinimumSegments = 16;
        private const int MaximumSegments = 128;
        private const int MinimumPoolSize = 1;
        private const int MaximumPoolSize = 12;

        [Header("Channel")]
        [SerializeField] private GameplayNoiseEventChannelSO noiseChannel;

        [Header("Circle")]
        [SerializeField] private Color circleColor = new Color(0.78f, 0.92f, 1f, 0.2f);
        [SerializeField, Min(0.001f)] private float lineWidth = 0.02f;
        [SerializeField, Range(MinimumSegments, MaximumSegments)] private int segments = 64;
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = -1;

        [Header("Timing")]
        [Tooltip("Multiplier applied only to presentation lifetime; it never changes enemy perception.")]
        [SerializeField, Min(0.01f)] private float durationMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float minimumVisibleDuration = 0.2f;
        [SerializeField] private bool expandWave = true;
        [SerializeField, Range(0f, 1f)] private float initialRadiusFraction = 0.08f;

        [Header("Reuse")]
        [SerializeField, Range(MinimumPoolSize, MaximumPoolSize)] private int circlePoolSize = 6;

        private sealed class CircleSlot
        {
            public LineRenderer Line;
            public Vector2 Position;
            public float Radius;
            public float StartTime;
            public float EndTime;
        }

        private CircleSlot[] slots;
        private Material lineMaterial;
        private int nextSlot;

        private void Awake()
        {
            BuildPool();
        }

        private void OnEnable()
        {
            BuildPool();
            noiseChannel?.Subscribe(HandleNoise);
        }

        private void OnDisable()
        {
            noiseChannel?.Unsubscribe(HandleNoise);
            HideAll();
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
                Destroy(lineMaterial);
        }

        private void Update()
        {
            if (slots == null)
                return;

            float now = Time.time;
            for (int i = 0; i < slots.Length; i++)
            {
                CircleSlot slot = slots[i];
                if (slot.Line == null || !slot.Line.enabled)
                    continue;

                if (now >= slot.EndTime)
                {
                    slot.Line.enabled = false;
                    continue;
                }

                float duration = Mathf.Max(0.0001f, slot.EndTime - slot.StartTime);
                float progress = Mathf.Clamp01((now - slot.StartTime) / duration);
                float radius = expandWave
                    ? slot.Radius * Mathf.Lerp(initialRadiusFraction, 1f, progress)
                    : slot.Radius;
                Color color = circleColor;
                color.a *= 1f - progress;
                slot.Line.startColor = color;
                slot.Line.endColor = color;
                SetCirclePositions(slot.Line, slot.Position, radius);
            }
        }

        private void HandleNoise(GameplayNoiseEvent noiseEvent)
        {
            if (!isActiveAndEnabled || noiseEvent.Emitter != gameObject || noiseEvent.Radius <= 0f)
                return;

            BuildPool();
            if (slots == null || slots.Length == 0)
                return;

            CircleSlot slot = slots[nextSlot];
            nextSlot = (nextSlot + 1) % slots.Length;
            slot.Position = noiseEvent.Position;
            slot.Radius = noiseEvent.Radius;
            slot.StartTime = Time.time;
            slot.EndTime = slot.StartTime + Mathf.Max(
                minimumVisibleDuration,
                noiseEvent.Duration * durationMultiplier);
            slot.Line.startColor = circleColor;
            slot.Line.endColor = circleColor;
            slot.Line.enabled = true;
            SetCirclePositions(
                slot.Line,
                slot.Position,
                expandWave ? slot.Radius * initialRadiusFraction : slot.Radius);
        }

        private void BuildPool()
        {
            int desiredSize = Mathf.Clamp(circlePoolSize, MinimumPoolSize, MaximumPoolSize);
            if (slots != null && slots.Length == desiredSize)
                return;

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].Line != null)
                        Destroy(slots[i].Line.gameObject);
                }
            }

            EnsureMaterial();
            slots = new CircleSlot[desiredSize];
            nextSlot = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject circle = new GameObject($"NoiseCircle_{i + 1}");
                circle.transform.SetParent(transform, false);
                LineRenderer line = circle.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = Mathf.Clamp(segments, MinimumSegments, MaximumSegments);
                line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
                line.numCapVertices = 0;
                line.numCornerVertices = 2;
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.TransformZ;
                line.sharedMaterial = lineMaterial;
                line.sortingLayerName = sortingLayerName;
                line.sortingOrder = sortingOrder;
                line.enabled = false;
                slots[i] = new CircleSlot { Line = line };
            }
        }

        private void EnsureMaterial()
        {
            if (lineMaterial != null)
                return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return;

            lineMaterial = new Material(shader)
            {
                name = "Runtime Player Noise Circle",
                hideFlags = HideFlags.DontSave
            };
        }

        private void SetCirclePositions(LineRenderer line, Vector2 center, float radius)
        {
            int count = Mathf.Clamp(segments, MinimumSegments, MaximumSegments);
            if (line.positionCount != count)
                line.positionCount = count;

            float angleStep = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep;
                line.SetPosition(
                    i,
                    new Vector3(
                        center.x + Mathf.Cos(angle) * radius,
                        center.y + Mathf.Sin(angle) * radius,
                        transform.position.z));
            }
        }

        private void HideAll()
        {
            if (slots == null)
                return;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Line != null)
                    slots[i].Line.enabled = false;
            }
        }

        private void OnValidate()
        {
            lineWidth = Mathf.Max(0.001f, lineWidth);
            segments = Mathf.Clamp(segments, MinimumSegments, MaximumSegments);
            durationMultiplier = Mathf.Max(0.01f, durationMultiplier);
            minimumVisibleDuration = Mathf.Max(0.01f, minimumVisibleDuration);
            initialRadiusFraction = Mathf.Clamp01(initialRadiusFraction);
            circlePoolSize = Mathf.Clamp(circlePoolSize, MinimumPoolSize, MaximumPoolSize);
        }
    }
}
