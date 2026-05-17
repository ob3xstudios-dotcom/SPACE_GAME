using System.Collections;
using UnityEngine;
using Game.Enemies.Combat;
using Game.Systems;

namespace Game.Enemies
{
    public class EnemyMeleeAnimatorDriver : MonoBehaviour
    {
        [Header("Refs (mejor arrastrarlas a mano en Inspector)")]
        [SerializeField] private Animator anim;
        [SerializeField] private EnemyMeleeAttack melee;
        [SerializeField] private Rigidbody2D rb;

        [Header("Animator Params")]
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private string attackDirInt = "AttackDir"; // 0 side, 1 up
        [SerializeField] private string parriedTrigger = "Parried";

        [Header("Impact timing (fallback si el Animation Event no dispara)")]
        [SerializeField] private bool useFallbackHitboxIfNoAnimationEvent = false;
        [SerializeField, Range(0f, 0.5f)] private float hitDelaySeconds = 0.08f;
        [SerializeField, Range(0.01f, 0.5f)] private float fallbackActiveSeconds = 0.12f;

        [Header("Hitstop")]
        [SerializeField, Range(0f, 0.12f)] private float hitStopSeconds = 0.06f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Vector2 lockedDir = Vector2.right;
        private Coroutine fallbackHitCo;
        private Coroutine activeWindowCo;
        private bool hitboxOpenedThisAttack;
        private int parriedHash;
        private AnimatorControllerParameterType parriedParamType;
        private bool hasParriedParam;

        private void OnValidate() => ResolveRefs();
        private void Awake()
        {
            ResolveRefs();
            CacheOptionalParriedParam();
        }

        private void OnDisable()
        {
            if (fallbackHitCo != null)
            {
                StopCoroutine(fallbackHitCo);
                fallbackHitCo = null;
            }

            if (activeWindowCo != null)
            {
                StopCoroutine(activeWindowCo);
                activeWindowCo = null;
            }

            melee?.DisableHitbox();
        }

        private void ResolveRefs()
        {
            // Animation Events llegan al MISMO GO que tiene el Animator
            if (anim == null) anim = GetComponent<Animator>();
            // Melee/RB suelen estar en el root; el driver debe estar donde está el Animator
            if (melee == null) melee = GetComponentInParent<EnemyMeleeAttack>();
            if (rb == null) rb = GetComponentInParent<Rigidbody2D>();
        }

        private void CacheOptionalParriedParam()
        {
            hasParriedParam = false;
            parriedHash = 0;

            if (anim == null || string.IsNullOrWhiteSpace(parriedTrigger))
                return;

            parriedHash = Animator.StringToHash(parriedTrigger);
            AnimatorControllerParameter[] parameters = anim.parameters;

            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.nameHash != parriedHash) continue;

