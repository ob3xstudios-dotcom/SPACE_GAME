using System.Collections.Generic;
using UnityEngine;
using Game.Player;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Applies the single longitudinal state transition and independently damages
    /// PlayerHealth through its canonical damage API.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class WoodSplitterSaw : MonoBehaviour
    {
        [SerializeField, Min(1)] private int damage = 1;

        private readonly Dictionary<Collider2D, WoodWorkpiece> woodContacts =
            new Dictionary<Collider2D, WoodWorkpiece>();
        private readonly Dictionary<Collider2D, PlayerHealth> playerContacts =
            new Dictionary<Collider2D, PlayerHealth>();
        private readonly HashSet<PlayerHealth> damagedThisPhysicsStep =
            new HashSet<PlayerHealth>();

        private Collider2D bladeCollider;

        private void Awake()
        {
            bladeCollider = GetComponent<Collider2D>();
            bladeCollider.isTrigger = true;
        }

        private void FixedUpdate()
        {
            damagedThisPhysicsStep.Clear();
        }

        private void OnDisable()
        {
            woodContacts.Clear();
            playerContacts.Clear();
            damagedThisPhysicsStep.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterAndProcess(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterAndProcess(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            woodContacts.Remove(other);
            playerContacts.Remove(other);
        }

        private void RegisterAndProcess(Collider2D other)
        {
            if (other == null)
                return;

            if (!woodContacts.TryGetValue(other, out WoodWorkpiece workpiece))
            {
                workpiece = other.GetComponentInParent<WoodWorkpiece>();
                woodContacts[other] = workpiece;
            }

            workpiece?.TryApplyLongitudinalCut();

            if (!playerContacts.TryGetValue(other, out PlayerHealth playerHealth))
            {
                playerHealth = other.GetComponentInParent<PlayerHealth>();
                playerContacts[other] = playerHealth;
            }

            if (playerHealth != null && damagedThisPhysicsStep.Add(playerHealth))
                playerHealth.TakeDamage(damage, transform.position);
        }
    }
}
