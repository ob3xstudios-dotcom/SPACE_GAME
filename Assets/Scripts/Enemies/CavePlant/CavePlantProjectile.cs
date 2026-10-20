using Game.Combat;
using Game.Player;
using UnityEngine;

namespace Game.Enemies.CavePlant
{
    /// <summary>
    /// Swept, non-homing CavePlant projectile. PlayerParry validates a successful
    /// defense before this component resolves the currently supported Destroy action.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    [DisallowMultipleComponent]
    public sealed class CavePlantProjectile : MonoBehaviour, IParryableProjectile
    {
        private const int CastCapacity = 16;

        [Header("Collision")]
        [SerializeField] private LayerMask playerMask = 512;
        [SerializeField] private LayerMask worldMask = 10312;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.14f;

        [Header("Fallback Defaults")]
        [SerializeField, Min(0.1f)] private float speed = 7f;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0.1f)] private float lifetime = 4f;

        [Header("Prototype Visual")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder = 30;
        [SerializeField] private Color projectileColor = new Color(0.68f, 1f, 0.16f, 1f);
        [SerializeField] private Color parryFlashColor = new Color(0.25f, 1f, 1f, 0.95f);
        [SerializeField, Min(0.05f)] private float visualSize = 0.3f;
        [SerializeField, Min(0.02f)] private float parryFlashDuration = 0.18f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[CastCapacity];
        private Rigidbody2D body;
        private CircleCollider2D projectileCollider;
        private Transform ownerRoot;
        private Vector2 direction = Vector2.right;
        private float remainingLifetime;
        private bool consumed;
        private Texture2D runtimeTexture;
        private Sprite runtimeSprite;
        private Material runtimeMaterial;
        private Transform visualRoot;

        public float CollisionRadius => collisionRadius;
        public bool IsParryable => !consumed && isActiveAndEnabled;
        public Collider2D ParryCollider => projectileCollider;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;

            projectileCollider = GetComponent<CircleCollider2D>();
            projectileCollider.isTrigger = true;
            projectileCollider.radius = collisionRadius;

            remainingLifetime = lifetime;
            EnsurePrototypeVisual();
        }

        public void Initialize(
            Vector2 initialDirection,
            float initialSpeed,
            int initialDamage,
            float initialLifetime,
            LayerMask initialWorldMask,
            LayerMask initialPlayerMask,
            Transform initialOwnerRoot,
            bool enableDebugLogs)
        {
            direction = initialDirection.sqrMagnitude > 0.0001f
                ? initialDirection.normalized
                : Vector2.right;
            speed = Mathf.Max(0.1f, initialSpeed);
            damage = Mathf.Max(1, initialDamage);
            lifetime = Mathf.Max(0.1f, initialLifetime);
            remainingLifetime = lifetime;
            worldMask = initialWorldMask;
            playerMask = initialPlayerMask;
            ownerRoot = initialOwnerRoot;
            debugLogs = enableDebugLogs;
            consumed = false;

            if (projectileCollider != null)
            {
                projectileCollider.radius = collisionRadius;
                projectileCollider.enabled = true;
            }
            if (body != null)
                body.simulated = true;
        }

        private void FixedUpdate()
        {
            if (consumed)
                return;

            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                Consume("LifetimeExpired", null);
                return;
            }

            float distance = speed * Time.fixedDeltaTime;
            Vector2 start = body.position;
            int combinedMask = playerMask.value | worldMask.value;
            int hitCount = Physics2D.CircleCastNonAlloc(
                start,
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
                body.position = start + direction * Mathf.Max(0f, nearestDistance);
                HandleImpact(nearestHit.collider);
                return;
            }

            body.MovePosition(start + direction * distance);
        }

        private void LateUpdate()
        {
            if (visualRoot == null || consumed)
                return;

            float pulse = 1f + Mathf.Sin(Time.time * 18f) * 0.08f;
            visualRoot.localScale = Vector3.one * (visualSize * pulse);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
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
            if (candidate == projectileCollider ||
                candidate.transform == transform ||
                candidate.transform.IsChildOf(transform))
            {
                return false;
            }
            if (ownerRoot != null &&
                (candidate.transform == ownerRoot || candidate.transform.IsChildOf(ownerRoot)))
            {
                return false;
            }

            int candidateLayer = 1 << candidate.gameObject.layer;
            if ((playerMask.value & candidateLayer) != 0)
                return true;

            return !candidate.isTrigger && (worldMask.value & candidateLayer) != 0;
        }

        private void HandleImpact(Collider2D other)
        {
            if (consumed || other == null)
                return;

            int otherLayer = 1 << other.gameObject.layer;
            if ((playerMask.value & otherLayer) != 0)
            {
                PlayerParry parry = other.GetComponentInParent<PlayerParry>();
                if (parry != null && parry.TryParryProjectile(this))
                    return;

                if (consumed)
                    return;

                PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
                bool applied = playerHealth != null &&
                               playerHealth.TryTakeDamage(damage, Position, 0f);
                LogPlayerHit(applied);
                Consume("PlayerHit", other);
                return;
            }

            if (!other.isTrigger && (worldMask.value & otherLayer) != 0)
                Consume("WorldHit", other);
        }

        public ProjectileParryResolution ResolveParry(
            ProjectileParryAction action,
            ProjectileParryContext context)
        {
            if (consumed || action != ProjectileParryAction.Destroy)
                return ProjectileParryResolution.None;

            Vector2 feedbackPosition = Position;
            Consume("Parried", null, false);
            SpawnParryFlash(feedbackPosition, context.ParryDirection);

            if (debugLogs)
                Debug.Log($"[CAVE PLANT PROJECTILE] Parried at={feedbackPosition}", this);

            return ProjectileParryResolution.Destroyed;
        }

        private void Consume(string reason, Collider2D hit, bool log = true)
        {
            if (consumed)
                return;

            consumed = true;
            if (projectileCollider != null)
                projectileCollider.enabled = false;
            if (body != null)
                body.simulated = false;

            if (log && debugLogs)
            {
                string hitName = hit != null ? hit.name : "None";
                Debug.Log(
                    $"[CAVE PLANT PROJECTILE] Consumed reason={reason} hit={hitName}",
                    this);
            }

            Destroy(gameObject);
        }

        private void EnsurePrototypeVisual()
        {
            if (visualRoot == null)
            {
                Transform existing = transform.Find("PrototypeVisual");
                if (existing != null)
                {
                    visualRoot = existing;
                }
                else
                {
                    GameObject visualObject = new GameObject("PrototypeVisual");
                    visualObject.layer = gameObject.layer;
                    visualObject.transform.SetParent(transform, false);
                    visualRoot = visualObject.transform;
                }
            }

            EnsureRuntimeResources();
            SpriteRenderer renderer = visualRoot.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = visualRoot.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = runtimeSprite;
            renderer.sharedMaterial = runtimeMaterial;
            renderer.color = projectileColor;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            visualRoot.localScale = Vector3.one * visualSize;
        }

        private void EnsureRuntimeResources()
        {
            if (runtimeTexture == null)
                runtimeTexture = BuildCircleTexture(40, "Cave Plant Projectile Texture Runtime");
            if (runtimeSprite == null)
            {
                runtimeSprite = Sprite.Create(
                    runtimeTexture,
                    new Rect(0f, 0f, runtimeTexture.width, runtimeTexture.height),
                    new Vector2(0.5f, 0.5f),
                    runtimeTexture.width);
                runtimeSprite.name = "Cave Plant Projectile Sprite Runtime";
            }
            if (runtimeMaterial == null)
                runtimeMaterial = CreateRuntimeMaterial("Cave Plant Projectile Material Runtime");
        }

        private void SpawnParryFlash(Vector2 position, Vector2 parryDirection)
        {
            GameObject flash = new GameObject("CavePlantProjectileParryFlash");
            flash.transform.position = position;
            flash.transform.rotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(parryDirection.y, parryDirection.x) * Mathf.Rad2Deg);

            Texture2D texture = BuildCircleTexture(48, "Cave Plant Parry Flash Texture Runtime");
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            sprite.name = "Cave Plant Parry Flash Sprite Runtime";
            Material material = CreateRuntimeMaterial("Cave Plant Parry Flash Material Runtime");

            SpriteRenderer renderer = flash.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = parryFlashColor;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder + 10;
            flash.transform.localScale = new Vector3(0.8f, 0.22f, 1f);

            float duration = Mathf.Max(0.02f, parryFlashDuration);
            Destroy(flash, duration);
            Destroy(sprite, duration);
            Destroy(texture, duration);
            if (material != null)
                Destroy(material, duration);
        }

        private static Texture2D BuildCircleTexture(int size, string textureName)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = textureName,
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

        private static Material CreateRuntimeMaterial(string materialName)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
        }

        private void OnValidate()
        {
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            speed = Mathf.Max(0.1f, speed);
            damage = Mathf.Max(1, damage);
            lifetime = Mathf.Max(0.1f, lifetime);
            visualSize = Mathf.Max(0.05f, visualSize);
            parryFlashDuration = Mathf.Max(0.02f, parryFlashDuration);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlayerHit(bool applied)
        {
            if (debugLogs)
                Debug.Log($"[CAVE PLANT PROJECTILE] Player hit applied={applied}", this);
        }

        private void OnDestroy()
        {
            if (runtimeSprite != null)
                Destroy(runtimeSprite);
            if (runtimeTexture != null)
                Destroy(runtimeTexture);
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }
    }
}
