using System;
using Game.Combat;
using Game.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Barrels
{
    public class BarrelBreakable : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField, Min(1)] private int maxHealth = 3;
        [SerializeField] private bool resetHealthOnEnable = true;

        [Header("Visual Damage")]
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite damagedSprite;
        [SerializeField] private Sprite brokenSprite;

        [Header("Break")]
        [SerializeField] private bool destroyOnBreak = false;
        [SerializeField, Min(0f)] private float destroyDelay = 0f;
        [SerializeField] private bool keepSolidColliderOnBreak = true;
        [SerializeField] private bool changeLayerOnBreak = true;
        [SerializeField] private string brokenLayerName = "BarrelBroken";
        [SerializeField] private string enemyLayerName = "Enemy";
        [SerializeField] private string playerLayerName = "Player";
        [SerializeField] private GameObject[] disableOnBreak;

        [Header("Events")]
        [SerializeField] private UnityEvent onDamaged;
        [SerializeField] private UnityEvent onBroken;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int currentHealth;
        private bool broken;

        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        public bool IsBroken => broken;

        public event Action<BarrelBreakable> Damaged;
        public event Action<BarrelBreakable> Broken;

        private void Awake()
        {
            ResolveRefs();
            currentHealth = maxHealth;
            RefreshVisual();
        }

        private void OnEnable()
        {
            if (broken)
            {
                currentHealth = 0;
                RefreshVisual();
                return;
            }

            if (!resetHealthOnEnable) return;

            broken = false;
            currentHealth = maxHealth;
            RefreshVisual();
        }

        private void Reset()
        {
            ResolveRefs();

            if (targetRenderer != null && normalSprite == null)
                normalSprite = targetRenderer.sprite;
        }

        public void TakeDamage(int damage, Vector2 sourcePosition)
        {
            if (broken || damage <= 0) return;

            currentHealth = Mathf.Max(0, currentHealth - damage);
            RefreshVisual();
            onDamaged?.Invoke();
            Damaged?.Invoke(this);

            if (currentHealth <= 0)
                Break();
        }

        public void Break()
        {
            if (broken) return;

            if (debugLogs)
                Debug.Log($"[RESPAWN TRACE] BarrelBreakable.Break {name} t={Time.time:0.000} unscaled={Time.unscaledTime:0.000}");

            broken = true;
            currentHealth = 0;
            RefreshVisual();

            onBroken?.Invoke();
            Broken?.Invoke(this);

            DisableInteractionComponents();
            ApplyBrokenLayer();

            if (disableOnBreak != null)
            {
                for (int i = 0; i < disableOnBreak.Length; i++)
                {
                    if (disableOnBreak[i] != null)
                        disableOnBreak[i].SetActive(false);
                }
            }

            if (destroyOnBreak)
                Destroy(gameObject, destroyDelay);
        }

        private void RefreshVisual()
        {
            if (targetRenderer == null) return;

            if (currentHealth >= maxHealth)
            {
                if (normalSprite != null)
                    targetRenderer.sprite = normalSprite;

                return;
            }

            if (currentHealth <= 0)
            {
                if (brokenSprite != null)
                    targetRenderer.sprite = brokenSprite;
                else if (damagedSprite != null)
                    targetRenderer.sprite = damagedSprite;

                return;
            }

            if (damagedSprite != null)
                targetRenderer.sprite = damagedSprite;
        }

        private void ResolveRefs()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        private void DisableInteractionComponents()
        {
            BarrelCarryable carryable = GetComponent<BarrelCarryable>();
            if (carryable != null)
                carryable.enabled = false;

            BarrelHideable hideable = GetComponent<BarrelHideable>();
            if (hideable != null)
                hideable.enabled = false;

            BarrelHideTriggerTop[] hideTriggers = GetComponentsInChildren<BarrelHideTriggerTop>(true);
            for (int i = 0; i < hideTriggers.Length; i++)
            {
                if (hideTriggers[i] != null)
                    hideTriggers[i].enabled = false;
            }

            HideTrigger[] genericHideTriggers = GetComponentsInChildren<HideTrigger>(true);
            for (int i = 0; i < genericHideTriggers.Length; i++)
            {
                if (genericHideTriggers[i] != null)
                    genericHideTriggers[i].enabled = false;
            }

            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D col = colliders[i];
                if (col == null) continue;

                if (col.isTrigger || !keepSolidColliderOnBreak)
                    col.enabled = false;
            }
        }

        private void ApplyBrokenLayer()
        {
            if (!changeLayerOnBreak) return;

            int brokenLayer = LayerMask.NameToLayer(brokenLayerName);
            if (brokenLayer < 0) return;

            int enemyLayer = LayerMask.NameToLayer(enemyLayerName);
            if (enemyLayer >= 0)
                Physics2D.IgnoreLayerCollision(enemyLayer, brokenLayer, true);

            int playerLayer = LayerMask.NameToLayer(playerLayerName);
            if (playerLayer >= 0)
                Physics2D.IgnoreLayerCollision(playerLayer, brokenLayer, true);

            SetLayerRecursive(transform, brokenLayer);
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;

            root.gameObject.layer = layer;

            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }
    }
}
