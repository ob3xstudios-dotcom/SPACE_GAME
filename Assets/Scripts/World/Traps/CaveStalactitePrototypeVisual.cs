using UnityEngine;

namespace Game.World.Traps
{
    /// <summary>
    /// Runtime-only placeholder art. Disable or remove this component when final
    /// sprites and an Animator are assigned; all gameplay collision is separate.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CaveStalactitePrototypeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CaveStalactiteController owner;
        [SerializeField] private Transform visualRoot;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 4;

        [Header("Colors")]
        [SerializeField] private Color armedColor = new Color(0.38f, 0.43f, 0.5f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.68f, 0.12f, 1f);
        [SerializeField] private Color fallingColor = new Color(0.95f, 0.18f, 0.08f, 1f);
        [SerializeField] private Color embeddedColor = new Color(0.46f, 0.51f, 0.56f, 1f);
        [SerializeField] private Color crackColor = new Color(0.12f, 0.1f, 0.09f, 1f);

        [Header("Feedback")]
        [SerializeField, Min(0f)] private float warningShakeAmplitude = 0.055f;
        [SerializeField, Min(0.1f)] private float warningShakeFrequency = 22f;
        [SerializeField, Min(0.02f)] private float impactFeedbackDuration = 0.18f;

        private Texture2D solidTexture;
        private Texture2D triangleTexture;
        private Sprite solidSprite;
        private Sprite triangleSprite;
        private Material runtimeMaterial;
        private SpriteRenderer spikeRenderer;
        private SpriteRenderer capRenderer;
        private SpriteRenderer leftCrackRenderer;
        private SpriteRenderer rightCrackRenderer;
        private Vector3 baseLocalPosition;
        private Vector3 baseLocalScale = Vector3.one;
        private float impactTimer;
        private bool defaultsCached;
        private bool hiddenAfterFailure;

        private void Awake()
        {
            EnsureVisual();
        }

        private void OnEnable()
        {
            hiddenAfterFailure = false;
            EnsureVisual();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (owner == null || visualRoot == null || spikeRenderer == null || hiddenAfterFailure)
                return;

            CaveStalactiteState state = owner.CurrentState;
            bool warning = state == CaveStalactiteState.Warning ||
                           state == CaveStalactiteState.WaitingForDropZone;
            bool embedded = state == CaveStalactiteState.Embedded;

            Color bodyColor = armedColor;
            Vector3 localPosition = baseLocalPosition;
            Vector3 localScale = baseLocalScale;

            if (warning)
            {
                float progress = state == CaveStalactiteState.WaitingForDropZone
                    ? 1f
                    : owner.WarningProgress;
                float shake = Mathf.Sin(Time.time * warningShakeFrequency) *
                              warningShakeAmplitude *
                              Mathf.Lerp(0.35f, 1f, progress);
                localPosition += Vector3.right * shake;
                localScale *= 1f + Mathf.Sin(Time.time * 12f) * 0.025f;
                bodyColor = Color.Lerp(armedColor, warningColor, 0.45f + progress * 0.55f);
            }
            else if (state == CaveStalactiteState.Falling)
            {
                bodyColor = fallingColor;
                localScale = Vector3.Scale(baseLocalScale, new Vector3(0.96f, 1.05f, 1f));
            }
            else if (embedded)
            {
                bodyColor = embeddedColor;
                if (impactTimer > 0f)
                {
                    impactTimer = Mathf.Max(0f, impactTimer - Time.deltaTime);
                    float normalized = impactTimer / Mathf.Max(0.02f, impactFeedbackDuration);
                    float pulse = 1f + Mathf.Sin((1f - normalized) * Mathf.PI) * 0.12f;
                    localScale = baseLocalScale * pulse;
                }
            }

            visualRoot.localPosition = localPosition;
            visualRoot.localScale = localScale;
            spikeRenderer.color = bodyColor;
            if (capRenderer != null)
            {
                capRenderer.color = embedded
                    ? Color.Lerp(embeddedColor, Color.white, 0.12f)
                    : bodyColor;
            }

            SetCracksVisible(embedded);
        }

        public void NotifyStateChanged(CaveStalactiteState state)
        {
            EnsureVisual();
            if (state != CaveStalactiteState.Embedded)
                impactTimer = 0f;
        }

        public void PlayImpact()
        {
            EnsureVisual();
            impactTimer = Mathf.Max(0.02f, impactFeedbackDuration);
        }

        public void HideAfterFailedFall()
        {
            hiddenAfterFailure = true;
            if (visualRoot != null)
                visualRoot.gameObject.SetActive(false);
        }

