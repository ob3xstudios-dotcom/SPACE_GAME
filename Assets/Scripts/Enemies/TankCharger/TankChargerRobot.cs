using Game.Enemies.Combat;
using Game.Combat;
using Game.World;
using UnityEngine;

namespace Game.Enemies.TankCharger
{
    public enum TankChargerState
    {
        Patrolling,
        Windup,
        Charging,
        Recovery,
        BellyUp,
        Stunned
    }

    internal enum TankDetectionSource
    {
        None,
        Vision,
        Hearing
    }

    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(EnemySensors))]
    [RequireComponent(typeof(Health))]
    public sealed class TankChargerRobot : MonoBehaviour, IPlayerAttackReceiver
    {
        private const float ChargeTrackingRayEpsilon = 0.02f;
        private const float ChargeTrackingBehindTolerance = 0.05f;

        [Header("References")]
        [SerializeField] private EnemySensors sensors;
        [SerializeField] private EnemyAttackHitbox chargeHitbox;
        [SerializeField] private Transform groundProbe;
        [SerializeField] private Transform wallProbe;

        [Header("Patrol")]
        [SerializeField, Min(0f)] private float patrolDistance = 5f;
        [SerializeField, Min(0f)] private float patrolSpeed = 2f;
        [SerializeField, Min(0f)] private float patrolAcceleration = 16f;
        [SerializeField] private bool waitAtEnds = true;
        [SerializeField, Min(0f)] private float endWaitDuration = 0.4f;

        [Header("Navigation Safety")]
        [SerializeField] private LayerMask groundMask = 10312;
        [SerializeField] private LayerMask wallMask = 10312;
        [SerializeField, Min(0.02f)] private float groundProbeDistance = 0.45f;
        [SerializeField, Min(0f)] private float groundProbeForwardOffset = 0.12f;
        [SerializeField, Min(0.02f)] private float wallProbeDistance = 0.18f;

        [Header("Detection")]
        [SerializeField, Min(0f)] private float visionVerticalTolerance = 1.75f;
        [SerializeField, Min(0f)] private float visionMinimumHorizontalOffset = 0.35f;

        [Header("Hearing")]
        [SerializeField] private bool enableRearHearing = true;
        [SerializeField, Min(0f)] private float rearHearingRange = 2.5f;
        [SerializeField, Min(0f)] private float rearHearingVerticalTolerance = 1.25f;
        [SerializeField, Min(0f)] private float rearHearingMinimumHorizontalOffset = 0.35f;

        [Header("Charge")]
        [SerializeField, Min(0f)] private float windupDuration = 0.65f;
        [SerializeField, Min(0f)] private float chargeSpeed = 11f;
        [SerializeField, Min(0f)] private float chargeAcceleration = 35f;
        [SerializeField, Min(0f)] private float chargeDistance = 6f;
        [SerializeField, Min(0f)] private float recoveryDuration = 0.7f;
        [SerializeField, Min(0f)] private float chargeCooldown = 1.5f;
        [SerializeField, Min(0.1f)] private float maxChargePursuitDuration = 3f;

        [Header("Charge Damage")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private LayerMask playerMask = 512;
        [SerializeField] private Vector2 chargeHitboxOffset = new Vector2(0.85f, 0f);
        [SerializeField] private Vector2 chargeHitboxSize = new Vector2(0.7f, 0.9f);

        [Header("Hit Reaction")]
        [SerializeField, Min(0f)] private float hitStunDuration = 0.35f;
        [SerializeField, Min(0f)] private float hitKnockbackHorizontal = 3.5f;
        [SerializeField, Min(0f)] private float hitKnockbackVertical = 1.5f;

        [Header("Tank Vulnerability")]
        [SerializeField, Min(0.1f)] private float bellyUpDuration = 1f;
        [SerializeField, Min(0f)] private float bellyUpSeparationSpeed = 5f;
        [SerializeField, Min(0f)] private float bellyUpSeparationSafetyMargin = 0.6f;
        [SerializeField, Min(0.05f)] private float bellyUpSeparationTimeout = 0.75f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private Rigidbody2D body;
        private BoxCollider2D bodyCollider;
        private float patrolCenterX;
        private int patrolDirection = 1;
        private int facingDirection = 1;
        private int chargeDirection = 1;
        private float endWaitTimer;
        private float stateTimer;
        private float cooldownTimer;
        private float windupTargetX;
        private float chargeStartX;
        private float committedChargeStartX;
        private float chargeElapsedTime;
        private float lastVisibleChargeTargetX;
        private float currentChargeSpeed;
        private bool hasVisualChargeTarget;
        private bool hitLoggedThisCharge;
        private PlayerStandableSurface standableSurface;
        private Collider2D playerBodyCollider;
        private Health health;
        private PlayerAttackContext pendingPlayerAttack;
        private bool hasPendingPlayerAttack;
        private bool bellyUpDamageConsumed;
        private bool bellyUpSeparationActive;
        private int bellyUpSeparationDirection;
        private float bellyUpSeparationTargetX;
        private float bellyUpSeparationTimer;

        public TankChargerState CurrentState { get; private set; } = TankChargerState.Patrolling;
        public int FacingDirection => facingDirection;
        public float WindupProgress => CurrentState != TankChargerState.Windup
            ? 0f
            : windupDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / windupDuration);

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<BoxCollider2D>();
            standableSurface = GetComponent<PlayerStandableSurface>();
            health = GetComponent<Health>();
            if (sensors == null)
                sensors = GetComponent<EnemySensors>();

            body.bodyType = RigidbodyType2D.Dynamic;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            patrolCenterX = body.position.x;

            if (chargeHitbox != null)
            {
                chargeHitbox.Configure(transform, damage, playerMask, false);
                chargeHitbox.SetBox(chargeHitboxOffset, chargeHitboxSize);
                chargeHitbox.DisableHitbox();
            }
        }

        private void OnEnable()
        {
            if (health == null)
                health = GetComponent<Health>();
            if (health != null)
                health.OnDamaged += HandleDamaged;
        }

        private void Start()
        {
            sensors.ResolvePlayer();
            sensors.SetExternalFacingDirection(facingDirection);
        }

        private void Update()
        {
            if (cooldownTimer > 0f)
                cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);

            if (CurrentState == TankChargerState.Stunned)
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= hitStunDuration)
                    TransitionTo(TankChargerState.Recovery);
                return;
            }

            if (CurrentState == TankChargerState.BellyUp)
            {
                stateTimer += Time.deltaTime;
                if (stateTimer >= bellyUpDuration)
                    EndBellyUp("Timeout");
                return;
            }

            sensors.SetExternalFacingDirection(facingDirection);
            sensors.TickVision(body.position);

            if (!hitLoggedThisCharge && chargeHitbox != null && chargeHitbox.HitCountThisActivation > 0)
            {
                hitLoggedThisCharge = true;
                LogPlayerHit();
            }

            switch (CurrentState)
            {
                case TankChargerState.Patrolling:
                    if (cooldownTimer <= 0f &&
                        TryDetectPlayer(out TankDetectionSource patrolSource, out float patrolTargetX))
                    {
                        BeginWindup(patrolSource, patrolTargetX);
                    }
                    break;

                case TankChargerState.Windup:
                    stateTimer += Time.deltaTime;
                    UpdateWindupTarget();
                    if (stateTimer >= windupDuration)
                        BeginCharge();
                    break;

                case TankChargerState.Charging:
                    UpdateChargeVisualTarget();
                    break;

                case TankChargerState.Recovery:
                    stateTimer += Time.deltaTime;
                    if (stateTimer >= recoveryDuration)
                    {
                        if (cooldownTimer <= 0f &&
                            TryDetectPlayer(out TankDetectionSource recoverySource, out float recoveryTargetX))
                        {
                            BeginWindup(recoverySource, recoveryTargetX);
                        }
                        else
                            TransitionTo(TankChargerState.Patrolling);
                    }
                    break;
            }
        }

        private void FixedUpdate()
        {
            switch (CurrentState)
            {
                case TankChargerState.Patrolling:
                    UpdatePatrol();
                    break;
                case TankChargerState.Windup:
                case TankChargerState.Recovery:
                    StopHorizontal(chargeAcceleration);
                    break;
                case TankChargerState.BellyUp:
                    UpdateBellyUpMovement();
                    break;
                case TankChargerState.Charging:
                    UpdateCharge();
                    break;
                case TankChargerState.Stunned:
                    UpdateStunMovement();
                    break;
            }

            if (standableSurface != null)
            {
                standableSurface.SetMovementDelta(
                    Vector2.right * body.velocity.x * Time.fixedDeltaTime);
            }
        }

        private void UpdatePatrol()
        {
            if (patrolDistance <= 0f || patrolSpeed <= 0f)
            {
                StopHorizontal(patrolAcceleration);
                return;
            }

            if (endWaitTimer > 0f)
            {
                endWaitTimer = Mathf.Max(0f, endWaitTimer - Time.fixedDeltaTime);
                StopHorizontal(patrolAcceleration);
                return;
            }

            float halfDistance = patrolDistance * 0.5f;
            bool reachedEnd = patrolDirection > 0
                ? body.position.x >= patrolCenterX + halfDistance
                : body.position.x <= patrolCenterX - halfDistance;
            float projectedStep = patrolSpeed * Time.fixedDeltaTime;

            if (reachedEnd || HasWallAhead(patrolDirection, projectedStep) ||
                !HasGroundAhead(patrolDirection, projectedStep))
            {
                patrolDirection *= -1;
                SetFacing(patrolDirection);
                if (waitAtEnds)
                    endWaitTimer = endWaitDuration;
                StopHorizontal(patrolAcceleration);
                return;
            }

            SetFacing(patrolDirection);
            MoveHorizontal(patrolDirection * patrolSpeed, patrolAcceleration);
        }

        private void BeginWindup(TankDetectionSource source, float targetX)
        {
            Transform player = sensors.Player;
            windupTargetX = player != null ? targetX : body.position.x + facingDirection;
            SetFacing(DirectionTo(windupTargetX));
            TransitionTo(TankChargerState.Windup);
            LogDetected(source, player);
            LogWindup();
        }

        private void UpdateWindupTarget()
        {
            if (!TryGetPlayerBounds(out Bounds playerBounds) || IsPlayerStandingOnTank(playerBounds))
                return;

            windupTargetX = playerBounds.center.x;
            SetFacing(DirectionTo(windupTargetX));
        }

        private bool TryDetectPlayer(out TankDetectionSource source, out float targetX)
        {
            source = TankDetectionSource.None;
            targetX = body.position.x + facingDirection;
            if (sensors == null || sensors.Player == null ||
                !TryGetPlayerBounds(out Bounds playerBounds))
                return false;

            targetX = playerBounds.center.x;
            if (IsPlayerStandingOnTank(playerBounds))
                return false;

            Bounds tankBounds = bodyCollider.bounds;
            Vector2 relative = (Vector2)playerBounds.center - (Vector2)tankBounds.center;
            float forwardDistance = relative.x * facingDirection;

            if (sensors.HasVisualLineOfSight &&
                forwardDistance >= visionMinimumHorizontalOffset &&
                Mathf.Abs(relative.y) <= visionVerticalTolerance)
            {
                source = TankDetectionSource.Vision;
                return true;
            }

            float rearDistance = -forwardDistance;
            float feetDelta = Mathf.Abs(playerBounds.min.y - tankBounds.min.y);
            if (enableRearHearing &&
                rearDistance >= rearHearingMinimumHorizontalOffset &&
                rearDistance <= rearHearingRange &&
                feetDelta <= rearHearingVerticalTolerance &&
                sensors.CanHearPlayer(
                    body.position,
                    rearHearingRange + rearHearingVerticalTolerance))
            {
                source = TankDetectionSource.Hearing;
                return true;
            }

            return false;
        }

        private bool TryGetPlayerBounds(out Bounds playerBounds)
        {
            playerBounds = default;
            Transform player = sensors != null ? sensors.Player : null;
            if (player == null)
                return false;

            if (playerBodyCollider == null || !playerBodyCollider.enabled ||
                (playerBodyCollider.transform != player && !playerBodyCollider.transform.IsChildOf(player)))
            {
                playerBodyCollider = null;
                Collider2D[] colliders = player.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider2D candidate = colliders[i];
                    if (candidate == null || candidate.isTrigger || !candidate.enabled)
                        continue;

                    playerBodyCollider = candidate;
                    break;
                }
            }

            if (playerBodyCollider == null)
                return false;

            playerBounds = playerBodyCollider.bounds;
            return true;
        }

        private bool IsPlayerStandingOnTank(Bounds playerBounds)
        {
            const float topContactTolerance = 0.12f;
            Bounds tankBounds = bodyCollider.bounds;
            float horizontalOverlap = Mathf.Min(playerBounds.max.x, tankBounds.max.x) -
                                      Mathf.Max(playerBounds.min.x, tankBounds.min.x);
            bool overlapsBodyInX = horizontalOverlap > 0.01f;
            bool feetAtOrAboveTop = playerBounds.min.y >= tankBounds.max.y - topContactTolerance;
            return overlapsBodyInX && feetAtOrAboveTop &&
                   playerBounds.center.y > tankBounds.center.y;
        }

        private void BeginCharge()
        {
            chargeDirection = DirectionTo(windupTargetX);
            SetFacing(chargeDirection);
            chargeStartX = body.position.x;
            committedChargeStartX = chargeStartX;
            chargeElapsedTime = 0f;
            hasVisualChargeTarget = TryGetChargeTrackingTarget(out Bounds playerBounds);
            lastVisibleChargeTargetX = hasVisualChargeTarget
                ? playerBounds.center.x
                : windupTargetX;
            currentChargeSpeed = 0f;
            hitLoggedThisCharge = false;

            if (chargeHitbox != null)
            {
                Vector2 offset = chargeHitboxOffset;
                offset.x = Mathf.Abs(offset.x) * chargeDirection;
                chargeHitbox.SetBox(offset, chargeHitboxSize);
                chargeHitbox.Activate(Vector2.right * chargeDirection);
            }

            TransitionTo(TankChargerState.Charging);
            LogChargeStart();
        }

        private void UpdateChargeVisualTarget()
        {
            if (!hasVisualChargeTarget)
                return;

            if (TryGetChargeTrackingTarget(out Bounds playerBounds))
            {
                lastVisibleChargeTargetX = playerBounds.center.x;
                return;
            }

            hasVisualChargeTarget = false;
            committedChargeStartX = body.position.x;
            LogChargeLostVisualTarget();
            LogChargeCommitted();
        }

        private bool TryGetChargeTrackingTarget(out Bounds playerBounds)
        {
            playerBounds = default;
            if (!TryGetPlayerBounds(out playerBounds))
                return false;

            Bounds tankBounds = bodyCollider.bounds;
            float forwardDistance =
                (playerBounds.center.x - tankBounds.center.x) * chargeDirection;
            if (forwardDistance < -ChargeTrackingBehindTolerance)
                return false;

            Vector2 rayOrigin = tankBounds.center;
            Vector2 playerCenter = playerBounds.center;
            Vector2 toPlayer = playerCenter - rayOrigin;
            float distance = toPlayer.magnitude;
            if (distance <= ChargeTrackingRayEpsilon)
                return true;

            RaycastHit2D obstacle = Physics2D.Raycast(
                rayOrigin,
                toPlayer / distance,
                distance - ChargeTrackingRayEpsilon,
                wallMask);
            return obstacle.collider == null;
        }

        private void UpdateCharge()
        {
            float nextSpeed = Mathf.MoveTowards(
                currentChargeSpeed,
                chargeSpeed,
                chargeAcceleration * Time.fixedDeltaTime);
            float projectedStep = nextSpeed * Time.fixedDeltaTime;

            if (HasWallAhead(chargeDirection, projectedStep))
            {
                EndCharge("Wall");
                return;
            }

            if (!HasGroundAhead(chargeDirection, projectedStep))
            {
                EndCharge("Ledge");
                return;
            }

            chargeElapsedTime += Time.fixedDeltaTime;
            if (chargeElapsedTime >= maxChargePursuitDuration)
            {
                EndCharge("Safety");
                return;
            }

            if (!hasVisualChargeTarget)
            {
                float committedDistance =
                    Mathf.Abs(body.position.x - committedChargeStartX);
                if (committedDistance >= chargeDistance)
                {
                    EndCharge("LostTargetDistance");
                    return;
                }
            }

            currentChargeSpeed = nextSpeed;
            body.velocity = new Vector2(chargeDirection * currentChargeSpeed, body.velocity.y);
        }

        private void EndCharge(string reason)
        {
            if (CurrentState != TankChargerState.Charging)
                return;

            if (chargeHitbox != null)
                chargeHitbox.DisableHitbox();

            currentChargeSpeed = 0f;
            body.velocity = new Vector2(0f, body.velocity.y);
            cooldownTimer = chargeCooldown;
            TransitionTo(TankChargerState.Recovery);
            LogChargeEnd(reason);
        }

        private void MoveHorizontal(float targetSpeed, float acceleration)
        {
            float nextSpeed = Mathf.MoveTowards(
                body.velocity.x,
                targetSpeed,
                acceleration * Time.fixedDeltaTime);
            body.velocity = new Vector2(nextSpeed, body.velocity.y);
        }

        private void StopHorizontal(float acceleration)
        {
            MoveHorizontal(0f, acceleration);
        }

        private void UpdateStunMovement()
        {
            float deceleration = hitStunDuration > 0f
                ? hitKnockbackHorizontal / hitStunDuration
                : hitKnockbackHorizontal;
            body.velocity = new Vector2(
                Mathf.MoveTowards(body.velocity.x, 0f, deceleration * Time.fixedDeltaTime),
                body.velocity.y);
        }

        private void UpdateBellyUpMovement()
        {
            if (bellyUpSeparationActive)
            {
                bellyUpSeparationTimer += Time.fixedDeltaTime;
                float signedRemaining =
                    (bellyUpSeparationTargetX - body.position.x) * bellyUpSeparationDirection;
                float projectedStep = bellyUpSeparationSpeed * Time.fixedDeltaTime;
                if (signedRemaining <= 0.02f ||
                    HasWallAhead(bellyUpSeparationDirection, projectedStep) ||
                    !HasGroundAhead(bellyUpSeparationDirection, projectedStep) ||
                    bellyUpSeparationTimer >= bellyUpSeparationTimeout)
                {
                    bellyUpSeparationActive = false;
                    body.velocity = new Vector2(0f, body.velocity.y);
                }
                else
                {
                    body.velocity = new Vector2(
                        bellyUpSeparationDirection * bellyUpSeparationSpeed,
                        body.velocity.y);
                    return;
                }
            }

            float deceleration = hitStunDuration > 0f
                ? bellyUpSeparationSpeed / hitStunDuration
                : bellyUpSeparationSpeed;
            body.velocity = new Vector2(
                Mathf.MoveTowards(body.velocity.x, 0f, deceleration * Time.fixedDeltaTime),
                body.velocity.y);
        }

        public PlayerAttackResolution ReceivePlayerAttack(PlayerAttackContext context)
        {
            if (health == null || health.CurrentHP <= 0)
                return PlayerAttackResolution.None;

            if (CurrentState == TankChargerState.BellyUp)
            {
                if (bellyUpDamageConsumed)
                    return PlayerAttackResolution.Armored;

                bellyUpDamageConsumed = true;
                int healthBefore = health.CurrentHP;
                pendingPlayerAttack = context;
                hasPendingPlayerAttack = true;
                try
                {
                    // Tank armor always converts the vulnerable follow-up to one HP,
                    // independently from future player damage upgrades.
                    health.TakeDamage(1, context.Origin);
                }
                finally
                {
                    hasPendingPlayerAttack = false;
                }

                int healthAfter = health.CurrentHP;
                bool applied = healthAfter < healthBefore;
                LogBellyUpDamage(healthBefore, healthAfter);
                return applied ? PlayerAttackResolution.Damage : PlayerAttackResolution.None;
            }

            if (context.Direction == PlayerAttackDirection.Down && context.IsCurrentSupport)
            {
                BeginBellyUp(context);
                return PlayerAttackResolution.BellyUp;
            }

            LogArmoredHit(context.Direction);
            return PlayerAttackResolution.Armored;
        }

        private void BeginBellyUp(PlayerAttackContext context)
        {
            if (chargeHitbox != null)
                chargeHitbox.DisableHitbox();

            currentChargeSpeed = 0f;
            cooldownTimer = Mathf.Max(cooldownTimer, chargeCooldown);
            bellyUpDamageConsumed = false;
            Collider2D playerCollider = context.Attacker != null
                ? context.Attacker.GetComponent<Collider2D>()
                : null;
            float playerHalfWidth = playerCollider != null
                ? playerCollider.bounds.extents.x
                : 0.4f;
            float playerCenterX = playerCollider != null
                ? playerCollider.bounds.center.x
                : context.Origin.x;
            float tankHalfWidth = bodyCollider != null
                ? bodyCollider.bounds.extents.x
                : 0.75f;
            float bodyCenterX = bodyCollider != null
                ? bodyCollider.bounds.center.x
                : body.position.x;
            float awayFromAttacker = bodyCenterX - playerCenterX;
            int separationDirection = Mathf.Abs(awayFromAttacker) > 0.1f
                ? (awayFromAttacker < 0f ? -1 : 1)
                : -context.AttackerFacing;
            float requiredSeparation =
                playerHalfWidth + tankHalfWidth + bellyUpSeparationSafetyMargin;
            float targetBodyCenterX =
                playerCenterX + separationDirection * requiredSeparation;
            bellyUpSeparationDirection = separationDirection;
            bellyUpSeparationTargetX =
                body.position.x + (targetBodyCenterX - bodyCenterX);
            bellyUpSeparationTimer = 0f;
            bellyUpSeparationActive = true;
            body.velocity = new Vector2(
                separationDirection * bellyUpSeparationSpeed,
                body.velocity.y);
            TransitionTo(TankChargerState.BellyUp);
            LogBellyUpStart();
        }

        private void EndBellyUp(string reason)
        {
            if (CurrentState != TankChargerState.BellyUp)
                return;

            bellyUpDamageConsumed = true;
            bellyUpSeparationActive = false;
            cooldownTimer = Mathf.Max(cooldownTimer, chargeCooldown);
            TransitionTo(TankChargerState.Recovery);
            if (reason == "Timeout")
                LogBellyUpExpired();
            LogBellyUpEnd(reason);
        }

        private void HandleDamaged(Health damagedHealth, int receivedDamage, Vector2 sourcePosition)
        {
            if (damagedHealth == null)
                return;

            TankChargerState interruptedState = CurrentState;
            LogHit(damagedHealth.CurrentHP, interruptedState);

            if (chargeHitbox != null)
                chargeHitbox.DisableHitbox();

            currentChargeSpeed = 0f;
            bellyUpSeparationActive = false;
            cooldownTimer = Mathf.Max(cooldownTimer, chargeCooldown);
            if (interruptedState == TankChargerState.BellyUp)
                LogBellyUpEnd(damagedHealth.CurrentHP <= 0 ? "Death" : "Hit");
            TransitionTo(TankChargerState.Stunned);
            body.velocity = GetHitKnockbackVelocity(sourcePosition);
        }

        private Vector2 GetHitKnockbackVelocity(Vector2 sourcePosition)
        {
            Vector2 away = body.position - sourcePosition;
            bool sourceAbove = sourcePosition.y > body.position.y + 0.1f;
            float horizontalDirection = Mathf.Abs(away.x) > 0.1f
                ? Mathf.Sign(away.x)
                : hasPendingPlayerAttack
                    ? pendingPlayerAttack.AttackerFacing
                    : facingDirection;
            float horizontal = !sourceAbove || hasPendingPlayerAttack
                ? horizontalDirection * hitKnockbackHorizontal
                : 0f;
            float vertical = sourceAbove
                ? -hitKnockbackVertical
                : hitKnockbackVertical;
            return new Vector2(horizontal, vertical);
        }

        private bool HasGroundAhead(int direction, float projectedStep)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 origin = groundProbe != null
                ? groundProbe.position
                : new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            origin.x += direction * (bounds.extents.x + groundProbeForwardOffset + projectedStep);
            return Physics2D.Raycast(origin, Vector2.down, groundProbeDistance, groundMask).collider != null;
        }

        private bool HasWallAhead(int direction, float projectedStep)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 origin = wallProbe != null
                ? wallProbe.position
                : bounds.center;
            origin.x += direction * bounds.extents.x;
            float distance = wallProbeDistance + projectedStep;
            Vector2 probeSize = new Vector2(0.05f, Mathf.Max(0.1f, bounds.size.y * 0.75f));
            return Physics2D.BoxCast(
                origin,
                probeSize,
                0f,
                Vector2.right * direction,
                distance,
                wallMask).collider != null;
        }

        private int DirectionTo(float targetX)
        {
            float delta = targetX - body.position.x;
            if (Mathf.Abs(delta) <= 0.01f)
                return facingDirection;
            return delta < 0f ? -1 : 1;
        }

        private void SetFacing(int direction)
        {
            if (direction == 0)
                return;

            facingDirection = direction < 0 ? -1 : 1;
            sensors.SetExternalFacingDirection(facingDirection);
        }

        private void TransitionTo(TankChargerState nextState)
        {
            CurrentState = nextState;
            stateTimer = 0f;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (CurrentState != TankChargerState.Charging || collision == null)
                return;

            int collisionLayer = 1 << collision.gameObject.layer;
            if ((wallMask.value & collisionLayer) == 0)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                if (Mathf.Abs(contact.normal.x) > 0.5f)
                {
                    EndCharge("Wall");
                    return;
                }
            }
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDamaged -= HandleDamaged;
            if (chargeHitbox != null)
                chargeHitbox.DisableHitbox();
            if (standableSurface != null)
                standableSurface.SetMovementDelta(Vector2.zero);
        }

        private void OnValidate()
        {
            patrolDistance = Mathf.Max(0f, patrolDistance);
            patrolSpeed = Mathf.Max(0f, patrolSpeed);
            patrolAcceleration = Mathf.Max(0f, patrolAcceleration);
            endWaitDuration = Mathf.Max(0f, endWaitDuration);
            groundProbeDistance = Mathf.Max(0.02f, groundProbeDistance);
            groundProbeForwardOffset = Mathf.Max(0f, groundProbeForwardOffset);
            wallProbeDistance = Mathf.Max(0.02f, wallProbeDistance);
            visionVerticalTolerance = Mathf.Max(0f, visionVerticalTolerance);
            visionMinimumHorizontalOffset = Mathf.Max(0f, visionMinimumHorizontalOffset);
            rearHearingRange = Mathf.Max(0f, rearHearingRange);
            rearHearingVerticalTolerance = Mathf.Max(0f, rearHearingVerticalTolerance);
            rearHearingMinimumHorizontalOffset = Mathf.Max(0f, rearHearingMinimumHorizontalOffset);
            windupDuration = Mathf.Max(0f, windupDuration);
            chargeSpeed = Mathf.Max(0f, chargeSpeed);
            chargeAcceleration = Mathf.Max(0f, chargeAcceleration);
            chargeDistance = Mathf.Max(0f, chargeDistance);
            recoveryDuration = Mathf.Max(0f, recoveryDuration);
            chargeCooldown = Mathf.Max(0f, chargeCooldown);
            maxChargePursuitDuration = Mathf.Max(0.1f, maxChargePursuitDuration);
            damage = 1;
            chargeHitboxSize.x = Mathf.Max(0.01f, chargeHitboxSize.x);
            chargeHitboxSize.y = Mathf.Max(0.01f, chargeHitboxSize.y);
            hitStunDuration = Mathf.Max(0f, hitStunDuration);
            hitKnockbackHorizontal = Mathf.Max(0f, hitKnockbackHorizontal);
            hitKnockbackVertical = Mathf.Max(0f, hitKnockbackVertical);
            bellyUpDuration = Mathf.Max(0.1f, bellyUpDuration);
            bellyUpSeparationSpeed = Mathf.Max(0f, bellyUpSeparationSpeed);
            bellyUpSeparationSafetyMargin = Mathf.Max(0f, bellyUpSeparationSafetyMargin);
            bellyUpSeparationTimeout = Mathf.Max(0.05f, bellyUpSeparationTimeout);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDetected(TankDetectionSource source, Transform player)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[TANK][DETECT] source={source} player={(player != null ? player.name : "None")}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogWindup()
        {
            if (debugLogs)
                Debug.Log($"[TANK][WINDUP] targetDirection={facingDirection}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeStart()
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[TANK][CHARGE START] direction={chargeDirection} " +
                    $"tracking={hasVisualChargeTarget} maxDistance={chargeDistance:0.###} " +
                    $"maxDuration={maxChargePursuitDuration:0.###}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeLostVisualTarget()
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[TANK][CHARGE LOST LOS] playerX={lastVisibleChargeTargetX:0.###} " +
                    $"tankX={body.position.x:0.###} lastDirection={chargeDirection}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeCommitted()
        {
            if (debugLogs)
                Debug.Log("[TANK][CHARGE COMMITTED]", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeEnd(string reason)
        {
            if (debugLogs)
                Debug.Log($"[TANK][CHARGE END] reason={reason}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlayerHit()
        {
            if (debugLogs)
                Debug.Log("[TANK][HIT PLAYER]", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogHit(int remainingHealth, TankChargerState interruptedState)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[TANK][HIT] hp={remainingHealth} state={interruptedState} " +
                    $"stun={hitStunDuration:0.###}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogArmoredHit(PlayerAttackDirection direction)
        {
            if (debugLogs)
                Debug.Log($"[TANK HIT ARMORED] direction={direction}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBellyUpStart()
        {
            if (debugLogs)
                Debug.Log($"[TANK BELLY UP START] duration={bellyUpDuration:0.###}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBellyUpDamage(int healthBefore, int healthAfter)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[TANK BELLY UP DAMAGE] hpBefore={healthBefore} hpAfter={healthAfter}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBellyUpExpired()
        {
            if (debugLogs)
                Debug.Log("[TANK BELLY UP EXPIRED]", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBellyUpEnd(string reason)
        {
            if (debugLogs)
                Debug.Log($"[TANK BELLY UP END] reason={reason}", this);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 center = Application.isPlaying && body != null
                ? new Vector2(patrolCenterX, transform.position.y)
                : (Vector2)transform.position;
            float halfDistance = Mathf.Max(0f, patrolDistance) * 0.5f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(center + Vector2.left * halfDistance, center + Vector2.right * halfDistance);
            Gizmos.DrawWireSphere(center + Vector2.left * halfDistance, 0.1f);
            Gizmos.DrawWireSphere(center + Vector2.right * halfDistance, 0.1f);

            float detectionRange = sensors != null ? sensors.ViewDistance : 0f;
            if (detectionRange > 0f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, detectionRange);
            }

            Bounds tankDetectionBounds = bodyCollider != null
                ? bodyCollider.bounds
                : new Bounds(transform.position, new Vector3(1.8f, 1.1f, 0f));
            int detectionDirection = Application.isPlaying ? facingDirection : 1;
            Vector3 rearCenter = tankDetectionBounds.center +
                                 Vector3.left * detectionDirection * rearHearingRange * 0.5f;
            Gizmos.color = new Color(0.75f, 0.25f, 1f, 0.85f);
            Gizmos.DrawWireCube(
                rearCenter,
                new Vector3(rearHearingRange, rearHearingVerticalTolerance * 2f, 0f));

            int direction = Application.isPlaying ? facingDirection : 1;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.right * direction * chargeDistance);

            Bounds bounds = bodyCollider != null
                ? bodyCollider.bounds
                : new Bounds(transform.position, new Vector3(1.8f, 1.1f, 0f));
            Vector2 groundOrigin = groundProbe != null
                ? groundProbe.position
                : new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            groundOrigin.x += direction * (bounds.extents.x + groundProbeForwardOffset);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(groundOrigin, groundOrigin + Vector2.down * groundProbeDistance);

            Vector2 wallOrigin = wallProbe != null ? wallProbe.position : (Vector2)bounds.center;
            wallOrigin.x += direction * bounds.extents.x;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(wallOrigin, wallOrigin + Vector2.right * direction * wallProbeDistance);
        }
    }
}
