using UnityEngine;

namespace Game.Player
{
    public class PlayerProgress : MonoBehaviour
    {
        [Header("Items / Unlocks")]
        [SerializeField] private bool hasDagger = false;
        public bool HasDagger => hasDagger;

        [Header("Parry Upgrades (when dagger + mana)")]
        [SerializeField, Min(0)] private int parryStunManaCost = 1;
        [SerializeField, Range(0f, 3f)] private float parryStunSeconds = 0.6f;

        public int ParryStunManaCost => parryStunManaCost;
        public float ParryStunSeconds => parryStunSeconds;

        public void GrantDagger()
        {
            hasDagger = true;
        }

        public void SetDagger(bool value)
        {
            hasDagger = value;
        }
    }
}
