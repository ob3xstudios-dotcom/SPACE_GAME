using UnityEngine;

namespace Game.Enemies.AerialSentry
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class AerialSentryVisionVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AerialSentryVision vision;
        [SerializeField] private AerialSentryRobot owner;

        [Header("Visual")]
        [SerializeField, Range(3, 64)] private int visionRayCount = 32;
        [SerializeField] private Color patrolColor = new Color(0.1f, 0.75f, 1f, 0.16f);
        [SerializeField] private Color acquiringColor = new Color(1f, 0.82f, 0.15f, 0.25f);
        [SerializeField] private Color chargingColor = new Color(1f, 0.15f, 0.08f, 0.34f);
        [SerializeField] private Color cooldownColor = new Color(0.25f, 0.55f, 1f, 0.13f);
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder;
        [SerializeField] private bool debugVisualLogs;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh visionMesh;
        private Material visionMaterial;
        private Vector3[] vertices;
        private Vector2[] uvs;
        private Color32[] colors;
        private int[] triangles;
        private bool visualStateLogged;

        private void Awake()
        {
            EnsureVisualResources();
            UpdateVisionMesh();
        }

        private void OnEnable()
        {
            EnsureVisualResources();
            UpdateVisionMesh();
        }

        private void LateUpdate()
        {
            UpdateVisionMesh();
        }

        private void EnsureVisualResources()
        {
            if (vision == null)
                vision = GetComponentInParent<AerialSentryVision>();
            if (owner == null)
                owner = GetComponentInParent<AerialSentryRobot>();

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.enabled = true;
            meshRenderer.forceRenderingOff = false;
            meshRenderer.sortingLayerName = sortingLayerName;
            meshRenderer.sortingOrder = sortingOrder;

            if (visionMesh == null)
            {
                visionMesh = new Mesh
                {
                    name = "Aerial Sentry Vision Fan",
                    hideFlags = HideFlags.DontSave
                };
                visionMesh.MarkDynamic();
            }
            meshFilter.sharedMesh = visionMesh;

            if (visionMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    visionMaterial = new Material(shader)
                    {
                        name = "Aerial Sentry Vision Runtime",
                        hideFlags = HideFlags.DontSave
                    };
                    if (visionMaterial.HasProperty("_MainTex"))
                        visionMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (visionMaterial.HasProperty("_Cull"))
                        visionMaterial.SetInt("_Cull", 0);
                }
            }
            if (visionMaterial != null)
                meshRenderer.sharedMaterial = visionMaterial;

            int desiredVertexCount = Mathf.Clamp(visionRayCount, 3, 64) + 1;
            if (vertices == null || vertices.Length != desiredVertexCount)
                RebuildBuffers();
        }

        private void UpdateVisionMesh()
        {
            EnsureVisualResources();
            if (vision == null || visionMesh == null)
                return;

            int desiredVertexCount = Mathf.Clamp(visionRayCount, 3, 64) + 1;
            if (vertices == null || vertices.Length != desiredVertexCount)
                RebuildBuffers();

            vertices[0] = transform.InverseTransformPoint(vision.Origin);
            int rays = vertices.Length - 1;
            float halfAngle = vision.VisionAngle * 0.5f;
            for (int i = 0; i < rays; i++)
            {
                float t = rays <= 1 ? 0.5f : i / (float)(rays - 1);
                float signedAngle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector2 worldPoint = vision.GetOccludedRayEnd(signedAngle);
                vertices[i + 1] = transform.InverseTransformPoint(worldPoint);
            }

            visionMesh.Clear();
            visionMesh.vertices = vertices;
            visionMesh.triangles = triangles;
            visionMesh.uv = uvs;
            visionMesh.colors32 = colors;
            visionMesh.RecalculateBounds();

            if (visionMaterial != null)
                visionMaterial.color = GetStateColor();

            LogVisualStateOnce();
        }

        private void RebuildBuffers()
        {
            int rays = Mathf.Clamp(visionRayCount, 3, 64);
            vertices = new Vector3[rays + 1];
            uvs = new Vector2[rays + 1];
            colors = new Color32[rays + 1];
            triangles = new int[(rays - 1) * 3];

            uvs[0] = new Vector2(0.5f, 0f);
            colors[0] = Color.white;
            for (int i = 0; i < rays; i++)
            {
                float t = rays <= 1 ? 0.5f : i / (float)(rays - 1);
                uvs[i + 1] = new Vector2(t, 1f);
                colors[i + 1] = Color.white;
            }

            for (int i = 0; i < rays - 1; i++)
            {
                int triangleIndex = i * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = i + 1;
                triangles[triangleIndex + 2] = i + 2;
            }
        }

        private void LogVisualStateOnce()
        {
            if (!debugVisualLogs || visualStateLogged || meshRenderer == null || visionMesh == null)
                return;

            visualStateLogged = true;
            string shaderName = visionMaterial != null && visionMaterial.shader != null
                ? visionMaterial.shader.name
                : "None";
            Debug.Log(
                $"[SENTRY VISUAL] visionMeshRenderer={meshRenderer.enabled} " +
                $"active={meshRenderer.gameObject.activeInHierarchy} " +
                $"sortingLayer={meshRenderer.sortingLayerName} " +
                $"sortingOrder={meshRenderer.sortingOrder} " +
                $"layer={meshRenderer.gameObject.layer} z={meshRenderer.transform.position.z:0.###} " +
                $"shader={shaderName} meshVertices={visionMesh.vertexCount} " +
                $"meshBounds={visionMesh.bounds}",
                this);
        }

        private Color GetStateColor()
        {
            if (owner == null)
                return patrolColor;

            switch (owner.CurrentState)
            {
                case AerialSentryState.Acquiring:
                    return acquiringColor;
                case AerialSentryState.Charging:
                    float pulse = 0.75f + Mathf.PingPong(Time.time * 3f, 0.25f);
                    return new Color(
                        chargingColor.r,
                        chargingColor.g,
                        chargingColor.b,
                        chargingColor.a * pulse);
                case AerialSentryState.Cooldown:
                    return Color.Lerp(cooldownColor, patrolColor, owner.CooldownProgress);
                default:
                    return patrolColor;
            }
        }

        private void OnValidate()
        {
            visionRayCount = Mathf.Clamp(visionRayCount, 3, 64);
        }

        private void OnDestroy()
        {
            if (visionMesh != null)
                Destroy(visionMesh);
            if (visionMaterial != null)
                Destroy(visionMaterial);
        }
    }
}
