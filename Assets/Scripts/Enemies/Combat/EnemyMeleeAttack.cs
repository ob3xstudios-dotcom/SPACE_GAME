using UnityEngine;

namespace Game.Enemies.Combat
{
    public class EnemyMeleeAttack : MonoBehaviour
    {
        [Header("Animation Driven Hitbox")]
        [SerializeField] private EnemyAttackHitbox attackHitbox;
        [SerializeField] private LayerMask targetLayer;

        [Header("Directional Attack (X/Y)")]
        [SerializeField] private bool directional = true;

        [Tooltip("Si ON, fuerza el ataque a 4 direcciones (L/R/U/D).")]
        [SerializeField] private bool snapTo4Directions = true;

        [Tooltip("Cuanto MÁS BAJO, más fácil será elegir vertical (arriba/abajo) aunque haya algo de X. Ej: 0.6 = más vertical.")]
        [SerializeField, Range(0.2f, 1.0f)] private float verticalBias = 0.65f;

        [Tooltip("Si |y| >= este valor, prioriza vertical sí o sí (útil si pivots están raros).")]
        [SerializeField, Range(0f, 5f)] private float forceVerticalIfAbsYOver = 0.35f;

        [Tooltip("Distancia desde el centro del enemy hasta el punto de ataque.")]
        [SerializeField, Min(0.01f)] private float attackOffset = 0.6f;

        [Header("Hitbox Presets")]
        [SerializeField] private Vector2 sideOffset = new Vector2(0.65f, 0f);
        [SerializeField] private Vector2 sideSize = new Vector2(0.9f, 0.8f);
        [SerializeField] private Vector2 upOffset = new Vector2(0f, 0.85f);
        [SerializeField] private Vector2 upSize = new Vector2(0.85f, 0.9f);
        [SerializeField] private Vector2 downOffset = new Vector2(0f, -0.65f);
        [SerializeField] private Vector2 downSize = new Vector2(0.85f, 0.75f);

        [Header("Damage")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0.05f)] private float cooldown = 0.8f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;
        [SerializeField] private bool debugLogs = false;

        private float cd;
        private Vector2 lockedDir = Vector2.right;

        public bool CanAttack => cd <= 0f;
        public float Cooldown => cooldown;
        public bool HitboxActive => attackHitbox != null && attackHitbox.IsActive;
        public bool DidHitThisActivation => attackHitbox != null && attackHitbox.HitCountThisActivation > 0;

        private void Awake()
        {
            ResolveHitbox();
            ConfigureHitbox();
        }

        private void Update()
        {
            if (cd > 0f) cd -= Time.deltaTime;
        }

        private void OnDisable()
        {
            DisableHitbox();
        }

        [ContextMenu("Setup Attack Hitbox")]
        private void SetupAttackHitbox()
        {
            ResolveHitbox();
            ConfigureHitbox();
            ApplyHitboxPreset(Vector2.right);
            DisableHitbox();
        }

        public void ForceCooldown()
        {
            cd = cooldown;
        }

        public void BeginAttack(Vector2 attackDir)
        {
            lockedDir = SnapAttackDirection(attackDir);
            DisableHitbox();

            if (debugLogs)
                Debug.Log($"[ENEMY ATTACK] {name} begin lockedDir={lockedDir}");
        }

        public void SetAttackDirection(Vector2 attackDir)
        {
            lockedDir = SnapAttackDirection(attackDir);

            if (attackHitbox != null && attackHitbox.IsActive)
                ApplyHitboxPreset(lockedDir);
        }

        public void SetAttackDirectionSide()
        {
            float side = lockedDir.x != 0f ? Mathf.Sign(lockedDir.x) : Mathf.Sign(transform.localScale.x == 0f ? 1f : transform.localScale.x);
            SetAttackDirection(new Vector2(side, 0f));
        }

        public void SetAttackDirectionUp()
        {
            SetAttackDirection(Vector2.up);
        }

        public void SetAttackDirectionDown()
        {
            SetAttackDirection(Vector2.down);
        }

        public void ActivateHitbox()
        {
            if (!CanAttack)
            {
                if (debugLogs) Debug.Log($"[ENEMY ATTACK] {name} hitbox blocked by cooldown ({cd:0.00}s)");
                return;
            }

            ResolveHitbox();
            ConfigureHitbox();

            if (attackHitbox == null)
            {
                Debug.LogWarning($"[ENEMY ATTACK] {name} has no AttackHitbox. Create a child named AttackHitbox with a trigger Collider2D and EnemyAttackHitbox.");
                return;
            }

            ApplyHitboxPreset(lockedDir);
            cd = cooldown;
            attackHitbox.Activate(lockedDir);
        }

