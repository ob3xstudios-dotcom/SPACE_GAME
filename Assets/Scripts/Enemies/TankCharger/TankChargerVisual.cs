using UnityEngine;

namespace Game.Enemies.TankCharger
{
    public sealed class TankChargerVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TankChargerRobot owner;
        [SerializeField] private Transform upperExhaust;
        [SerializeField] private Transform lowerExhaust;

        [Header("Rendering")]
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int baseSortingOrder = 10;

        [Header("Colors")]
        [SerializeField] private Color bodyColor = new Color(0.22f, 0.28f, 0.32f, 1f);
        [SerializeField] private Color trackColor = new Color(0.08f, 0.1f, 0.12f, 1f);
        [SerializeField] private Color patrolEyeColor = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private Color windupEyeColor = new Color(1f, 0.65f, 0.08f, 1f);
        [SerializeField] private Color chargeEyeColor = new Color(1f, 0.1f, 0.04f, 1f);
        [SerializeField] private Color recoveryEyeColor = new Color(0.3f, 0.55f, 0.9f, 1f);

        private Texture2D solidTexture;
        private Texture2D circleTexture;
        private Sprite solidSprite;
        private Sprite circleSprite;
        private Material spriteMaterial;
        private Material smokeMaterial;
        private SpriteRenderer eyeRenderer;
        private ParticleSystem upperSmoke;
        private ParticleSystem lowerSmoke;
        private TankChargerState previousState;
        private Vector3 baseScale;
        private Quaternion baseLocalRotation;

        private void Awake()
        {
            if (owner == null)
                owner = GetComponentInParent<TankChargerRobot>();

            baseScale = transform.localScale;
            baseLocalRotation = transform.localRotation;
            EnsureVisual();
            previousState = owner != null ? owner.CurrentState : TankChargerState.Patrolling;
        }

        private void OnEnable()
        {
            EnsureVisual();
        }

        private void LateUpdate()
        {
            if (owner == null)
                return;

            int facing = owner.FacingDirection;
            transform.localScale = new Vector3(
                Mathf.Abs(baseScale.x) * facing,
                baseScale.y,
                baseScale.z);
            transform.localRotation = owner.CurrentState == TankChargerState.BellyUp
                ? baseLocalRotation * Quaternion.Euler(0f, 0f, 180f)
                : baseLocalRotation;

            UpdateEye();
            UpdateSmoke();
            previousState = owner.CurrentState;
        }

        private void EnsureVisual()
        {
            EnsureRuntimeResources();

            EnsurePart("Tracks", new Vector3(0f, -0.38f, 0f), new Vector3(1.9f, 0.42f, 1f),
                trackColor, baseSortingOrder, solidSprite);
            EnsurePart("Body", new Vector3(0f, 0.02f, 0f), new Vector3(1.65f, 0.78f, 1f),
                bodyColor, baseSortingOrder + 2, solidSprite);
            EnsurePart("Turret", new Vector3(0.15f, 0.46f, 0f), new Vector3(1.05f, 0.32f, 1f),
                bodyColor * 1.2f, baseSortingOrder + 3, solidSprite);

            EnsurePart("WheelRear", new Vector3(-0.55f, -0.38f, 0f), Vector3.one * 0.38f,
                new Color(0.2f, 0.24f, 0.27f, 1f), baseSortingOrder + 1, circleSprite);
            EnsurePart("WheelCenter", new Vector3(0f, -0.38f, 0f), Vector3.one * 0.42f,
                new Color(0.2f, 0.24f, 0.27f, 1f), baseSortingOrder + 1, circleSprite);
            EnsurePart("WheelFront", new Vector3(0.55f, -0.38f, 0f), Vector3.one * 0.38f,
                new Color(0.2f, 0.24f, 0.27f, 1f), baseSortingOrder + 1, circleSprite);

            eyeRenderer = EnsurePart("Eye", new Vector3(0.58f, 0.47f, 0f), new Vector3(0.25f, 0.18f, 1f),
                patrolEyeColor, baseSortingOrder + 4, circleSprite);

            upperExhaust = EnsureTransform(upperExhaust, "UpperExhaust", new Vector3(-0.82f, 0.35f, 0f));
            lowerExhaust = EnsureTransform(lowerExhaust, "LowerExhaust", new Vector3(-0.82f, 0.08f, 0f));
            upperSmoke = EnsureSmokeSystem(upperExhaust);
            lowerSmoke = EnsureSmokeSystem(lowerExhaust);
        }

