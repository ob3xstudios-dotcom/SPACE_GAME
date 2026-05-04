using UnityEngine;

namespace Game.Player
{
    public class PlayerProgress : MonoBehaviour
    {
        [Header("Items")]
        [SerializeField] private bool hasDagger = false;
        public bool HasDagger => hasDagger;

        [Header("Parry Dagger Upgrade")]
        [SerializeField, Min(0)] private int parryStunManaCost = 1;
        [SerializeField, Range(0f, 3f)] private float parryStunSeconds = 0.65f;

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