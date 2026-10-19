using Game.Player;
using Game.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Currency
{
    /// <summary>
    /// Runtime-only bridge between the canonical player death/ground state and CurrencyService.
    /// It is attached automatically and does not need to be added to scene or Player prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurrencyPlayerAgent : MonoBehaviour
    {
        private PlayerHealth playerHealth;
        private PlayerController playerController;
        private Collider2D playerCollider;
        private readonly Collider2D[] overlapResults = new Collider2D[24];
        private Collider2D evaluatedSupport;
        private bool evaluatedSupportIsSafe;
        private bool hasSafePosition;
        private bool deathProcessed;
        private bool subscribedToHealth;
        private Vector3 lastSafePosition;
        private string lastSafeSceneName = string.Empty;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeToHealth();
        }

        private void OnDisable()
        {
            UnsubscribeFromHealth();
        }

        private void FixedUpdate()
        {
            if (deathProcessed)
                return;

            CaptureSafePositionIfAvailable();
        }

        public void HandleFatalFall()
        {
            HandleDeathOnce("fall");
        }

        internal void Bind(PlayerHealth health)
        {
            if (health == null)
                return;

            if (playerHealth != health)
            {
                UnsubscribeFromHealth();
                playerHealth = health;
                playerController = health.GetComponent<PlayerController>();
                playerCollider = health.GetComponent<Collider2D>();
            }

            if (isActiveAndEnabled)
                SubscribeToHealth();
        }

        internal bool IsBoundTo(PlayerHealth health)
        {
            return health != null && playerHealth == health && subscribedToHealth;
        }

        private void HandlePlayerDied()
        {
            HandleDeathOnce("health");
        }

        private void HandlePlayerRespawned()
        {
            deathProcessed = false;
            evaluatedSupport = null;
            evaluatedSupportIsSafe = false;
        }

        private void HandleDeathOnce(string cause)
        {
            if (deathProcessed)
                return;

            deathProcessed = true;
            CurrencyService service = CurrencyService.Instance;
            int carriedCoinsAtDeath = service.CarriedCoins;
            service.LogDiagnostic(
                $"[CURRENCY][DEATH] type={cause} carried={carriedCoinsAtDeath} " +
                $"player='{name}' healthInstance={(playerHealth != null ? playerHealth.GetInstanceID() : 0)} " +
                $"processedOnce=true",
                this);

            if (carriedCoinsAtDeath <= 0)
            {
                service.LogDiagnostic(
                    $"[CURRENCY][DROP] type={cause} carried=0 bagId='<none>' " +
                    "scene='<none>' position='<none>' result=skipped reason=empty-wallet",
                    this);
                return;
            }

            if (!TryGetRecoverableDropPosition(out string sceneName, out Vector3 position))
            {
                service.LogDiagnostic(
                    $"[CURRENCY][DROP] type={cause} carried={carriedCoinsAtDeath} " +
                    "bagId='<none>' scene='<none>' position='<none>' " +
                    "result=skipped reason=no-recoverable-position",
                    this);
                Debug.LogWarning(
                    $"[CURRENCY] Death cause='{cause}' had {carriedCoinsAtDeath} carried coins, " +
                    "but no recoverable position is known. The coins remain in CarriedCoins.",
                    this);
                return;
            }

            if (!service.TryDropCarriedCoins(cause, sceneName, position, out _))
            {
                Debug.LogWarning(
                    $"[CURRENCY] Could not persist a coin bag for death cause='{cause}'. " +
                    "CarriedCoins were preserved.",
                    this);
            }
        }

        private void CaptureSafePositionIfAvailable()
        {
            ResolveReferences();
            if (playerController == null ||
                !playerController.IsGrounded ||
                playerController.IsGroundedOnMovingSupport)
            {
                return;
            }

            Collider2D support = playerController.CurrentGroundCollider;
            if (support == null)
                return;

            if (support != evaluatedSupport)
            {
                evaluatedSupport = support;
                evaluatedSupportIsSafe = IsStableRecoverableSupport(support);
            }

            if (!evaluatedSupportIsSafe)
                return;

            if (IsInsideKnownHazard())
                return;

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.name))
                return;

            lastSafePosition = transform.position;
            lastSafeSceneName = scene.name;
            hasSafePosition = CurrencySaveData.IsFinite(lastSafePosition);
        }

        private bool TryGetRecoverableDropPosition(out string sceneName, out Vector3 position)
        {
            Scene currentScene = gameObject.scene;
            if (hasSafePosition &&
                currentScene.IsValid() &&
                string.Equals(lastSafeSceneName, currentScene.name, System.StringComparison.Ordinal))
            {
                sceneName = lastSafeSceneName;
                position = lastSafePosition;
                return true;
            }

            GameManager manager = GameManager.Instance;
            Checkpoint checkpoint = manager != null ? manager.ActiveCheckpoint : null;
            Transform respawnTransform = checkpoint != null ? checkpoint.RespawnTransform : null;
            if (respawnTransform != null && currentScene.IsValid() && !string.IsNullOrEmpty(currentScene.name))
            {
                sceneName = currentScene.name;
                position = respawnTransform.position;
                return CurrencySaveData.IsFinite(position);
            }

            sceneName = string.Empty;
            position = default;
            return false;
        }

        private static bool IsStableRecoverableSupport(Collider2D support)
        {
            if (support == null || !support.enabled || support.isTrigger || !support.gameObject.activeInHierarchy)
                return false;

            Rigidbody2D supportBody = support.attachedRigidbody;
            if (supportBody != null && supportBody.bodyType != RigidbodyType2D.Static)
                return false;

            return !ContainsUnsafeBehaviourInHierarchy(support.transform);
        }

        private bool IsInsideKnownHazard()
        {
            if (playerCollider == null)
                return false;

            Bounds bounds = playerCollider.bounds;
            int count = Physics2D.OverlapBoxNonAlloc(
                bounds.center,
                bounds.size,
                0f,
                overlapResults);

            for (int i = 0; i < count; i++)
            {
                Collider2D overlap = overlapResults[i];
                overlapResults[i] = null;
                if (overlap == null || overlap == playerCollider ||
                    overlap.transform.root == transform.root)
                {
                    continue;
                }

                if (ContainsUnsafeBehaviourInHierarchy(overlap.transform))
                    return true;
            }

            return false;
        }

        private static bool ContainsUnsafeBehaviourInHierarchy(Transform origin)
        {
            Transform current = origin;
            while (current != null)
            {
                MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;

                    string typeName = behaviour.GetType().Name;
                    if (typeName.Contains("Hazard") ||
                        typeName.Contains("DamageZone") ||
                        typeName.Contains("DeathVolume") ||
                        typeName.Contains("SawTrap") ||
                        typeName.Contains("SplitterSaw") ||
                        typeName.Contains("DisposalTrigger"))
                    {
                        return true;
                    }
                }

                current = current.parent;
            }

            return false;
        }

        private void ResolveReferences()
        {
            if (playerHealth == null)
                playerHealth = GetComponent<PlayerHealth>();
            if (playerController == null)
                playerController = GetComponent<PlayerController>();
            if (playerCollider == null)
                playerCollider = GetComponent<Collider2D>();
        }

        private void SubscribeToHealth()
        {
            if (playerHealth == null || subscribedToHealth)
                return;

            playerHealth.OnDied += HandlePlayerDied;
            playerHealth.OnRespawned += HandlePlayerRespawned;
            subscribedToHealth = true;
        }

        private void UnsubscribeFromHealth()
        {
            if (playerHealth == null || !subscribedToHealth)
                return;

            playerHealth.OnDied -= HandlePlayerDied;
            playerHealth.OnRespawned -= HandlePlayerRespawned;
            subscribedToHealth = false;
        }
    }
}