        private void EnsureVisual()
        {
            ResolveReferences();
            if (visualRoot == null)
                return;

            visualRoot.gameObject.SetActive(true);
            EnsureRuntimeResources();
            if (!defaultsCached)
            {
                baseLocalPosition = visualRoot.localPosition;
                baseLocalScale = visualRoot.localScale;
                defaultsCached = true;
            }

            spikeRenderer = EnsurePart(
                visualRoot,
                "PrototypeSpike",
                Vector3.zero,
                new Vector3(0.9f, 1.2f, 1f),
                0f,
                armedColor,
                sortingOrder,
                triangleSprite);
            capRenderer = EnsurePart(
                visualRoot,
                "PrototypePlatformCap",
                new Vector3(0f, 1.08f, 0f),
                new Vector3(1.25f, 0.16f, 1f),
                0f,
                armedColor,
                sortingOrder + 1,
                solidSprite);
            leftCrackRenderer = EnsurePart(
                visualRoot,
                "PrototypeCrackLeft",
                new Vector3(-0.23f, -1.06f, 0f),
                new Vector3(0.34f, 0.045f, 1f),
                24f,
                crackColor,
                sortingOrder + 2,
                solidSprite);
            rightCrackRenderer = EnsurePart(
                visualRoot,
                "PrototypeCrackRight",
                new Vector3(0.25f, -1.08f, 0f),
                new Vector3(0.32f, 0.045f, 1f),
                -20f,
                crackColor,
                sortingOrder + 2,
                solidSprite);
            SetCracksVisible(owner != null && owner.CurrentState == CaveStalactiteState.Embedded);
        }

        private SpriteRenderer EnsurePart(
            Transform parent,
            string partName,
            Vector3 localPosition,
            Vector3 localScale,
            float localRotationZ,
            Color color,
            int order,
            Sprite sprite)
        {
            Transform part = parent.Find(partName);
            if (part == null)
            {
                GameObject partObject = new GameObject(partName);
                partObject.transform.SetParent(parent, false);
                part = partObject.transform;
            }

            part.gameObject.layer = gameObject.layer;
            part.localPosition = localPosition;
            part.localRotation = Quaternion.Euler(0f, 0f, localRotationZ);
            part.localScale = localScale;

            SpriteRenderer renderer = part.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = part.gameObject.AddComponent<SpriteRenderer>();

            renderer.enabled = true;
            renderer.sprite = sprite;
            if (runtimeMaterial != null)
                renderer.sharedMaterial = runtimeMaterial;
            renderer.color = color;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = order;
            return renderer;
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Cave Stalactite Solid Texture Runtime",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                solidTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                solidTexture.Apply(false, true);
            }

            if (triangleTexture == null)
                triangleTexture = BuildTriangleTexture(48, 96);
            if (solidSprite == null)
                solidSprite = CreateSprite(solidTexture, "Cave Stalactite Solid Sprite Runtime", 2f);
            if (triangleSprite == null)
                triangleSprite = CreateSprite(triangleTexture, "Cave Stalactite Spike Sprite Runtime", 48f);

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Cave Stalactite Material Runtime",
                        hideFlags = HideFlags.DontSave
                    };
                }
            }
        }

        private void ResolveReferences()
        {
            if (owner == null)
                owner = GetComponent<CaveStalactiteController>();
            if (visualRoot == null)
                visualRoot = transform.Find("Visual");
        }

        private void SetCracksVisible(bool visible)
        {
            if (leftCrackRenderer != null)
                leftCrackRenderer.enabled = visible;
            if (rightCrackRenderer != null)
                rightCrackRenderer.enabled = visible;
        }

        private static Sprite CreateSprite(Texture2D texture, string spriteName, float pixelsPerUnit)
        {
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            sprite.name = spriteName;
            return sprite;
        }

        private static Texture2D BuildTriangleTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Cave Stalactite Triangle Texture Runtime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color[] pixels = new Color[width * height];
            float centerX = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float height01 = y / (float)(height - 1);
                float halfWidth = Mathf.Lerp(0.6f, width * 0.5f - 1f, height01);
                float featherStart = Mathf.Max(0f, halfWidth - 1.25f);
                for (int x = 0; x < width; x++)
                {
                    float distance = Mathf.Abs(x - centerX);
                    float alpha = distance <= featherStart
                        ? 1f
                        : 1f - Mathf.InverseLerp(featherStart, halfWidth, distance);
                    pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDisable()
        {
            impactTimer = 0f;
            if (visualRoot != null && defaultsCached)
            {
                visualRoot.localPosition = baseLocalPosition;
                visualRoot.localScale = baseLocalScale;
            }
        }

        private void OnValidate()
        {
            warningShakeAmplitude = Mathf.Max(0f, warningShakeAmplitude);
            warningShakeFrequency = Mathf.Max(0.1f, warningShakeFrequency);
            impactFeedbackDuration = Mathf.Max(0.02f, impactFeedbackDuration);
        }

        private void OnDestroy()
        {
            if (solidSprite != null)
                Destroy(solidSprite);
            if (triangleSprite != null)
                Destroy(triangleSprite);
            if (solidTexture != null)
                Destroy(solidTexture);
            if (triangleTexture != null)
                Destroy(triangleTexture);
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }
    }
}
