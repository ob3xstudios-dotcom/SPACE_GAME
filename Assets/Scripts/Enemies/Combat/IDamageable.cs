using UnityEngine;

namespace Game.Combat
{
    public enum PlayerAttackDirection
    {
        Horizontal,
        Up,
        Down
    }

    public enum PlayerAttackResolution
    {
        None,
        Damage,
        BellyUp,
        Armored
    }

    public readonly struct PlayerAttackContext
    {
        public PlayerAttackContext(
            int attackId,
            int damage,
            PlayerAttackDirection direction,
            Vector2 attackVector,
            Vector2 origin,
            int attackerFacing,
            bool isCurrentSupport,
            Transform attacker)
        {
            AttackId = attackId;
            Damage = damage;
            Direction = direction;
            AttackVector = attackVector;
            Origin = origin;
            AttackerFacing = attackerFacing < 0 ? -1 : 1;
            IsCurrentSupport = isCurrentSupport;
            Attacker = attacker;
        }

        public int AttackId { get; }
        public int Damage { get; }
        public PlayerAttackDirection Direction { get; }
        public Vector2 AttackVector { get; }
        public Vector2 Origin { get; }
        public int AttackerFacing { get; }
        public bool IsCurrentSupport { get; }
        public Transform Attacker { get; }
    }

    /// <summary>
    /// Optional player-specific attack path. Enemies that need armor or directional
    /// reactions can resolve the hit before the generic IDamageable fallback runs.
    /// </summary>
    public interface IPlayerAttackReceiver
    {
        PlayerAttackResolution ReceivePlayerAttack(PlayerAttackContext context);
    }

    /// <summary>
    /// Optional contact-damage gate for an enemy that was actually damaged by a
    /// downward player attack. This is deliberately scoped to the struck enemy.
    /// </summary>
    public interface IPlayerDownAttackContactSuppressor
    {
        void SuppressContactDamageForDownAttack(int attackId);
    }

    public interface IDamageable
    {
        void TakeDamage(int damage, Vector2 sourcePosition);
    }
}