        public void DisableHitbox()
        {
            if (attackHitbox != null)
                attackHitbox.DisableHitbox();
        }

        public bool TryAttack(Vector2 sourcePosition) => TryAttack(sourcePosition, Vector2.right);

        public bool TryAttack(Vector2 sourcePosition, Vector2 attackDir)
        {
            if (!CanAttack)
            {
                if (debugLogs) Debug.Log($"[ENEMY ATTACK] {name} blocked by cooldown ({cd:0.00}s)");
                return false;
            }

            if (targetLayer.value == 0)
            {
                Debug.LogWarning($"[ENEMY ATTACK] {name} targetLayer=NOTHING (mask=0). Marca la layer del Player en el inspector.");
                // seguimos, pero va a dar hits=0 siempre
            }

            BeginAttack(attackDir);
            ActivateHitbox();
            return DidHitThisActivation;
        }

        private void ResolveHitbox()
        {
            if (attackHitbox != null) return;

            Transform child = transform.Find("AttackHitbox");
            if (child != null)
                attackHitbox = child.GetComponent<EnemyAttackHitbox>();

            if (attackHitbox == null)
                attackHitbox = GetComponentInChildren<EnemyAttackHitbox>(true);

            if (attackHitbox == null)
            {
                GameObject hitboxGo = new GameObject("AttackHitbox");
                hitboxGo.transform.SetParent(transform, false);
                hitboxGo.transform.localPosition = Vector3.zero;
                hitboxGo.transform.localRotation = Quaternion.identity;
                hitboxGo.transform.localScale = Vector3.one;

                BoxCollider2D box = hitboxGo.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.enabled = false;

                attackHitbox = hitboxGo.AddComponent<EnemyAttackHitbox>();

                if (debugLogs)
                    Debug.Log($"[ENEMY ATTACK] {name} created runtime AttackHitbox child. Add it to the prefab to tune animation frames in-editor.");
            }
        }

        private void ConfigureHitbox()
        {
            if (attackHitbox == null) return;
            attackHitbox.Configure(transform, damage, targetLayer, debugLogs);
        }

        private Vector2 SnapAttackDirection(Vector2 attackDir)
        {
            Vector2 dir = attackDir.sqrMagnitude < 0.0001f ? Vector2.right : attackDir.normalized;
            if (!directional) return Vector2.right;
            if (!snapTo4Directions) return dir;

            float ax = Mathf.Abs(attackDir.x);
            float ay = Mathf.Abs(attackDir.y);

            bool forceVertical = ay >= forceVerticalIfAbsYOver && ay > ax;
            bool chooseVertical = forceVertical || ay >= ax * verticalBias;

            if (chooseVertical)
                return new Vector2(0f, Mathf.Sign(attackDir.y == 0f ? 1f : attackDir.y));

            return new Vector2(Mathf.Sign(attackDir.x == 0f ? 1f : attackDir.x), 0f);
        }

        private void ApplyHitboxPreset(Vector2 dir)
        {
            if (attackHitbox == null) return;

            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                attackHitbox.SetBox(dir.y > 0f ? upOffset : downOffset, dir.y > 0f ? upSize : downSize);
                return;
            }

            float side = Mathf.Sign(dir.x == 0f ? 1f : dir.x);
            Vector2 offset = new Vector2(Mathf.Abs(sideOffset.x) * side, sideOffset.y);
            attackHitbox.SetBox(offset, sideSize);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;
            Gizmos.color = Color.red;
            Vector2 dir = lockedDir.sqrMagnitude < 0.0001f ? Vector2.right : lockedDir;
            Vector2 offset;
            Vector2 size;

            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                offset = dir.y > 0f ? upOffset : downOffset;
                size = dir.y > 0f ? upSize : downSize;
            }
            else
            {
                float side = Mathf.Sign(dir.x == 0f ? 1f : dir.x);
                offset = new Vector2(Mathf.Abs(sideOffset.x) * side, sideOffset.y);
                size = sideSize;
            }

            Gizmos.DrawWireCube((Vector2)transform.position + offset, size);
        }
#endif
    }
}
