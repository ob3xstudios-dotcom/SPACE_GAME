using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerParkourFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private CapsuleCollider2D capsule;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer[] visualRenderers;
        [SerializeField] private AudioSource audioSource;

        [Header("Particles")]
        [SerializeField] private ParticleSystem wallSlideDust;
        [SerializeField] private ParticleSystem wallJumpBurst;
        [SerializeField] private ParticleSystem landBurst;
        [SerializeField] private bool createDefaultParticles = true;

        [Header("Audio Hooks")]
        [SerializeField] private AudioClip wallSlideStartClip;
        [SerializeField] private AudioClip wallJumpClip;
        [SerializeField] private AudioClip landClip;
        [SerializeField, Range(0f, 1f)] private float wallSlideStartVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] private float wallJumpVolume = 0.75f;
        [SerializeField, Range(0f, 1f)] private float landVolume = 0.55f;

        [Header("Landing")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 6f;
        [SerializeField, Min(0f)] private float hardLandingSpeed = 14f;

        [Header("Wall Slide Visual")]
        [SerializeField] private bool enableWallSlideLean = true;
        [SerializeField, Range(0f, 12f)] private float wallSlideLeanDegrees = 5f;
        [SerializeField, Range(1f, 30f)] private float wallSlideLeanLerp = 14f;
        [SerializeField] private bool enableWallSlideTint = true;
        [SerializeField] private Color wallSlideTint = new Color(0.78f, 0.92f, 1f, 1f);
        [SerializeField, Range(1f, 30f)] private float wallSlideTintLerp = 18f;

        private bool wasGrounded;
        private bool wasWallSliding;
        private float lastAirDownSpeed;
        private Quaternion visualBaseRotation;
        private Color[] visualBaseColors;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (capsule == null) capsule = GetComponent<CapsuleCollider2D>();
            Transform leanRoot = visualRoot;
            if (leanRoot == null)
            {
                SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
                if (sprite != null && sprite.transform != transform)
                    leanRoot = sprite.transform;
            }

            if (leanRoot == transform)
                leanRoot = null;

            visualRoot = leanRoot;
            if (visualRoot == null)
                enableWallSlideLean = false;

            if (visualRenderers == null || visualRenderers.Length == 0)
                visualRenderers = GetComponentsInChildren<SpriteRenderer>();

            visualBaseColors = new Color[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
                visualBaseColors[i] = visualRenderers[i] != null ? visualRenderers[i].color : Color.white;

            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

            if (visualRoot != null)
            {
                try
                {
                    visualBaseRotation = visualRoot.localRotation;
                }
                catch (UnassignedReferenceException)
                {
                    visualRoot = null;
                    enableWallSlideLean = false;
                }
            }
            else
            {
                enableWallSlideLean = false;
            }

            if (createDefaultParticles)
            {
                if (wallSlideDust == null) wallSlideDust = CreateParticleSystem("WallSlideDust", true);
                if (wallJumpBurst == null) wallJumpBurst = CreateParticleSystem("WallJumpBurst", false);
                if (landBurst == null) landBurst = CreateParticleSystem("LandBurst", false);
            }
        }

        private void OnEnable()
        {
            if (controller != null)
                controller.WallJumped += OnWallJumped;
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.WallJumped -= OnWallJumped;

            RestoreVisuals();
        }

        private void Update()
        {
            if (controller == null || rb == null) return;

            bool grounded = controller.IsGrounded;
            bool wallSliding = controller.IsWallSliding;

            if (!grounded)
                lastAirDownSpeed = Mathf.Max(lastAirDownSpeed, -rb.velocity.y);

            if (wallSliding && !wasWallSliding)
                OnWallSlideStarted();
            else if (!wallSliding && wasWallSliding)
                OnWallSlideStopped();

            if (!wasGrounded && grounded)
                OnLanded(lastAirDownSpeed);

            UpdateWallSlideDust(wallSliding);
            UpdateWallSlideLean(wallSliding);
            UpdateWallSlideTint(wallSliding);

            if (grounded)
                lastAirDownSpeed = 0f;

            wasGrounded = grounded;
            wasWallSliding = wallSliding;
        }

        private void OnWallSlideStarted()
        {
            PlayOneShot(wallSlideStartClip, wallSlideStartVolume);
            if (wallSlideDust != null && !wallSlideDust.isPlaying)
                wallSlideDust.Play();
        }

        private void OnWallSlideStopped()
        {
            if (wallSlideDust != null)
                wallSlideDust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnWallJumped(int jumpedWallSide)
        {
            PositionAtWallSide(wallJumpBurst, jumpedWallSide, 0.05f);
            if (wallJumpBurst != null)
                wallJumpBurst.Play();

            PlayOneShot(wallJumpClip, wallJumpVolume);
        }

        private void OnLanded(float downSpeed)
        {
            if (downSpeed < minLandingSpeed) return;

            PositionAtFeet(landBurst);
            if (landBurst != null)
                landBurst.Play();

            PlayOneShot(landClip, landVolume);
        }

        private void UpdateWallSlideDust(bool wallSliding)
        {
            if (!wallSliding || wallSlideDust == null) return;

            PositionAtWallSide(wallSlideDust, controller.WallSide, -0.25f);
            if (!wallSlideDust.isPlaying)
                wallSlideDust.Play();
        }

        private void UpdateWallSlideLean(bool wallSliding)
        {
            if (!enableWallSlideLean || visualRoot == null) return;

            float targetZ = wallSliding ? -controller.WallSide * wallSlideLeanDegrees : 0f;
            Quaternion target = visualBaseRotation * Quaternion.Euler(0f, 0f, targetZ);
            visualRoot.localRotation = Quaternion.Slerp(
                visualRoot.localRotation,
                target,
                1f - Mathf.Exp(-wallSlideLeanLerp * Time.deltaTime)
            );
        }

        private void UpdateWallSlideTint(bool wallSliding)
        {
            if (!enableWallSlideTint || visualRenderers == null || visualBaseColors == null) return;

            float t = 1f - Mathf.Exp(-wallSlideTintLerp * Time.deltaTime);
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                SpriteRenderer renderer = visualRenderers[i];
                if (renderer == null) continue;

                Color target = wallSliding ? MultiplyColor(visualBaseColors[i], wallSlideTint) : visualBaseColors[i];
                target.a = visualBaseColors[i].a;
                renderer.color = Color.Lerp(renderer.color, target, t);
            }
        }

        private static Color MultiplyColor(Color a, Color b)
        {
            return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        }

        private void PositionAtWallSide(ParticleSystem system, int side, float yOffset)
        {
            if (system == null || capsule == null) return;
            if (side == 0) side = controller != null && controller.FacingLeft ? -1 : 1;

            Bounds b = capsule.bounds;
            system.transform.position = new Vector3(
                b.center.x + b.extents.x * side,
                b.center.y + yOffset,
                transform.position.z
            );
            system.transform.rotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
        }

        private void PositionAtFeet(ParticleSystem system)
        {
            if (system == null || capsule == null) return;

            Bounds b = capsule.bounds;
            system.transform.position = new Vector3(b.center.x, b.min.y + 0.05f, transform.position.z);
            system.transform.rotation = Quaternion.identity;
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (clip == null || audioSource == null || volume <= 0f) return;
            audioSource.PlayOneShot(clip, volume);
        }

        private void RestoreVisuals()
        {
            if (visualRoot != null)
                visualRoot.localRotation = visualBaseRotation;

            if (visualRenderers == null || visualBaseColors == null) return;
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] != null)
                    visualRenderers[i].color = visualBaseColors[i];
            }
        }

        private ParticleSystem CreateParticleSystem(string objectName, bool looping)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = looping;
            main.startLifetime = looping ? 0.22f : 0.28f;
            main.startSpeed = looping ? 1.2f : 2.4f;
            main.startSize = looping ? 0.055f : 0.11f;
            main.maxParticles = looping ? 40 : 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = looping ? 18f : 0f;
            emission.SetBursts(looping
                ? new ParticleSystem.Burst[0]
                : new[] { new ParticleSystem.Burst(0f, (short)12) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = looping ? 18f : 35f;
            shape.radius = looping ? 0.04f : 0.1f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.82f, 0.78f, 0.68f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.52f, 0.45f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.75f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            return system;
        }
    }
}
