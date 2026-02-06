using UnityEngine;

namespace Game.Player
{
    public class PlayerParry : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private Game.Input.InputReader input;
        [SerializeField] private PlayerAnimatorDriver animDriver;
        [SerializeField] private PlayerMana mana;

        [Header("Skill gate")]
        [SerializeField] private bool hasParry = true;
        [SerializeField] private bool hasParryDagger = false; // ← item futuro

        [Header("Parry Tuning")]
        [SerializeField, Range(0.05f, 0.25f)] private float parryWindow = 0.12f;
        [SerializeField, Range(0.1f, 1.2f)] private float parryCooldown = 0.45f;
        [SerializeField, Range(0f, 0.5f)] private float failRecovery = 0.18f;

        [Header("Dagger Upgrade")]
        [SerializeField] private int daggerManaCost = 1;
        [SerializeField] private float daggerStunSeconds = 1.2f;

        [Header("Hitbox")]
        [SerializeField] private float radius = 0.55f;
        [SerializeField] private float forwardOffset = 0.65f;
        [SerializeField] private LayerMask enemyLayer;

        [Header("Enemy push")]
        [SerializeField] private float pushSpeed = 8.5f;
        [SerializeField] private float pushLockSeconds = 0.18f;

        private float windowT;
        private float cdT;
        private float failT;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (input == null) input = GetComponent<Game.Input.InputReader>();
            if (animDriver == null) animDriver = GetComponent<PlayerAnimatorDriver>();
            if (mana == null) mana = GetComponent<PlayerMana>();
        }

        private void Update()
        {
            cdT -= Time.deltaTime;
            windowT -= Time.deltaTime;
            failT -= Time.deltaTime;

            if (failT > 0f) return;
            if (!hasParry) return;

            if (input != null && input.ConsumeParryPressed())
            {
                if (cdT > 0f) return;

                windowT = parryWindow;
                cdT = parryCooldown;

                animDriver?.TriggerParry();

                bool success = TryParryHit();

                if (!success)
                    failT = failRecovery;
            }
        }

        private bool TryParryHit()
        {
            Vector2 origin = transform.position;
            Vector2 dir = controller != null && controller.FacingLeft ? Vector2.left : Vector2.right;
            Vector2 p = origin + dir * forwardOffset;

            var hits = Physics2D.OverlapCircleAll(p, radius, enemyLayer);
            if (hits == null || hits.Length == 0) return false;

            foreach (var col in hits)
            {
                if (col == null) continue;

                var enemy = col.GetComponentInParent<Game.Enemies.EnemyBase>();
                if (enemy == null) continue;

                if (!enemy.IsParryable) continue;

                Game.Enemies.States.EnemyAttackState.TryParryEnemy(enemy);

                Vector2 pushDir = ((Vector2)enemy.transform.position - origin);
                if (pushDir.sqrMagnitude < 0.0001f) pushDir = dir;

                enemy.ApplyParryPush(pushDir, pushSpeed, pushLockSeconds);

                // ✅ Dagger upgrade check
                if (hasParryDagger && mana != null && mana.ConsumeMana(daggerManaCost))
                {
                    enemy.ApplyParryStun(daggerStunSeconds);
                }

                return true;
            }

            return false;
        }
    }
}
