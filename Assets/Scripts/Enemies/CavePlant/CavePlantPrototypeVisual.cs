using UnityEngine;

namespace Game.Enemies.CavePlant
{
    /// <summary>
    /// Runtime placeholder art. Disable this component when final sprites and an
    /// Animator are assigned; gameplay collision and the muzzle remain independent.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CavePlantPrototypeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CavePlantController owner;
        [SerializeField] private Transform visualRoot;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 8;

        [Header("Colors")]
        [SerializeField] private Color stemColor = new Color(0.2f, 0.52f, 0.12f, 1f);
        [SerializeField] private Color leafColor = new Color(0.28f, 0.7f, 0.16f, 1f);
        [SerializeField] private Color bulbColor = new Color(0.5f, 0.18f, 0.55f, 1f);
        [SerializeField] private Color telegraphColor = new Color(1f, 0.62f, 0.08f, 1f);
        [SerializeField] private Color fireColor = new Color(0.75f, 1f, 0.18f, 1f);
        [SerializeField] private Color mouthColor = new Color(0.09f, 0.035f, 0.08f, 1f);

        [Header("Feedback")]
        [SerializeField, Min(0.02f)] private float fireFlashDuration = 0.14f;
        [SerializeField, Min(0f)] private float telegraphShake = 2.5f;

        private Texture2D solidTexture;
        private Texture2D circleTexture;
        private Sprite solidSprite;
        private Sprite circleSprite;
        private Material runtimeMaterial;
        private Transform headRoot;
        private SpriteRenderer bulbRenderer;
        private SpriteRenderer mouthRenderer;
        private Vector3 headBaseScale = Vector3.one;
        private float fireFlashTimer;

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
            ResolveReferences();
            if (owner == null || headRoot == null || bulbRenderer == null)
                return;

            Vector2 aim = owner.AimDirection.sqrMagnitude > 0.0001f
                ? owner.AimDirection.normalized
                : Vector2.right;
            float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            float scale = 1f;
            Color color = bulbColor;

            if (owner.CurrentState == CavePlantState.Telegraph)
            {
                float progress = owner.TelegraphProgress;
                float pulse = 0.5f + Mathf.PingPong(Time.time * 6f, 0.5f);
                color = Color.Lerp(bulbColor, telegraphColor, 0.3f + progress * 0.7f);
                angle += Mathf.Sin(Time.time * 24f) * telegraphShake * progress;
                scale = 1f + pulse * 0.08f + progress * 0.12f;
            }
            else if (owner.CurrentState == CavePlantState.Fire || fireFlashTimer > 0f)
            {
                fireFlashTimer = Mathf.Max(0f, fireFlashTimer - Time.deltaTime);
                color = fireColor;
                scale = 1.18f;
            }

            headRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
            headRoot.localScale = headBaseScale * scale;
            bulbRenderer.color = color;
            if (mouthRenderer != null)
                mouthRenderer.color = owner.CurrentState == CavePlantState.Telegraph
                    ? Color.Lerp(mouthColor, Color.white, owner.TelegraphProgress * 0.35f)
                    : mouthColor;
        }

        public void NotifyStateChanged(CavePlantState state)
        {
            EnsureVisual();
            if (state == CavePlantState.Fire)
                fireFlashTimer = Mathf.Max(0.02f, fireFlashDuration);
        }

        private void EnsureVisual()
        {
            ResolveReferences();
            if (visualRoot == null)
                return;

            EnsureRuntimeResources();

            EnsurePart(
                visualRoot,
                "Stem",
                new Vector3(0f, 0.55f, 0f),
                new Vector3(0.22f, 1.1f, 1f),
                0f,
                stemColor,
                sortingOrder,
                solidSprite);
            EnsurePart(
                visualRoot,
                "LeftLeaf",
                new Vector3(-0.36f, 0.42f, 0f),
                new Vector3(0.62f, 0.22f, 1f),
                28f,
                leafColor,
                sortingOrder + 1,
                circleSprite);
            EnsurePart(
                visualRoot,
                "RightLeaf",
                new Vector3(0.36f, 0.25f, 0f),
                new Vector3(0.62f, 0.22f, 1f),
                -24f,
                leafColor,
                sortingOrder + 1,
                circleSprite);

            headRoot = visualRoot.Find("HeadPivot");
            if (headRoot == null)
            {
                GameObject headObject = new GameObject("HeadPivot");
                headObject.layer = gameObject.layer;
                headObject.transform.SetParent(visualRoot, false);
                headRoot = headObject.transform;
            }
            headRoot.localPosition = new Vector3(0f, 1.18f, 0f);

            bulbRenderer = EnsurePart(
                headRoot,
                "Bulb",
                new Vector3(0.16f, 0f, 0f),
                new Vector3(0.78f, 0.58f, 1f),
                0f,
                bulbColor,
                sortingOrder + 2,
                circleSprite);
            mouthRenderer = EnsurePart(
                headRoot,
                "Mouth",
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.24f, 0.18f, 1f),
                0f,
                mouthColor,
                sortingOrder + 3,
                circleSprite);
            EnsurePart(
                headRoot,
                "UpperPetal",
                new Vector3(0.1f, 0.32f, 0f),
                new Vector3(0.48f, 0.2f, 1f),
                18f,
                leafColor,
                sortingOrder + 1,
                circleSprite);
            EnsurePart(
                headRoot,
                "LowerPetal",
                new Vector3(0.1f, -0.32f, 0f),
                new Vector3(0.48f, 0.2f, 1f),
                -18f,
                leafColor,
                sortingOrder + 1,
                circleSprite);

            headBaseScale = Vector3.one;
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
                partObject.layer = gameObject.layer;
                partObject.transform.SetParent(parent, false);
                part = partObject.transform;
            }

            part.localPosition = localPosition;
            part.localRotation = Quaternion.Euler(0f, 0f, localRotationZ);
            part.localScale = localScale;

            SpriteRenderer renderer = part.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = part.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = runtimeMaterial;
            renderer.color = color;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = order;
            renderer.enabled = true;
            return renderer;
        }

        private void ResolveReferences()
        {
            if (owner == null)
                owner = GetComponent<CavePlantController>();
            if (visualRoot == null)
                visualRoot = transform.Find("Visual");
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Cave Plant Solid Texture Runtime",
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
                solidSprite = CreateSprite(solidTexture, "Cave Plant Solid Sprite Runtime", 2f);
            if (circleSprite == null)
                circleSprite = CreateSprite(circleTexture, "Cave Plant Circle Sprite Runtime", 48f);
            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Cave Plant Material Runtime",
                        hideFlags = HideFlags.DontSave
                    };
                }
            }
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

        private static Texture2D BuildCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Cave Plant Circle Texture Runtime",
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

        private void OnDisable()
        {
            fireFlashTimer = 0f;
            if (headRoot != null)
                headRoot.localScale = headBaseScale;
        }

        private void OnValidate()
        {
            fireFlashDuration = Mathf.Max(0.02f, fireFlashDuration);
            telegraphShake = Mathf.Max(0f, telegraphShake);
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
