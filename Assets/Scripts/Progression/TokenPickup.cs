using System;
using Game.Player;
using UnityEngine;

namespace Game.Progression
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class TokenPickup : MonoBehaviour
    {
        [Header("Persistent identity")]
        [Tooltip("Stable and unique within this scene.")]
        [SerializeField] private string pickupId = string.Empty;

        [Header("Definition")]
        [SerializeField] private TokenDefinition tokenDefinition;

        [Header("References")]
        [SerializeField] private Collider2D pickupCollider;
        [SerializeField] private TokenPickupVisual visual;

        private bool consumed;

        public string PickupId => pickupId;
        public TokenDefinition Definition => tokenDefinition;

        private void Awake()
        {
            ResolveReferences();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
            if (visual != null && tokenDefinition != null)
                visual.Configure(tokenDefinition.PermanentModifier.stat);

            if (string.IsNullOrEmpty(pickupId) || tokenDefinition == null ||
                string.IsNullOrEmpty(tokenDefinition.TokenTypeId))
            {
                Debug.LogWarning(
                    $"[PROGRESSION] TokenPickup '{name}' needs a stable pickup ID and definition.",
                    this);
                return;
            }

            if (ProgressionService.Instance.IsTokenCollected(gameObject.scene.name, pickupId))
                ConsumeWithoutReward();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed || other == null || tokenDefinition == null ||
                string.IsNullOrEmpty(pickupId))
            {
                return;
            }

            PlayerController player = other.GetComponent<PlayerController>() ??
                                      other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            ProgressionService service = ProgressionService.Instance;
            string sceneName = gameObject.scene.name;
            if (service.IsTokenCollected(sceneName, pickupId))
            {
                ConsumeWithoutReward();
                return;
            }

            if (service.TryCollectToken(sceneName, pickupId, tokenDefinition))
                ConsumeWithoutReward();
        }

        private void ConsumeWithoutReward()
        {
            if (consumed)
                return;

            consumed = true;
            if (pickupCollider != null)
                pickupCollider.enabled = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void Reset()
        {
            ResolveReferences();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

#if UNITY_EDITOR
        [ContextMenu("Progression/Generate New Persistent ID")]
        private void GenerateNewPersistentId()
        {
            UnityEditor.Undo.RecordObject(this, "Generate Token Pickup ID");
            pickupId = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnValidate()
        {
            pickupId = ProgressionSaveData.NormalizeId(pickupId);
            ResolveReferences();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }
#endif

        private void ResolveReferences()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider2D>();
            if (visual == null)
                visual = GetComponentInChildren<TokenPickupVisual>(true);
        }
    }
}
