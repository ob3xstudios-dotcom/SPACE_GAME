using UnityEngine;

namespace Game.Enemies.CrabCrawler
{
    /// <summary>
    /// Runtime-only placeholder art. Disable or remove this component when the final
    /// sprite hierarchy is assigned under the controller's Visual root.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CrabCrawlerPrototypeVisual : MonoBehaviour
    {
        [Header("Reference")]
        [SerializeField] private CrabCrawlerController owner;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 10;

        [Header("Colors")]
        [SerializeField] private Color bodyColor = new Color(0.58f, 0.18f, 0.12f, 1f);
        [SerializeField] private Color shellColor = new Color(0.82f, 0.32f, 0.16f, 1f);
        [SerializeField] private Color legColor = new Color(0.34f, 0.09f, 0.07f, 1f);
        [SerializeField] private Color patrolEyeColor = new Color(0.25f, 0.95f, 1f, 1f);
        [SerializeField] private Color windupEyeColor = new Color(1f, 0.8f, 0.1f, 1f);
        [SerializeField] private Color lungeEyeColor = new Color(1f, 0.12f, 0.05f, 1f);
        [SerializeField] private Color recoveryEyeColor = new Color(0.35f, 0.55f, 1f, 1f);

        private Texture2D solidTexture;
        private Texture2D circleTexture;
        private Sprite solidSprite;
        private Sprite circleSprite;
        private Material runtimeMaterial;
        private SpriteRenderer shellRenderer;
        private SpriteRenderer frontEyeRenderer;
        private SpriteRenderer rearEyeRenderer;
        private Vector3 frontEyeScale;
        private Vector3 rearEyeScale;

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
            if (owner == null)
                owner = GetComponentInParent<CrabCrawlerController>();
            if (owner == null || frontEyeRenderer == null || rearEyeRenderer == null)
                return;

            Color eyeColor = patrolEyeColor;
            float pulseScale = 1f;
            switch (owner.CurrentState)
            {
                case CrabCrawlerState.Windup:
                    eyeColor = Color.Lerp(windupEyeColor, lungeEyeColor, owner.WindupProgress);
                    pulseScale = 0.9f + Mathf.PingPong(Time.time * 5f, 0.25f);
                    break;
                case CrabCrawlerState.Lunging:
                    eyeColor = lungeEyeColor;
                    pulseScale = 1.2f;
                    break;
                case CrabCrawlerState.Recovering:
                    eyeColor = recoveryEyeColor;
                    pulseScale = 0.9f;
                    break;
            }

            frontEyeRenderer.color = eyeColor;
            rearEyeRenderer.color = eyeColor;
            frontEyeRenderer.transform.localScale = frontEyeScale * pulseScale;
            rearEyeRenderer.transform.localScale = rearEyeScale * pulseScale;
            if (shellRenderer != null)
                shellRenderer.color = owner.CurrentState == CrabCrawlerState.Lunging
                    ? Color.Lerp(shellColor, lungeEyeColor, 0.18f)
                    : shellColor;
        }

        private void EnsureVisual()
        {
            if (owner == null)
                owner = GetComponentInParent<CrabCrawlerController>();

            EnsureRuntimeResources();

            EnsurePart("RearUpperLeg", new Vector3(-0.42f, -0.2f, 0f), new Vector3(0.58f, 0.08f, 1f),
                22f, legColor, sortingOrder, solidSprite);
            EnsurePart("RearLowerLeg", new Vector3(-0.48f, -0.36f, 0f), new Vector3(0.58f, 0.08f, 1f),
                -20f, legColor, sortingOrder, solidSprite);
            EnsurePart("FrontUpperLeg", new Vector3(0.3f, -0.2f, 0f), new Vector3(0.58f, 0.08f, 1f),
                -22f, legColor, sortingOrder, solidSprite);
            EnsurePart("FrontLowerLeg", new Vector3(0.35f, -0.36f, 0f), new Vector3(0.58f, 0.08f, 1f),
                20f, legColor, sortingOrder, solidSprite);

            EnsurePart("Body", new Vector3(-0.08f, -0.02f, 0f), new Vector3(1.08f, 0.5f, 1f),
                0f, bodyColor, sortingOrder + 1, circleSprite);
            shellRenderer = EnsurePart("Shell", new Vector3(-0.16f, 0.1f, 0f), new Vector3(0.82f, 0.48f, 1f),
                0f, shellColor, sortingOrder + 2, circleSprite);

            EnsurePart("ClawArm", new Vector3(0.48f, 0.02f, 0f), new Vector3(0.48f, 0.1f, 1f),
                -8f, legColor, sortingOrder + 1, solidSprite);
            EnsurePart("Claw", new Vector3(0.72f, 0.02f, 0f), new Vector3(0.36f, 0.28f, 1f),
                0f, shellColor, sortingOrder + 2, circleSprite);

            rearEyeRenderer = EnsurePart("RearEye", new Vector3(0.06f, 0.31f, 0f), new Vector3(0.11f, 0.15f, 1f),
                0f, patrolEyeColor, sortingOrder + 3, circleSprite);
            frontEyeRenderer = EnsurePart("FrontEye", new Vector3(0.31f, 0.29f, 0f), new Vector3(0.13f, 0.17f, 1f),
                0f, patrolEyeColor, sortingOrder + 3, circleSprite);
            rearEyeScale = rearEyeRenderer.transform.localScale;
            frontEyeScale = frontEyeRenderer.transform.localScale;
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Crab Crawler Solid Texture Runtime",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                solidTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                solidTexture.Apply(false, true);
            }

            if (circleTexture == null)
                circleTexture = BuildCircleTexture(48);
            if (solidSprite == null)
                solidSprite = CreateSprite(solidTexture, "Crab Crawler Solid Sprite Runtime");
            if (circleSprite == null)
                circleSprite = CreateSprite(circleTexture, "Crab Crawler Circle Sprite Runtime");

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Crab Crawler Material Runtime",
                        hideFlags = HideFlags.DontSave
                    };
                    if (runtimeMaterial.HasProperty("_MainTex"))
                        runtimeMaterial.SetTexture("_MainTex", solidTexture);
                }
            }
        }

        private SpriteRenderer EnsurePart(
            string partName,
            Vector3 localPosition,
            Vector3 localScale,
            float localRotationZ,
            Color color,
            int order,
            Sprite sprite)
        {
            Transform part = transform.Find(partName);
            if (part == null)
            {
                GameObject partObject = new GameObject(partName);
                partObject.transform.SetParent(transform, false);
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

        private static Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            sprite.name = spriteName;
            return sprite;
        }

        private static Texture2D BuildCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Crab Crawler Circle Texture Runtime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
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

        private void OnDestroy()
        {
            if (solidSprite != null)
                Destroy(solidSprite);
            if (circleSprite != null)
                Destroy(circleSprite);
            if (solidTexture != null)
                Destroy(solidTexture);
            if (circleTexture != null)
                Destroy(circleTexture);
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }
    }
}
