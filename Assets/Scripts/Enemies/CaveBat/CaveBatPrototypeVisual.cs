using UnityEngine;

namespace Game.Enemies.CaveBat
{
    /// <summary>
    /// Runtime-only placeholder art. Disable or remove this component when final
    /// sprites and an Animator are assigned; gameplay collision stays on the root.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CaveBatPrototypeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CaveBatController owner;
        [SerializeField] private Transform visualRoot;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 8;

        [Header("Colors")]
        [SerializeField] private Color patrolColor = new Color(0.22f, 0.16f, 0.32f, 1f);
        [SerializeField] private Color wingColor = new Color(0.34f, 0.23f, 0.46f, 1f);
        [SerializeField] private Color alertColor = new Color(1f, 0.68f, 0.16f, 1f);
        [SerializeField] private Color attackColor = new Color(0.95f, 0.14f, 0.12f, 1f);
        [SerializeField] private Color earColor = new Color(0.68f, 0.42f, 0.76f, 1f);

        private Texture2D solidTexture;
        private Texture2D circleTexture;
        private Sprite solidSprite;
        private Sprite circleSprite;
        private Material runtimeMaterial;
        private SpriteRenderer bodyRenderer;
        private SpriteRenderer headRenderer;
        private SpriteRenderer leftWingRenderer;
        private SpriteRenderer rightWingRenderer;
        private SpriteRenderer leftEarRenderer;
        private SpriteRenderer rightEarRenderer;
        private Vector3 visualBaseScale = Vector3.one;
        private bool defaultsCached;

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
            if (owner == null || visualRoot == null || bodyRenderer == null)
                return;

            Color bodyColor = patrolColor;
            Color currentWingColor = wingColor;
            Color currentEarColor = earColor;
            float flapSpeed = 8f;
            float flapAmplitude = 22f;
            float bodyPulse = 1f;

            switch (owner.CurrentState)
            {
                case CaveBatState.Alert:
                case CaveBatState.Planning:
                    float alertPulse = 0.45f + Mathf.PingPong(Time.time * 4f, 0.55f);
                    bodyColor = Color.Lerp(patrolColor, alertColor, alertPulse);
                    currentEarColor = Color.Lerp(earColor, alertColor, alertPulse);
                    bodyPulse = 1f + Mathf.Sin(Time.time * 12f) * 0.05f;
                    break;
                case CaveBatState.Approach:
                    bodyColor = Color.Lerp(patrolColor, alertColor, 0.4f);
                    currentWingColor = Color.Lerp(wingColor, alertColor, 0.25f);
                    flapSpeed = 12f;
                    flapAmplitude = 30f;
                    break;
                case CaveBatState.UManeuver:
                    bool uIsOffensive = owner.IsOffensivePhase;
                    bodyColor = uIsOffensive
                        ? attackColor
                        : Color.Lerp(patrolColor, alertColor, 0.4f);
                    currentWingColor = uIsOffensive
                        ? Color.Lerp(wingColor, attackColor, 0.65f)
                        : Color.Lerp(wingColor, alertColor, 0.25f);
                    currentEarColor = uIsOffensive ? attackColor : earColor;
                    flapSpeed = uIsOffensive ? 15f : 12f;
                    flapAmplitude = uIsOffensive ? 12f : 30f;
                    bodyPulse = uIsOffensive ? 1.06f : 1f;
                    break;
                case CaveBatState.Dive:
                    bodyColor = attackColor;
                    currentWingColor = Color.Lerp(wingColor, attackColor, 0.65f);
                    currentEarColor = attackColor;
                    flapSpeed = 16f;
                    flapAmplitude = 10f;
                    bodyPulse = 1.08f;
                    break;
                case CaveBatState.Recover:
                    bodyColor = Color.Lerp(patrolColor, attackColor, 0.3f);
                    flapSpeed = 14f;
                    flapAmplitude = 28f;
                    break;
                case CaveBatState.Return:
                    bodyColor = Color.Lerp(patrolColor, alertColor, 0.15f);
                    flapSpeed = 10f;
                    flapAmplitude = 24f;
                    break;
            }

            Vector2 movement = owner.MovementDirection;
            float facing = movement.x < -0.02f ? -1f : 1f;
            visualRoot.localScale = new Vector3(
                Mathf.Abs(visualBaseScale.x) * facing,
                visualBaseScale.y,
                visualBaseScale.z) * bodyPulse;
            visualRoot.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Clamp(movement.y * 16f, -18f, 18f));

            float flap = Mathf.Sin(Time.time * flapSpeed) * flapAmplitude;
            if (leftWingRenderer != null)
            {
                leftWingRenderer.color = currentWingColor;
                leftWingRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, 18f + flap);
            }

            if (rightWingRenderer != null)
            {
                rightWingRenderer.color = currentWingColor;
                rightWingRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, -18f - flap);
            }

            bodyRenderer.color = bodyColor;
            if (headRenderer != null)
                headRenderer.color = bodyColor;
            if (leftEarRenderer != null)
                leftEarRenderer.color = currentEarColor;
            if (rightEarRenderer != null)
                rightEarRenderer.color = currentEarColor;
        }

        private void EnsureVisual()
        {
            ResolveReferences();
            if (visualRoot == null)
                return;

            EnsureRuntimeResources();
            if (!defaultsCached)
            {
                visualBaseScale = visualRoot.localScale;
                defaultsCached = true;
            }

            leftWingRenderer = EnsurePart(
                visualRoot,
                "LeftWing",
                new Vector3(-0.48f, 0.03f, 0f),
                new Vector3(0.72f, 0.3f, 1f),
                18f,
                wingColor,
                sortingOrder,
                circleSprite);
            rightWingRenderer = EnsurePart(
                visualRoot,
                "RightWing",
                new Vector3(0.48f, 0.03f, 0f),
                new Vector3(0.72f, 0.3f, 1f),
                -18f,
                wingColor,
                sortingOrder,
                circleSprite);
            bodyRenderer = EnsurePart(
                visualRoot,
                "Body",
                new Vector3(0f, 0f, 0f),
                new Vector3(0.72f, 0.48f, 1f),
                0f,
                patrolColor,
                sortingOrder + 1,
                circleSprite);
            headRenderer = EnsurePart(
                visualRoot,
                "Head",
                new Vector3(0f, -0.23f, 0f),
                new Vector3(0.5f, 0.4f, 1f),
                0f,
                patrolColor,
                sortingOrder + 2,
                circleSprite);
            leftEarRenderer = EnsurePart(
                visualRoot,
                "LeftEar",
                new Vector3(-0.18f, -0.43f, 0f),
                new Vector3(0.14f, 0.34f, 1f),
                -18f,
                earColor,
                sortingOrder + 3,
                solidSprite);
            rightEarRenderer = EnsurePart(
                visualRoot,
                "RightEar",
                new Vector3(0.18f, -0.43f, 0f),
                new Vector3(0.14f, 0.34f, 1f),
                18f,
                earColor,
                sortingOrder + 3,
                solidSprite);
        }

        private void ResolveReferences()
        {
            if (owner == null)
                owner = GetComponent<CaveBatController>();
            if (visualRoot == null)
                visualRoot = transform.Find("Visual");
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Cave Bat Solid Texture Runtime",
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
                solidSprite = CreateSprite(solidTexture, "Cave Bat Solid Sprite Runtime");
            if (circleSprite == null)
                circleSprite = CreateSprite(circleTexture, "Cave Bat Circle Sprite Runtime");

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Cave Bat Material Runtime",
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
                name = "Cave Bat Circle Texture Runtime",
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