                hasParriedParam = true;
                parriedParamType = parameter.type;
                return;
            }
        }

        public void BeginAttack(Vector2 rawDir)
        {
            ResolveRefs();

            hitboxOpenedThisAttack = false;
            lockedDir = (rawDir.sqrMagnitude < 0.0001f) ? Vector2.right : rawDir.normalized;

            int dirType = ShouldUseUp(rawDir) ? 1 : 0;

            if (debugLogs)
                Debug.Log($"[MELEE DRIVER] BeginAttack raw={rawDir} lockedDir={lockedDir} dirType={dirType}");

            if (anim == null)
            {
                Debug.LogError("[MELEE DRIVER] anim == NULL (¿el Animator está en este mismo GameObject?)");
                return;
            }

            anim.SetInteger(attackDirInt, dirType);
            anim.ResetTrigger(attackTrigger);
            anim.SetTrigger(attackTrigger);

            melee?.BeginAttack(lockedDir);

            if (fallbackHitCo != null) StopCoroutine(fallbackHitCo);
            fallbackHitCo = useFallbackHitboxIfNoAnimationEvent
                ? StartCoroutine(FallbackHitRoutine(hitDelaySeconds))
                : null;
        }

        // ✅ Opcional: feedback de parry en el enemy
        public void TriggerParried()
        {
            ResolveRefs();
            if (anim == null) return;

            if (!hasParriedParam)
                CacheOptionalParriedParam();

            if (!hasParriedParam)
                return;

            if (parriedParamType == AnimatorControllerParameterType.Trigger)
            {
                anim.ResetTrigger(parriedHash);
                anim.SetTrigger(parriedHash);
                return;
            }

            if (parriedParamType == AnimatorControllerParameterType.Bool)
                anim.SetBool(parriedHash, true);
        }

        // ✅ Pon este nombre en el Animation Event (para evitar “same name…”)
        public void Anim_DoMeleeHit_Enemy()
        {
            if (debugLogs) Debug.Log("[MELEE DRIVER] Anim_DoMeleeHit_Enemy CALLED");
            OpenHitboxWindow();
        }

        // Compat (si ya lo teníais puesto)
        public void Anim_DoMeleeHit()
        {
            if (debugLogs) Debug.Log("[MELEE DRIVER] Anim_DoMeleeHit CALLED");
            OpenHitboxWindow();
        }

        public void Anim_EnableEnemyHitbox()
        {
            if (debugLogs) Debug.Log("[MELEE DRIVER] Anim_EnableEnemyHitbox CALLED");
            OpenHitboxWindow(false);
        }

        public void Anim_DisableEnemyHitbox()
        {
            if (debugLogs) Debug.Log("[MELEE DRIVER] Anim_DisableEnemyHitbox CALLED");
            CloseHitboxWindow();
        }

        public void Anim_EnemyHitboxSide()
        {
            ResolveRefs();
            melee?.SetAttackDirectionSide();
        }

        public void Anim_EnemyHitboxUp()
        {
            ResolveRefs();
            melee?.SetAttackDirectionUp();
        }

        public void Anim_EnemyHitboxDown()
        {
            ResolveRefs();
            melee?.SetAttackDirectionDown();
        }

        private IEnumerator FallbackHitRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (hitboxOpenedThisAttack) yield break;

            if (debugLogs)
                Debug.Log("[MELEE DRIVER] FallbackHitbox (Animation Event no disparó)");

            OpenHitboxWindow();
        }

        private void OpenHitboxWindow(bool autoClose = true)
        {
            hitboxOpenedThisAttack = true;

            if (fallbackHitCo != null)
            {
                StopCoroutine(fallbackHitCo);
                fallbackHitCo = null;
            }

            ResolveRefs();

            if (melee == null || rb == null)
            {
                Debug.LogError($"[MELEE DRIVER] OpenHitbox FAIL melee={(melee ? "OK" : "NULL")} rb={(rb ? "OK" : "NULL")}");
                return;
            }

            if (debugLogs)
                Debug.Log($"[MELEE DRIVER] DoHit rbPos={rb.position} lockedDir={lockedDir} CanAttack={melee.CanAttack}");

            melee.SetAttackDirection(lockedDir);
            melee.ActivateHitbox();

            if (!autoClose) return;

            if (activeWindowCo != null) StopCoroutine(activeWindowCo);
            activeWindowCo = StartCoroutine(AutoCloseHitboxRoutine(fallbackActiveSeconds));
        }

        private IEnumerator AutoCloseHitboxRoutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            CloseHitboxWindow();
        }

        private void CloseHitboxWindow()
        {
            if (activeWindowCo != null)
            {
                StopCoroutine(activeWindowCo);
                activeWindowCo = null;
            }

            ResolveRefs();

            bool didHit = melee != null && melee.DidHitThisActivation;
            melee?.DisableHitbox();

            if (didHit && hitStopSeconds > 0f)
                HitStopManager.Request(hitStopSeconds);
        }

        private static bool ShouldUseUp(Vector2 raw)
        {
            float ax = Mathf.Abs(raw.x);
            float ay = Mathf.Abs(raw.y);
            return raw.y > 0f && ay > ax;
        }

    }
}
