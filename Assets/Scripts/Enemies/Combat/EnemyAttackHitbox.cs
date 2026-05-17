using System.Collections.Generic;
using UnityEngine;

namespace Game.Enemies.Combat
{
    [RequireComponent(typeof(Collider2D))]
    public class EnemyAttackHitbox : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private LayerMask targetLayer;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private readonly HashSet<Component> damagedThisActivation = new HashSet<Component>();
        private Collider2D hitboxCollider;
        private Transform source;
        private Vector2 attackDir = Vector2.right;
        private bool active;

        public bool IsActive => active;
        public int HitCountThisActivation { get; private set; }

        private void Awake()
        {
            ResolveCollider();
            DisableHitbox();
        }

        private void OnDisable()
        {
            active = false;
            damagedThisActivation.Clear();
            HitCountThisActivation = 0;
        }

        public void Configure(Transform damageSource, int newDamage, LayerMask newTargetLayer, bool enableDebugLogs)
        {
            source = damageSource != null ? damageSource : transform.root;
            damage = Mathf.Max(1, newDamage);
            targetLayer = newTargetLayer;
            debugLogs = enableDebugLogs;
            ResolveCollider();
        }

        public void SetBox(Vector2 offset, Vector2 size)
        {
            ResolveCollider();

            if (hitboxCollider is BoxCollider2D box)
            {
                box.offset = offset;
                box.size = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
                return;
            }

            transform.localPosition = offset;
        }

        public void Activate(Vector2 dir)
        {
            ResolveCollider();

            attackDir = dir.sqrMagnitude < 0.0001f ? Vector2.right : dir.normalized;
            active = true;
            HitCountThisActivation = 0;
            damagedThisActivation.Clear();

            if (hitboxCollider != null)
                hitboxCollider.enabled = true;

            if (debugLogs)
                Debug.Log($"[ENEMY HITBOX] ON {name} dir={attackDir} dmg={damage}");
        }

        public void DisableHitbox()
        {
            ResolveCollider();

            active = false;

            if (hitboxCollider != null)
            {
                hitboxCollider.isTrigger = true;
                hitboxCollider.enabled = false;
            }

            if (debugLogs)
                Debug.Log($"[ENEMY HITBOX] OFF {name}");
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryDamage(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryDamage(other);
        }

        private void TryDamage(Collider2D other)
        {
            if (!active || other == null) return;

            if (targetLayer.value != 0 && (targetLayer.value & (1 << other.gameObject.layer)) == 0)
                return;

            var damageable =
                other.GetComponent<Game.Combat.IDamageable>() ??
                other.GetComponentInParent<Game.Combat.IDamageable>();

            if (damageable == null) return;

            var key = damageable as Component;
            if (key != null && damagedThisActivation.Contains(key))
                return;

            key = key != null ? key : other;
            damagedThisActivation.Add(key);

            Vector2 sourcePosition = GetDamageSourcePosition();
            damageable.TakeDamage(damage, sourcePosition);
            HitCountThisActivation++;

            if (debugLogs)
                Debug.Log($"[ENEMY HITBOX] HIT {other.name} dmg={damage} source={sourcePosition}");
        }

        private Vector2 GetDamageSourcePosition()
        {
            Vector2 origin = source != null ? (Vector2)source.position : (Vector2)transform.position;
            return origin - attackDir * 0.25f;
        }

        private void ResolveCollider()
        {
            if (hitboxCollider == null)
                hitboxCollider = GetComponent<Collider2D>();

            if (hitboxCollider != null)
                hitboxCollider.isTrigger = true;
        }
    }
}
