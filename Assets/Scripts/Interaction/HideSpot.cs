using System.Collections;
using Game.Input;
using Game.Player;
using UnityEngine;

namespace Game.Interaction
{
    public class HideSpot : MonoBehaviour
    {
        [Header("Hide")]
        [SerializeField] private bool canHide = true;
        [SerializeField] private Transform hidePoint;
        [SerializeField] private Transform exitPoint;
        [SerializeField, Min(0f)] private float fallbackExitVerticalOffset = 1.35f;
        [SerializeField] private bool disablePlayerCollidersWhileHidden = true;
        [SerializeField, Min(0f)] private float hideInputLockSeconds = 0.2f;
        [SerializeField, Min(0f)] private float playerFadeOutSeconds = 0.15f;

        private PlayerController candidateController;
        private Transform candidateRoot;
        private InputReader candidateInput;
        private Rigidbody2D candidateRb;
        private PlayerController hiddenController;
        private InputReader hiddenInput;
        private Rigidbody2D hiddenRb;
        private Collider2D[] hiddenColliders;
        private SpriteRenderer[] hiddenRenderers;
        private Color[] hiddenRendererColors;
        private Coroutine fadeRoutine;
        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;
        private float hideInputLockTimer;
        private bool hidden;

        public bool IsOccupied => hidden;
        protected PlayerController CandidateController => candidateController;
        protected InputReader CandidateInput => candidateInput;
        protected Rigidbody2D CandidateRigidbody => candidateRb;

        protected virtual void OnDisable()
        {
            StopFade();
            RestoreHiddenPlayerRenderers();

            if (hidden)
                ExitHiddenPlayer();

            ClearCandidate();
        }

        protected virtual void Update()
        {
            if (hideInputLockTimer > 0f)
                hideInputLockTimer -= Time.deltaTime;

            if (hidden)
            {
                if (hideInputLockTimer <= 0f && hiddenInput != null && hiddenInput.ConsumeJumpPressed())
                    ExitHiddenPlayer();

                return;
            }

            UpdateVisible();
        }

        protected virtual void UpdateVisible()
        {
        }

        public virtual bool CanEnter(GameObject player)
        {
            if (!canHide || hidden || player == null) return false;
            PlayerController controller = player.GetComponentInParent<PlayerController>();
            return CanEnter(controller);
        }

        public virtual bool TryEnter(GameObject player)
        {
            if (player == null) return false;

            PlayerController controller = player.GetComponentInParent<PlayerController>();
            return TryEnter(controller);
        }

        protected bool TryEnter(PlayerController controller)
        {
            if (!CanEnter(controller)) return false;

            hiddenController = controller;
            hiddenInput = controller == candidateController
                ? candidateInput
                : controller.GetComponent<InputReader>() ?? controller.GetComponentInChildren<InputReader>(true);
            hiddenRb = controller == candidateController
                ? candidateRb
                : controller.GetComponent<Rigidbody2D>() ?? controller.GetComponentInChildren<Rigidbody2D>(true);
            hiddenColliders = controller.GetComponentsInChildren<Collider2D>(true);
            CacheHiddenPlayerRenderers(controller);

            controller.ResetTransientState();

            if (hiddenRb != null)
            {
                cachedBodyType = hiddenRb.bodyType;
                cachedConstraints = hiddenRb.constraints;
                cachedGravityScale = hiddenRb.gravityScale;
                hiddenRb.velocity = Vector2.zero;
                hiddenRb.angularVelocity = 0f;
                hiddenRb.bodyType = RigidbodyType2D.Kinematic;
                hiddenRb.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;
                hiddenRb.gravityScale = 0f;
            }

            Vector3 target = hidePoint != null ? hidePoint.position : transform.position;
            controller.transform.position = target;
            controller.enabled = false;

            if (disablePlayerCollidersWhileHidden)
                SetHiddenPlayerColliders(false);

            hidden = true;
            hideInputLockTimer = hideInputLockSeconds;
            FadeHiddenPlayerOut();
            ClearCandidate();
            return true;
        }

