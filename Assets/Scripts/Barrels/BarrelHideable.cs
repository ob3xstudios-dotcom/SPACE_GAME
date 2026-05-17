using Game.Interaction;
using Game.Player;
using UnityEngine;

namespace Game.Barrels
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class BarrelHideable : HideSpot
    {
        private BarrelBreakable breakable;
        private BarrelCarryable carryable;
        private BarrelHideTriggerTop[] hideTriggers;
        private Collider2D[] hideTriggerColliders;

        private void Awake()
        {
            breakable = GetComponent<BarrelBreakable>();
            carryable = GetComponent<BarrelCarryable>();
            CacheHideTriggers();
        }

        private void OnEnable()
        {
            if (breakable == null)
                breakable = GetComponent<BarrelBreakable>();

            if (carryable == null)
                carryable = GetComponent<BarrelCarryable>();

            if (hideTriggers == null || hideTriggers.Length == 0)
                CacheHideTriggers();

            if (breakable != null)
                breakable.Broken += HandleBarrelBroken;

            if (carryable != null)
                carryable.CarryStateChanged += HandleCarryStateChanged;

            ApplyCarryState(carryable != null && carryable.IsCarried);
        }

        protected override void OnDisable()
        {
            if (breakable != null)
                breakable.Broken -= HandleBarrelBroken;

            if (carryable != null)
                carryable.CarryStateChanged -= HandleCarryStateChanged;

            base.OnDisable();
        }

        public override bool CanEnter(GameObject player)
        {
            if (carryable != null && carryable.IsCarried) return false;
            return base.CanEnter(player);
        }

        protected override bool CanEnter(PlayerController controller)
        {
            if (carryable != null && carryable.IsCarried) return false;
            return base.CanEnter(controller);
        }

        public void HandleTopTriggerEnter(Collider2D other)
        {
            if (carryable != null && carryable.IsCarried) return;
            RegisterCandidate(other);
        }

        public void HandleTopTriggerExit(Collider2D other)
        {
            ClearCandidate(other);
        }

        private void HandleBarrelBroken(BarrelBreakable brokenBarrel)
        {
            ExitHiddenPlayer();
        }

        private void HandleCarryStateChanged(bool carried)
        {
            ApplyCarryState(carried);
        }

        private void ApplyCarryState(bool carried)
        {
            if (carried)
                ClearCandidate();

            bool enabledState = !carried;

            if (hideTriggers != null)
            {
                for (int i = 0; i < hideTriggers.Length; i++)
                {
                    if (hideTriggers[i] != null)
                        hideTriggers[i].enabled = enabledState;
                }
            }

            if (hideTriggerColliders != null)
            {
                for (int i = 0; i < hideTriggerColliders.Length; i++)
                {
                    if (hideTriggerColliders[i] != null)
                        hideTriggerColliders[i].enabled = enabledState;
                }
            }
        }

        private void CacheHideTriggers()
        {
            hideTriggers = GetComponentsInChildren<BarrelHideTriggerTop>(true);
            hideTriggerColliders = new Collider2D[hideTriggers.Length];

            for (int i = 0; i < hideTriggers.Length; i++)
                hideTriggerColliders[i] = hideTriggers[i] != null ? hideTriggers[i].GetComponent<Collider2D>() : null;
        }
    }
}
