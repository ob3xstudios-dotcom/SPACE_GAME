using System.Collections;
using UnityEngine;

namespace Game.Enemies.CaveWorm
{
    /// <summary>
    /// Runtime-only placeholder art. Remove or disable this component when final
    /// sprites and an Animator are assigned; gameplay collision remains separate.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CaveWormPrototypeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CaveWormController owner;
        [SerializeField] private Transform wormVisualRoot;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 8;

        [Header("Colors")]
        [SerializeField] private Color holeColor = new Color(0.08f, 0.045f, 0.035f, 1f);
        [SerializeField] private Color bodyColor = new Color(0.48f, 0.19f, 0.12f, 1f);
        [SerializeField] private Color segmentColor = new Color(0.69f, 0.3f, 0.15f, 1f);
        [SerializeField] private Color alertColor = new Color(1f, 0.65f, 0.08f, 1f);
        [SerializeField] private Color attackColor = new Color(1f, 0.12f, 0.05f, 1f);

        [Header("Confirmed Impact")]
        [Tooltip("Optional overlay sprite for the worm. Uses its current head sprite when empty.")]
        [SerializeField] private Sprite wormImpactSprite;
        [Tooltip("Optional overlay sprite for the Player. Uses the current Player sprite when empty.")]
        [SerializeField] private Sprite playerImpactSprite;
        [SerializeField, Min(0.02f)] private float impactVisualDuration = 0.16f;
        [SerializeField] private Color wormImpactTint = new Color(1f, 0.25f, 0.08f, 0.9f);
        [SerializeField] private Color playerImpactTint = new Color(1f, 0.82f, 0.3f, 0.88f);

        private Texture2D solidTexture;
        private Texture2D circleTexture;
        private Sprite solidSprite;
        private Sprite circleSprite;
        private Material runtimeMaterial;
        private SpriteRenderer holeRenderer;
        private SpriteRenderer headRenderer;
        private SpriteRenderer leftEyeRenderer;
        private SpriteRenderer rightEyeRenderer;
        private Vector3 headBaseScale;
        private Vector3 leftEyeBaseScale;
        private Vector3 rightEyeBaseScale;
        private SpriteRenderer wormImpactOverlay;
        private SpriteRenderer playerImpactOverlay;
        private Coroutine impactFeedbackRoutine;

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
            if (owner == null || holeRenderer == null || headRenderer == null)
                return;

            ApplyHoleOrientation();

            Color eyeColor = Color.white;
            float headScale = 1f;
            float eyeScale = 1f;
            Color currentHoleColor = holeColor;

            switch (owner.CurrentState)
            {
                case CaveWormState.Alert:
                    float alertPulse = 0.65f + Mathf.PingPong(Time.time * 4f, 0.35f);
                    currentHoleColor = Color.Lerp(holeColor, alertColor, alertPulse);
                    eyeColor = alertColor;
                    break;
                case CaveWormState.Emerging:
                    currentHoleColor = Color.Lerp(holeColor, attackColor, 0.55f);
                    eyeColor = attackColor;
                    headScale = 1.12f;
                    break;
                case CaveWormState.Attack:
                    float telegraph = owner.AttackTelegraphProgress;
                    currentHoleColor = Color.Lerp(alertColor, attackColor, telegraph);
                    eyeColor = Color.Lerp(alertColor, attackColor, telegraph);
                    headScale = 1f + telegraph * 0.18f;
                    eyeScale = 1f + telegraph * 0.3f;
                    break;
                case CaveWormState.Retreating:
                    eyeColor = segmentColor;
                    break;
            }

            holeRenderer.color = currentHoleColor;
            headRenderer.color = owner.IsAttackActive
                ? Color.Lerp(segmentColor, attackColor, 0.3f)
                : segmentColor;
            headRenderer.transform.localScale = headBaseScale * headScale;