        public void ExitHiddenPlayer()
        {
            if (!hidden) return;

            StopFade();
            RestoreHiddenPlayerRenderers();

            if (hiddenRb != null)
            {
                hiddenRb.bodyType = cachedBodyType;
                hiddenRb.constraints = cachedConstraints;
                hiddenRb.gravityScale = cachedGravityScale;
                hiddenRb.velocity = Vector2.zero;
                hiddenRb.angularVelocity = 0f;
            }

            if (hiddenController != null)
            {
                Vector3 exitPos = GetExitPosition();
                hiddenController.transform.position = exitPos;
                hiddenController.enabled = true;
                hiddenController.ResetTransientState();
            }

            if (disablePlayerCollidersWhileHidden)
                SetHiddenPlayerColliders(true);

            hiddenController = null;
            hiddenInput = null;
            hiddenRb = null;
            hiddenColliders = null;
            hiddenRenderers = null;
            hiddenRendererColors = null;
            hidden = false;
            ClearCandidate();
        }

        public void RegisterCandidate(Collider2D other)
        {
            if (hidden || other == null) return;

            PlayerController controller = other.GetComponentInParent<PlayerController>();
            if (controller == null) return;

            candidateController = controller;
            candidateRoot = controller.transform;
            candidateInput = controller.GetComponent<InputReader>() ?? controller.GetComponentInChildren<InputReader>(true);
            candidateRb = controller.GetComponent<Rigidbody2D>() ?? controller.GetComponentInChildren<Rigidbody2D>(true);
        }

        public void ClearCandidate(Collider2D other)
        {
            if (other != null && candidateRoot != null && other.transform.IsChildOf(candidateRoot))
                ClearCandidate();
        }

        protected void ClearCandidate()
        {
            candidateController = null;
            candidateRoot = null;
            candidateInput = null;
            candidateRb = null;
        }

        protected virtual bool CanEnter(PlayerController controller)
        {
            return canHide && !hidden && controller != null && !controller.IsCarrying;
        }

        private Vector3 GetExitPosition()
        {
            if (exitPoint != null)
                return exitPoint.position;

            Collider2D ownCollider = GetComponent<Collider2D>();
            if (ownCollider != null)
                return new Vector3(ownCollider.bounds.center.x, ownCollider.bounds.max.y + fallbackExitVerticalOffset, transform.position.z);

            return transform.position + Vector3.up * fallbackExitVerticalOffset;
        }

        private void SetHiddenPlayerColliders(bool enabled)
        {
            if (hiddenColliders == null) return;

            for (int i = 0; i < hiddenColliders.Length; i++)
            {
                if (hiddenColliders[i] != null)
                    hiddenColliders[i].enabled = enabled;
            }
        }

        private void CacheHiddenPlayerRenderers(PlayerController controller)
        {
            hiddenRenderers = controller.GetComponentsInChildren<SpriteRenderer>(true);
            if (hiddenRenderers == null || hiddenRenderers.Length == 0)
            {
                hiddenRendererColors = null;
                return;
            }

            hiddenRendererColors = new Color[hiddenRenderers.Length];
            for (int i = 0; i < hiddenRenderers.Length; i++)
                hiddenRendererColors[i] = hiddenRenderers[i] != null ? hiddenRenderers[i].color : Color.white;
        }

        private void FadeHiddenPlayerOut()
        {
            StopFade();

            if (hiddenRenderers == null || hiddenRenderers.Length == 0)
                return;

            if (playerFadeOutSeconds <= 0f)
            {
                SetHiddenPlayerAlpha(0f);
                return;
            }

            fadeRoutine = StartCoroutine(FadeHiddenPlayerAlpha(0f, playerFadeOutSeconds));
        }

        private IEnumerator FadeHiddenPlayerAlpha(float targetAlpha, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                SetHiddenPlayerAlpha(Mathf.Lerp(1f, targetAlpha, t));
                yield return null;
            }

            SetHiddenPlayerAlpha(targetAlpha);
            fadeRoutine = null;
        }

        private void SetHiddenPlayerAlpha(float alpha)
        {
            if (hiddenRenderers == null || hiddenRendererColors == null) return;

            for (int i = 0; i < hiddenRenderers.Length; i++)
            {
                SpriteRenderer renderer = hiddenRenderers[i];
                if (renderer == null) continue;

                Color color = hiddenRendererColors[i];
                color.a *= alpha;
                renderer.color = color;
            }
        }

        private void RestoreHiddenPlayerRenderers()
        {
            if (hiddenRenderers == null || hiddenRendererColors == null) return;

            for (int i = 0; i < hiddenRenderers.Length; i++)
            {
                if (hiddenRenderers[i] != null)
                    hiddenRenderers[i].color = hiddenRendererColors[i];
            }
        }

        private void StopFade()
        {
            if (fadeRoutine == null) return;

            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
    }
}
