using Game.Player;
using Game.Audio;
using UnityEngine;

namespace Game.Enemies.AerialSentry
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class AerialSentryProjectile : MonoBehaviour
    {
        private const int CastCapacity = 12;
        private const string WorldImpactEventId = "enemy.aerial.projectile.impact.world";

        [Header("Collision")]
        [SerializeField] private LayerMask playerMask = 512;
        [SerializeField] private LayerMask worldMask = 10312;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;

        [Header("Fallback Defaults")]
        [SerializeField, Min(0.1f)] private float speed = 7f;
        [SerializeField, Tooltip("Fixed to one health unit for this projectile.")] private int damage = 1;
        [SerializeField, Min(0f)] private float stunDuration = 0.35f;
        [SerializeField, Min(0.1f)] private float lifetime = 4f;

        [Header("Visual")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int trailSortingOrder = 29;
        [SerializeField] private int coreSortingOrder = 30;
        [SerializeField] private Color glowColor = new Color(0.15f, 0.85f, 1f, 0.55f);
        [SerializeField] private Color coreColor = new Color(0.8f, 1f, 1f, 1f);
        [SerializeField, Min(0.05f)] private float projectileVisualSize = 0.25f;
        [SerializeField, Min(0.05f)] private float trailLength = 0.55f;
        [SerializeField, Min(0.01f)] private float trailWidth = 0.1f;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[CastCapacity];
        private Rigidbody2D body;
        private Vector2 direction = Vector2.down;
        private Collider2D ownerCollider;
        private bool consumed;
        private bool debugLogs;
        private float remainingLifetime;
        private float age;
        private LineRenderer glowLine;
        private LineRenderer coreLine;
        private SpriteRenderer coreRenderer;
        private Material glowMaterial;
        private Material coreMaterial;
        private Texture2D coreTexture;
        private Sprite coreSprite;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;

            CircleCollider2D projectileCollider = GetComponent<CircleCollider2D>();
            projectileCollider.isTrigger = true;
            projectileCollider.radius = collisionRadius;

            remainingLifetime = lifetime;
            BuildVisual();
        }

        public void Initialize(
            Vector2 initialDirection,
            float initialSpeed,
            int initialDamage,
            float initialStunDuration,
            float initialLifetime,
            LayerMask initialWorldCollisionMask,
            Collider2D initialOwnerCollider,
            bool enableDebugLogs)
        {
            direction = initialDirection.sqrMagnitude > 0.0001f
                ? initialDirection.normalized
                : Vector2.down;
            speed = Mathf.Max(0.1f, initialSpeed);
            damage = Mathf.Max(1, initialDamage);
            stunDuration = Mathf.Max(0f, initialStunDuration);
            lifetime = Mathf.Max(0.1f, initialLifetime);
            remainingLifetime = lifetime;
            worldMask = initialWorldCollisionMask;
            ownerCollider = initialOwnerCollider;
            debugLogs = enableDebugLogs;
            age = 0f;
        }

        private void FixedUpdate()
        {
            if (consumed)
                return;

            age += Time.fixedDeltaTime;
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                Consume("LifetimeExpired", null);
                return;
            }

            float distance = speed * Time.fixedDeltaTime;
            int combinedMask = playerMask.value | worldMask.value;
            int hitCount = Physics2D.CircleCastNonAlloc(
                body.position,
                collisionRadius,
                direction,
                castHits,
                distance,
                combinedMask);

            RaycastHit2D nearestHit = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = castHits[i];
                if (!IsRelevantCollider(candidate.collider) || candidate.distance >= nearestDistance)
                    continue;

                nearestDistance = candidate.distance;
                nearestHit = candidate;
            }

            if (nearestHit.collider != null)
            {
                body.position = nearestHit.point;
                HandleImpact(nearestHit.collider);
                return;
            }

            body.MovePosition(body.position + direction * distance);
        }

        private void LateUpdate()
        {
            Vector3 head = body != null ? (Vector3)body.position : transform.position;
            Vector3 tail = head - (Vector3)(direction * trailLength);
            SetLinePositions(glowLine, tail, head);
            SetLinePositions(coreLine, tail, head);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!consumed && IsRelevantCollider(other))
                HandleImpact(other);
        }

        private bool IsRelevantCollider(Collider2D candidate)
        {
            if (candidate == null)
                return false;
            if (candidate.transform == transform || candidate.transform.IsChildOf(transform))
                return false;
            if (ownerCollider != null &&
                (candidate == ownerCollider ||
                 candidate.transform == ownerCollider.transform ||
                 candidate.transform.IsChildOf(ownerCollider.transform)))
                return false;

            int candidateLayer = 1 << candidate.gameObject.layer;
            if ((playerMask.value & candidateLayer) != 0)
                return true;

            return !candidate.isTrigger && (worldMask.value & candidateLayer) != 0;
        }

        private void HandleImpact(Collider2D other)
        {
            if (consumed)
                return;

            int otherLayer = 1 << other.gameObject.layer;
            if ((playerMask.value & otherLayer) != 0)
            {
                PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
                if (playerHealth != null)
                {
                    bool applied = playerHealth.TryTakeDamage(damage, body.position, stunDuration);
                    LogPlayerHit(applied);
                }

                Consume("PlayerHit", other);
                return;
            }

            if (!other.isTrigger && (worldMask.value & otherLayer) != 0)
                Consume("WorldHit", other);
        }

        private void Consume(string reason, Collider2D hit)
        {
            if (consumed)
                return;

            consumed = true;
            if (reason == "WorldHit")
                AudioService.PlayAtAcoustic(WorldImpactEventId, body.position);
            LogDespawn(reason, hit);
            Destroy(gameObject);
        }

        private void BuildVisual()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
                return;

            glowMaterial = CreateRuntimeMaterial(shader, "Aerial Sentry Projectile Glow Runtime");
            coreMaterial = CreateRuntimeMaterial(shader, "Aerial Sentry Projectile Core Runtime");

            coreTexture = BuildCircleTexture(48);
            coreSprite = Sprite.Create(
                coreTexture,
                new Rect(0f, 0f, coreTexture.width, coreTexture.height),
                new Vector2(0.5f, 0.5f),
                coreTexture.width);
            coreSprite.name = "Aerial Sentry Projectile Core Runtime";

            GameObject coreObject = new GameObject("CoreVisual");
            coreObject.layer = gameObject.layer;
            coreObject.transform.SetParent(transform, false);
            coreObject.transform.localScale = Vector3.one * projectileVisualSize;
            coreRenderer = coreObject.AddComponent<SpriteRenderer>();
            coreRenderer.enabled = true;
            coreRenderer.sprite = coreSprite;
            coreRenderer.sharedMaterial = coreMaterial;
            coreRenderer.color = coreColor;
            coreRenderer.sortingLayerName = sortingLayerName;
            coreRenderer.sortingOrder = coreSortingOrder;

            glowLine = CreateLine(
                "TrailGlow",
                glowMaterial,
                glowColor,
                trailWidth * 2.2f,
                trailSortingOrder - 1);
            coreLine = CreateLine(
                "TrailCore",
                coreMaterial,
                coreColor,
                trailWidth,
                trailSortingOrder);
        }

        private static Material CreateRuntimeMaterial(Shader shader, string materialName)
        {
            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
            return material;
        }

        private LineRenderer CreateLine(
            string lineName,
            Material material,
            Color color,
            float width,
            int sortingOrder)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.enabled = true;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.sharedMaterial = material;
            line.startColor = new Color(color.r, color.g, color.b, 0f);
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = sortingOrder;
            return line;
        }

        private static void SetLinePositions(LineRenderer line, Vector3 tail, Vector3 head)
        {
            if (line == null)
                return;

            line.SetPosition(0, tail);
            line.SetPosition(1, head);
        }

        private static Texture2D BuildCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Aerial Sentry Projectile Core Texture Runtime",
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

        private void OnValidate()
        {
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            speed = Mathf.Max(0.1f, speed);
            damage = 1;
            stunDuration = Mathf.Max(0f, stunDuration);
            lifetime = Mathf.Max(0.1f, lifetime);
            trailLength = Mathf.Max(0.05f, trailLength);
            projectileVisualSize = Mathf.Max(0.05f, projectileVisualSize);
            trailWidth = Mathf.Max(0.01f, trailWidth);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlayerHit(bool applied)
        {
            if (debugLogs)
                Debug.Log($"[SENTRY][HIT PLAYER] applied={applied}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDespawn(string reason, Collider2D hit)
        {
            if (!debugLogs)
                return;

            string hitName = hit != null ? hit.name : "None";
            Debug.Log(
                $"[SENTRY][PROJECTILE DESPAWN] reason={reason} hit={hitName} lifetime={age:0.000}",
                this);
        }

        private void OnDestroy()
        {
            if (coreSprite != null)
                Destroy(coreSprite);
            if (coreTexture != null)
                Destroy(coreTexture);
            if (glowMaterial != null)
                Destroy(glowMaterial);
            if (coreMaterial != null)
                Destroy(coreMaterial);
        }
    }
}
