using UnityEngine;

namespace Game.Enemies.States
{
    /// <summary>
    /// Estado de stun “real” para el enemigo (bloquea movimiento durante X segundos).
    /// Compatible con vuestro sistema: IEnemyState + EnemyBase.SetState().
    /// </summary>
    public class EnemyStunnedState : Game.Enemies.IEnemyState
    {
        private readonly float stunSeconds;
        private readonly float decel;

        private float timer;

        /// <param name="seconds">Duración del stun</param>
        /// <param name="stopDecel">Frenada mientras está stuneado</param>
        public EnemyStunnedState(float seconds, float stopDecel = 60f)
        {
            stunSeconds = Mathf.Max(0f, seconds);
            decel = Mathf.Max(0f, stopDecel);
        }

        public void Enter(EnemyBase enemy)
        {
            timer = stunSeconds;

            // seguridad: no parryable en stun
            enemy.SetParryable(false);

            // opcional: frenar de golpe
            enemy.StopSmooth(decel);

            // Si queréis animación “Stunned”, poned un bool en vuestro Animator locomotion:
            // enemy.MeleeDriver/anim etc. no es público aquí, así que normalmente lo controlaríais
            // con otro driver. (Si me dices dónde está el Animator, lo conecto).
        }

        public void Tick(EnemyBase enemy)
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;

            // Al terminar: vuelve a persecución si tiene memoria, si no vuelve a patrulla.
            if (enemy.CanSeePlayer() || enemy.HasTargetInMemory)
                enemy.SetState(new EnemyChaseState());
            else
                enemy.SetState(new EnemyReturnToPatrolState());
        }

        public void FixedTick(EnemyBase enemy)
        {
            // mientras stuneado: frena hasta parar
            enemy.StopSmooth(decel);
        }

        public void Exit(EnemyBase enemy)
        {
            // nada
        }
    }
}