            if (leftEyeRenderer != null)
            {
                leftEyeRenderer.color = eyeColor;
                leftEyeRenderer.transform.localScale = leftEyeBaseScale * eyeScale;
            }

            if (rightEyeRenderer != null)
            {
                rightEyeRenderer.color = eyeColor;
                rightEyeRenderer.transform.localScale = rightEyeBaseScale * eyeScale;
            }
        }

        public void PlayConfirmedImpact(Transform playerTarget)
        {
            EnsureVisual();
            ClearConfirmedImpact();

            ConfigureImpactOverlay(
                ref wormImpactOverlay,
                headRenderer,
                "CaveWormImpactOverlay",
                wormImpactSprite,
                wormImpactTint);

            SpriteRenderer playerRenderer = FindPrimaryRenderer(playerTarget);
            ConfigureImpactOverlay(
                ref playerImpactOverlay,
                playerRenderer,
                "CaveWormPlayerImpactOverlay",
                playerImpactSprite,
                playerImpactTint);

            if ((wormImpactOverlay != null && wormImpactOverlay.enabled) ||
                (playerImpactOverlay != null && playerImpactOverlay.enabled))
            {
                impactFeedbackRoutine = StartCoroutine(ConfirmedImpactRoutine());
            }
        }

        public void ClearConfirmedImpact()
        {
            if (impactFeedbackRoutine != null)
            {
                StopCoroutine(impactFeedbackRoutine);
                impactFeedbackRoutine = null;
            }

            SetImpactOverlayEnabled(wormImpactOverlay, false);
            SetImpactOverlayEnabled(playerImpactOverlay, false);
        }

        private IEnumerator ConfirmedImpactRoutine()
        {
            float duration = Mathf.Max(0.02f, impactVisualDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float pulse = 1f + Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI) * 0.14f;
                SetImpactOverlayScale(wormImpactOverlay, pulse);
                SetImpactOverlayScale(playerImpactOverlay, pulse);
                yield return null;
            }

            SetImpactOverlayEnabled(wormImpactOverlay, false);
            SetImpactOverlayEnabled(playerImpactOverlay, false);
            impactFeedbackRoutine = null;
        }

        private static void ConfigureImpactOverlay(
            ref SpriteRenderer overlay,
            SpriteRenderer source,
            string overlayName,
            Sprite overrideSprite,
            Color tint)
        {
            if (source == null)
            {
                SetImpactOverlayEnabled(overlay, false);
                return;
            }

            if (overlay == null)
            {
                GameObject overlayObject = new GameObject(overlayName);
                overlay = overlayObject.AddComponent<SpriteRenderer>();
            }

            Transform overlayTransform = overlay.transform;
            overlayTransform.SetParent(source.transform, false);
            overlayTransform.localPosition = Vector3.zero;
            overlayTransform.localRotation = Quaternion.identity;
            overlayTransform.localScale = Vector3.one;
            overlay.gameObject.layer = source.gameObject.layer;

            overlay.sprite = overrideSprite != null ? overrideSprite : source.sprite;
            overlay.sharedMaterial = source.sharedMaterial;
            overlay.color = tint;
            overlay.flipX = source.flipX;
            overlay.flipY = source.flipY;
            overlay.sortingLayerID = source.sortingLayerID;
            overlay.sortingOrder = source.sortingOrder + 20;
            overlay.enabled = overlay.sprite != null;
        }

        private static SpriteRenderer FindPrimaryRenderer(Transform root)
        {
            if (root == null)
                return null;

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer candidate = renderers[i];
                if (candidate == null ||
                    candidate.sprite == null ||
                    candidate.name == "CaveWormPlayerImpactOverlay")
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static void SetImpactOverlayEnabled(SpriteRenderer overlay, bool enabled)
        {
            if (overlay == null)
                return;

            overlay.transform.localScale = Vector3.one;
            overlay.enabled = enabled;
        }

        private static void SetImpactOverlayScale(SpriteRenderer overlay, float scale)
        {
            if (overlay != null)
                overlay.transform.localScale = Vector3.one * scale;
        }

        private void EnsureVisual()
        {
            ResolveReferences();
            if (wormVisualRoot == null)
                return;

            EnsureRuntimeResources();

            holeRenderer = EnsurePart(
                transform,
                "PrototypeHole",
                new Vector3(0f, -0.02f, 0f),
                new Vector3(1.55f, 0.32f, 1f),
                0f,
                holeColor,
                sortingOrder,
                circleSprite);

            EnsurePart(
                wormVisualRoot,
                "LowerBody",
                new Vector3(0f, 0.22f, 0f),
                new Vector3(0.5f, 0.78f, 1f),
                0f,
                bodyColor,
                sortingOrder + 1,
                circleSprite);
            EnsurePart(
                wormVisualRoot,
                "UpperBody",
                new Vector3(0f, 0.69f, 0f),
                new Vector3(0.64f, 0.72f, 1f),
                0f,
                segmentColor,
                sortingOrder + 2,
                circleSprite);
            headRenderer = EnsurePart(
                wormVisualRoot,
                "Head",
                new Vector3(0.04f, 1.05f, 0f),
                new Vector3(0.78f, 0.58f, 1f),
                -4f,
                segmentColor,
                sortingOrder + 3,
                circleSprite);
            EnsurePart(
                wormVisualRoot,
                "Mouth",
                new Vector3(0.25f, 0.95f, 0f),
                new Vector3(0.28f, 0.07f, 1f),
                -8f,
                holeColor,
                sortingOrder + 4,
                solidSprite);
            leftEyeRenderer = EnsurePart(
                wormVisualRoot,
                "LeftEye",
                new Vector3(-0.11f, 1.18f, 0f),
                new Vector3(0.09f, 0.12f, 1f),
                0f,
                Color.white,
                sortingOrder + 4,
                circleSprite);
            rightEyeRenderer = EnsurePart(
                wormVisualRoot,
                "RightEye",
                new Vector3(0.16f, 1.17f, 0f),
                new Vector3(0.09f, 0.12f, 1f),
                0f,
                Color.white,
                sortingOrder + 4,
                circleSprite);

            headBaseScale = headRenderer.transform.localScale;
            leftEyeBaseScale = leftEyeRenderer.transform.localScale;
            rightEyeBaseScale = rightEyeRenderer.transform.localScale;
            ApplyHoleOrientation();
        }

        private void ApplyHoleOrientation()
        {
            if (owner == null || holeRenderer == null)
                return;

            Vector3 inwardOffset = -(Vector3)owner.LocalAttackVector * 0.02f;
            holeRenderer.transform.localPosition = inwardOffset;
            holeRenderer.transform.localRotation =
                Quaternion.Euler(0f, 0f, owner.AttackRotationDegrees);
        }

        private void ResolveReferences()
        {
            if (owner == null)
                owner = GetComponent<CaveWormController>();
            if (wormVisualRoot == null)
                wormVisualRoot = transform.Find("Visual");
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Cave Worm Solid Texture Runtime",
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
                solidSprite = CreateSprite(solidTexture, "Cave Worm Solid Sprite Runtime");
            if (circleSprite == null)
                circleSprite = CreateSprite(circleTexture, "Cave Worm Circle Sprite Runtime");

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Cave Worm Material Runtime",
                        hideFlags = HideFlags.DontSave
                    };
                }
            }
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
                name = "Cave Worm Circle Texture Runtime",
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
            ClearConfirmedImpact();
        }

        private void OnValidate()
        {
            impactVisualDuration = Mathf.Max(0.02f, impactVisualDuration);
        }

        private void OnDestroy()
        {
            ClearConfirmedImpact();
            if (wormImpactOverlay != null)
                Destroy(wormImpactOverlay.gameObject);
            if (playerImpactOverlay != null)
                Destroy(playerImpactOverlay.gameObject);
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