        private void EnsureRuntimeResources()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Tank Charger Solid Texture Runtime",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                solidTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                solidTexture.Apply(false, true);
            }

            if (circleTexture == null)
                circleTexture = BuildCircleTexture(48);

            if (solidSprite == null)
                solidSprite = CreateSprite(solidTexture, "Tank Charger Solid Sprite Runtime");
            if (circleSprite == null)
                circleSprite = CreateSprite(circleTexture, "Tank Charger Circle Sprite Runtime");

            if (spriteMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                    spriteMaterial = CreateMaterial(shader, "Tank Charger Sprite Material Runtime", solidTexture);
            }

            if (smokeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                                Shader.Find("Particles/Standard Unlit") ??
                                Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default");
                if (shader != null)
                    smokeMaterial = CreateMaterial(shader, "Tank Charger Smoke Material Runtime", circleTexture);
            }
        }

        private SpriteRenderer EnsurePart(
            string partName,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            int sortingOrder,
            Sprite sprite)
        {
            Transform part = transform.Find(partName);
            if (part == null)
            {
                GameObject partObject = new GameObject(partName);
                partObject.transform.SetParent(transform, false);
                part = partObject.transform;
            }

            part.gameObject.layer = gameObject.layer;
            part.localPosition = localPosition;
            part.localRotation = Quaternion.identity;
            part.localScale = localScale;

            SpriteRenderer renderer = part.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = part.gameObject.AddComponent<SpriteRenderer>();

            renderer.enabled = true;
            renderer.sprite = sprite;
            if (spriteMaterial != null)
                renderer.sharedMaterial = spriteMaterial;
            renderer.color = color;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private Transform EnsureTransform(Transform existing, string objectName, Vector3 localPosition)
        {
            if (existing == null)
            {
                existing = transform.Find(objectName);
                if (existing == null)
                {
                    GameObject newObject = new GameObject(objectName);
                    newObject.transform.SetParent(transform, false);
                    existing = newObject.transform;
                }
            }

            existing.gameObject.layer = gameObject.layer;
            existing.localPosition = localPosition;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        private ParticleSystem EnsureSmokeSystem(Transform exhaust)
        {
            ParticleSystem smoke = exhaust.GetComponent<ParticleSystem>();
            if (smoke == null)
                smoke = exhaust.gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = smoke.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.28f, 0.3f, 0.32f, 0.8f),
                new Color(0.65f, 0.68f, 0.7f, 0.55f));
            main.maxParticles = 64;

            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.enabled = false;

            ParticleSystem.VelocityOverLifetimeModule velocity = smoke.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.45f, -0.18f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);

            ParticleSystem.SizeOverLifetimeModule size = smoke.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 1.35f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule color = smoke.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.7f, 0.72f, 0.75f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = fade;

            ParticleSystemRenderer renderer = smoke.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = true;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = baseSortingOrder - 1;
            if (smokeMaterial != null)
                renderer.sharedMaterial = smokeMaterial;

            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return smoke;
        }

        private void UpdateEye()
        {
            if (eyeRenderer == null)
                return;

            switch (owner.CurrentState)
            {
                case TankChargerState.Windup:
                    float progress = owner.WindupProgress;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(7f, 18f, progress));
                    eyeRenderer.color = Color.Lerp(windupEyeColor, chargeEyeColor, progress);
                    eyeRenderer.transform.localScale = Vector3.one * Mathf.Lerp(0.24f, 0.38f, progress) *
                                                       Mathf.Lerp(0.92f, 1.12f, pulse);
                    break;
                case TankChargerState.Charging:
                    eyeRenderer.color = Color.Lerp(chargeEyeColor, Color.white, 0.25f);
                    eyeRenderer.transform.localScale = Vector3.one * 0.34f;
                    break;
                case TankChargerState.Recovery:
                    eyeRenderer.color = recoveryEyeColor;
                    eyeRenderer.transform.localScale = new Vector3(0.25f, 0.18f, 1f);
                    break;
                case TankChargerState.BellyUp:
                    eyeRenderer.color = recoveryEyeColor * 0.7f;
                    eyeRenderer.transform.localScale = new Vector3(0.25f, 0.18f, 1f);
                    break;
                default:
                    eyeRenderer.color = patrolEyeColor;
                    eyeRenderer.transform.localScale = new Vector3(0.25f, 0.18f, 1f);
                    break;
            }
        }

        private void UpdateSmoke()
        {
            if (owner.CurrentState == TankChargerState.Windup)
            {
                SetSmokeIntensity(Mathf.Lerp(0.25f, 1f, owner.WindupProgress));
            }
            else
            {
                SetSmokeIntensity(0f);
            }

            if (previousState == TankChargerState.Windup &&
                owner.CurrentState == TankChargerState.Charging)
            {
                EmitSmokeBurst(upperSmoke);
                EmitSmokeBurst(lowerSmoke);
            }
        }

        private void SetSmokeIntensity(float intensity)
        {
            SetSmokeIntensity(upperSmoke, intensity);
            SetSmokeIntensity(lowerSmoke, intensity);
        }

        private static void SetSmokeIntensity(ParticleSystem smoke, float intensity)
        {
            if (smoke == null)
                return;

            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = Mathf.Lerp(5f, 24f, Mathf.Clamp01(intensity));

            if (intensity > 0f)
            {
                if (!smoke.isPlaying)
                    smoke.Play();
            }
            else if (smoke.isPlaying)
            {
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static void EmitSmokeBurst(ParticleSystem smoke)
        {
            if (smoke == null)
                return;

            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                startLifetime = 0.8f,
                startSize = 0.34f,
                startColor = new Color(0.7f, 0.72f, 0.74f, 0.8f)
            };
            smoke.Play();
            smoke.Emit(parameters, 7);
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static Sprite CreateSprite(Texture2D texture, string spriteName)
        {
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            sprite.name = spriteName;
            return sprite;
        }

        private static Material CreateMaterial(Shader shader, string materialName, Texture texture)
        {
            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            return material;
        }

        private static Texture2D BuildCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tank Charger Circle Texture Runtime",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f - 1f;
            float featherStart = radius - 1.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = 1f - Mathf.InverseLerp(featherStart, radius, distance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (solidSprite != null)
                Destroy(solidSprite);
            if (circleSprite != null)
                Destroy(circleSprite);
            if (solidTexture != null)
                Destroy(solidTexture);
            if (circleTexture != null)
                Destroy(circleTexture);
            if (spriteMaterial != null)
                Destroy(spriteMaterial);
            if (smokeMaterial != null)
                Destroy(smokeMaterial);
        }
    }
}
