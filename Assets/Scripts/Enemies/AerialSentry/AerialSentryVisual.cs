using UnityEngine;

namespace Game.Enemies.AerialSentry
{
    public sealed class AerialSentryVisual : MonoBehaviour
    {
        [Header("Reference")]
        [SerializeField] private AerialSentryRobot owner;

        [Header("Renderers")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer eyeRenderer;
        [SerializeField] private SpriteRenderer coreRenderer;
        [SerializeField] private LineRenderer aimLine;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 20;
        [SerializeField] private bool debugVisualLogs;

        [Header("Charge Telegraph")]
        [SerializeField, Min(0.05f)] private float coreSize = 0.22f;
        [SerializeField, Min(0.05f)] private float chargedCoreSize = 0.52f;
        [SerializeField, Min(0.01f)] private float aimLineWidth = 0.045f;

        [Header("Colors")]
        [SerializeField] private Color bodyColor = new Color(0.28f, 0.34f, 0.42f, 1f);
        [SerializeField] private Color patrolCoreColor = new Color(0.2f, 0.85f, 1f, 1f);
        [SerializeField] private Color acquiringCoreColor = new Color(1f, 0.82f, 0.15f, 1f);
        [SerializeField] private Color pursuingCoreColor = new Color(1f, 0.38f, 0.08f, 1f);
        [SerializeField] private Color searchingCoreColor = new Color(1f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color chargingCoreColor = new Color(1f, 0.12f, 0.06f, 1f);
        [SerializeField] private Color cooldownCoreColor = new Color(0.25f, 0.5f, 0.9f, 1f);

        private Texture2D circleTexture;
        private Sprite circleSprite;
        private Material runtimeMaterial;
        private bool visualStateLogged;

        private void Awake()
        {
            EnsureVisual();
        }

        private void OnEnable()
        {
            EnsureVisual();
        }

        private void LateUpdate()
        {
            if (coreRenderer == null)
                EnsureVisual();
            if (coreRenderer == null)
                return;

            Color coreColor = GetCoreColor();
            float coreVisualSize = coreSize;

            if (owner != null && owner.CurrentState == AerialSentryState.Acquiring)
                coreVisualSize = coreSize * 1.3f;

            if (owner != null && owner.CurrentState == AerialSentryState.Pursuing)
                coreVisualSize = coreSize * 1.2f;

            if (owner != null && owner.CurrentState == AerialSentryState.Searching)
            {
                float searchPulse = 0.75f + Mathf.PingPong(Time.time * 0.9f, 0.45f);
                coreVisualSize = coreSize * searchPulse;
            }

            if (owner != null && owner.CurrentState == AerialSentryState.Charging)
            {
                float progress = owner.ChargeProgress;
                float pulseRate = Mathf.Lerp(5f, 14f, progress);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
                float pulseScale = Mathf.Lerp(1f, Mathf.Lerp(0.88f, 1.16f, pulse), progress);
                coreVisualSize = Mathf.Lerp(coreSize * 1.4f, chargedCoreSize, progress) * pulseScale;
                coreColor = Color.Lerp(coreColor, Color.white, progress * (0.45f + pulse * 0.35f));
            }

            coreRenderer.color = coreColor;
            coreRenderer.transform.localScale = Vector3.one * coreVisualSize;
            if (eyeRenderer != null)
            {
                float eyeHighlight = owner != null && owner.CurrentState == AerialSentryState.Charging
                    ? Mathf.Lerp(0.1f, 0.75f, owner.ChargeProgress)
                    : 0.35f;
                eyeRenderer.color = Color.Lerp(coreColor, Color.white, eyeHighlight);
            }

            UpdateAimLine(coreColor);
        }

        private void EnsureVisual()
        {
            if (owner == null)
                owner = GetComponentInParent<AerialSentryRobot>();

            EnsureRuntimeResources();

            bodyRenderer = EnsurePart(
                bodyRenderer,
                "Body",
                Vector3.zero,
                new Vector3(1.05f, 1.05f, 1f),
                sortingOrder,
                bodyColor);

            eyeRenderer = EnsurePart(
                eyeRenderer,
                "Eye",
                new Vector3(0f, -0.34f, 0f),
                new Vector3(0.34f, 0.22f, 1f),
                sortingOrder + 1,
                patrolCoreColor);

            coreRenderer = EnsurePart(
                coreRenderer,
                "Core",
                new Vector3(0f, 0.03f, 0f),
                Vector3.one * coreSize,
                sortingOrder + 2,
                patrolCoreColor);

            EnsureAimLine();

            LogVisualStateOnce();
        }

        private void EnsureRuntimeResources()
        {
            if (circleTexture == null)
                circleTexture = BuildCircleTexture(64);

            if (circleSprite == null)
            {
                circleSprite = Sprite.Create(
                    circleTexture,
                    new Rect(0f, 0f, circleTexture.width, circleTexture.height),
                    new Vector2(0.5f, 0.5f),
                    circleTexture.width);
                circleSprite.name = "Aerial Sentry Runtime Circle";
            }

            if (runtimeMaterial == null)
                runtimeMaterial = CreateRuntimeMaterial("Aerial Sentry Body Runtime");
        }

        private SpriteRenderer EnsurePart(
            SpriteRenderer renderer,
            string partName,
            Vector3 localPosition,
            Vector3 localScale,
            int order,
            Color color)
        {
            if (renderer == null)
            {
                Transform existingPart = transform.Find(partName);
                if (existingPart == null)
                {
                    GameObject part = new GameObject(partName);
                    part.transform.SetParent(transform, false);
                    existingPart = part.transform;
                }

                renderer = existingPart.GetComponent<SpriteRenderer>();
                if (renderer == null)
                    renderer = existingPart.gameObject.AddComponent<SpriteRenderer>();
            }

            GameObject partObject = renderer.gameObject;
            partObject.layer = gameObject.layer;
            partObject.SetActive(true);
            renderer.transform.SetParent(transform, false);
            renderer.transform.localPosition = localPosition;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = localScale;

            renderer.enabled = true;
            renderer.sprite = circleSprite;
            if (runtimeMaterial != null)
                renderer.sharedMaterial = runtimeMaterial;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = order;
            renderer.color = color;
            return renderer;
        }

        private void EnsureAimLine()
        {
            if (aimLine == null)
            {
                Transform existingLine = transform.Find("AimLine");
                if (existingLine == null)
                {
                    GameObject lineObject = new GameObject("AimLine");
                    lineObject.transform.SetParent(transform, false);
                    existingLine = lineObject.transform;
                }

                aimLine = existingLine.GetComponent<LineRenderer>();
                if (aimLine == null)
                    aimLine = existingLine.gameObject.AddComponent<LineRenderer>();
            }

            aimLine.gameObject.layer = gameObject.layer;
            aimLine.useWorldSpace = true;
            aimLine.loop = false;
            aimLine.positionCount = 2;
            aimLine.enabled = false;
            aimLine.startWidth = aimLineWidth;
            aimLine.endWidth = aimLineWidth * 0.55f;
            aimLine.numCapVertices = 4;
            if (runtimeMaterial != null)
                aimLine.sharedMaterial = runtimeMaterial;
            aimLine.sortingLayerName = sortingLayerName;
            aimLine.sortingOrder = sortingOrder + 3;
        }

        private void UpdateAimLine(Color chargeColor)
        {
            if (aimLine == null)
                return;

            bool showLine = owner != null && owner.CurrentState == AerialSentryState.Charging;
            aimLine.enabled = showLine;
            if (!showLine)
                return;

            float progress = owner.ChargeProgress;
            Color lineColor = Color.Lerp(chargeColor, Color.white, progress * 0.55f);
            lineColor.a = Mathf.Lerp(0.65f, 1f, progress);
            aimLine.startColor = lineColor;
            aimLine.endColor = lineColor;
            aimLine.SetPosition(0, owner.MuzzlePosition);
            aimLine.SetPosition(1, owner.ChargeTarget);
        }

        private static Material CreateRuntimeMaterial(string materialName)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
            return material;
        }

        private void LogVisualStateOnce()
        {
            if (!debugVisualLogs || visualStateLogged || bodyRenderer == null)
                return;

            visualStateLogged = true;
            string shaderName = bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.shader != null
                ? bodyRenderer.sharedMaterial.shader.name
                : "None";
            Debug.Log(
                $"[SENTRY VISUAL] bodyRenderer={bodyRenderer.enabled} " +
                $"active={bodyRenderer.gameObject.activeInHierarchy} " +
                $"sortingLayer={bodyRenderer.sortingLayerName} " +
                $"sortingOrder={bodyRenderer.sortingOrder} " +
                $"layer={bodyRenderer.gameObject.layer} z={bodyRenderer.transform.position.z:0.###} " +
                $"shader={shaderName}",
                this);
        }

        private Color GetCoreColor()
        {
            if (owner == null)
                return patrolCoreColor;

            switch (owner.CurrentState)
            {
                case AerialSentryState.Acquiring:
                    return acquiringCoreColor;
                case AerialSentryState.Pursuing:
                    return pursuingCoreColor;
                case AerialSentryState.Searching:
                    float searchPulse = 0.35f + Mathf.PingPong(Time.time * 0.9f, 0.65f);
                    return Color.Lerp(new Color(
                        searchingCoreColor.r,
                        searchingCoreColor.g,
                        searchingCoreColor.b,
                        0.35f), searchingCoreColor, searchPulse);
                case AerialSentryState.Returning:
                    return cooldownCoreColor;
                case AerialSentryState.Charging:
                    return chargingCoreColor;
                case AerialSentryState.Cooldown:
                    return Color.Lerp(cooldownCoreColor, patrolCoreColor, owner.CooldownProgress);
                default:
                    return patrolCoreColor;
            }
        }

        private static Texture2D BuildCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Aerial Sentry Runtime Circle Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f - 1f;
            float featherStart = radius - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = 1f - Mathf.InverseLerp(featherStart, radius, distance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnValidate()
        {
            coreSize = Mathf.Max(0.05f, coreSize);
            chargedCoreSize = Mathf.Max(coreSize, chargedCoreSize);
            aimLineWidth = Mathf.Max(0.01f, aimLineWidth);
        }

        private void OnDestroy()
        {
            if (circleSprite != null)
                Destroy(circleSprite);
            if (circleTexture != null)
                Destroy(circleTexture);
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }
    }
}
