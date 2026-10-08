using UnityEngine;

namespace Game.World.Traps
{
    /// <summary>
    /// Dependency-free provisional visual. It can be replaced without changing trap timing or damage.
    /// </summary>
    public sealed class FlameJetVisual : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color nozzleColor = new Color(0.28f, 0.3f, 0.34f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.65f, 0.08f, 1f);
        [SerializeField] private Color flameStartColor = new Color(1f, 0.95f, 0.25f, 1f);
        [SerializeField] private Color flameEndColor = new Color(1f, 0.12f, 0.02f, 0.15f);

        private float jetLength = 4f;
        private float jetWidth = 1f;
        private LineRenderer nozzleLine;
        private LineRenderer warningLine;
        private LineRenderer flameLine;
        private Material runtimeMaterial;
        private bool initialized;
        private bool stateAssigned;
        private bool warningActive;
        private bool emittingActive;

        private void Awake()
        {
            EnsureVisuals();
            UpdateStaticGeometry();
            ApplyState();
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;

            float time = Time.time;
            if (warningLine.enabled)
                AnimateWarning(time);

            if (flameLine.enabled)
                UpdateFlameGeometry(time);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void Configure(float length, float width)
        {
            jetLength = Mathf.Max(0.1f, length);
            jetWidth = Mathf.Max(0.1f, width);

            if (initialized)
                UpdateStaticGeometry();
        }

        public void SetState(bool warning, bool emitting)
        {
            stateAssigned = true;
            warningActive = warning;
            emittingActive = emitting;
            EnsureVisuals();
            ApplyState();
        }

        private void ApplyState()
        {
            bool showWarning = stateAssigned && warningActive && !emittingActive;
            bool showFlame = stateAssigned && emittingActive;
            warningLine.enabled = showWarning;
            flameLine.enabled = showFlame;

            if (warningLine.enabled)
                AnimateWarning(Time.time);

            if (flameLine.enabled)
                UpdateFlameGeometry(Time.time);
        }

        private void EnsureVisuals()
        {
            if (initialized)
                return;

            runtimeMaterial = CreateRuntimeMaterial();
            nozzleLine = CreateLine("Nozzle", sortingOrder, 4);
            warningLine = CreateLine("Warning", sortingOrder + 1, 2);
            flameLine = CreateLine("Flame", sortingOrder + 2, 7);

            nozzleLine.loop = true;
            nozzleLine.startColor = nozzleColor;
            nozzleLine.endColor = nozzleColor;

            flameLine.colorGradient = BuildFlameGradient();
            flameLine.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.34f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.7f, 0.7f),
                new Keyframe(1f, 0.08f));

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
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
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

            float nozzleHalfHeight = Mathf.Max(0.18f, jetWidth * 0.34f);
            float nozzleBack = Mathf.Max(0.35f, jetWidth * 0.45f);
            nozzleLine.widthMultiplier = Mathf.Clamp(jetWidth * 0.09f, 0.04f, 0.12f);
            nozzleLine.SetPosition(0, new Vector3(-nozzleBack, -nozzleHalfHeight, 0f));
            nozzleLine.SetPosition(1, new Vector3(0.05f, -nozzleHalfHeight, 0f));
            nozzleLine.SetPosition(2, new Vector3(0.05f, nozzleHalfHeight, 0f));
            nozzleLine.SetPosition(3, new Vector3(-nozzleBack, nozzleHalfHeight, 0f));

            warningLine.SetPosition(0, Vector3.zero);
            warningLine.SetPosition(1, new Vector3(Mathf.Min(jetLength, 0.65f), 0f, 0f));
            UpdateFlameGeometry(0f);
        }

        private void AnimateWarning(float time)
        {
            float pulse = 0.55f + Mathf.PingPong(time * 3.5f, 0.45f);
            Color color = warningColor;
            color.a *= pulse;
            warningLine.startColor = color;
            warningLine.endColor = color;
            warningLine.widthMultiplier = jetWidth * Mathf.Lerp(0.12f, 0.28f, pulse);
        }

        private void UpdateFlameGeometry(float time)
        {
            if (flameLine == null)
                return;

            int lastIndex = flameLine.positionCount - 1;
            for (int i = 0; i <= lastIndex; i++)
            {
                float normalized = i / (float)lastIndex;
                float envelope = Mathf.Sin(normalized * Mathf.PI);
                float wave = Mathf.Sin((time * 16f) - (normalized * 13f));
                float y = wave * envelope * jetWidth * 0.11f;
                flameLine.SetPosition(i, new Vector3(jetLength * normalized, y, 0f));
            }

            flameLine.widthMultiplier = jetWidth * (0.92f + Mathf.Sin(time * 19f) * 0.06f);
        }

        private Gradient BuildFlameGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(flameStartColor, 0f),
                    new GradientColorKey(new Color(1f, 0.42f, 0.03f), 0.48f),
                    new GradientColorKey(flameEndColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(flameStartColor.a, 0f),
                    new GradientAlphaKey(0.9f, 0.65f),
                    new GradientAlphaKey(flameEndColor.a, 1f)
                });
            return gradient;
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
