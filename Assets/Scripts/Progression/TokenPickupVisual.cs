using System.Collections.Generic;
using UnityEngine;

namespace Game.Progression
{
    [DisallowMultipleComponent]
    public sealed class TokenPickupVisual : MonoBehaviour
    {
        [SerializeField] private PlayerStatType statType;
        [SerializeField] private string sortingLayerName = "Characters";
        [SerializeField] private int sortingOrder;
        [SerializeField, Min(0.01f)] private float lineWidth = 0.1f;
        [SerializeField] private bool enableMotion = true;
        [SerializeField, Min(0f)] private float floatAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float floatFrequency = 1.7f;

        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private Material runtimeMaterial;
        private Vector3 baseLocalPosition;

        private void Awake()
        {
            baseLocalPosition = transform.localPosition;
            Rebuild();
        }

        private void LateUpdate()
        {
            if (!enableMotion)
                return;
            transform.localPosition = baseLocalPosition +
                Vector3.up * (Mathf.Sin(Time.time * floatFrequency) * floatAmplitude);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void Configure(PlayerStatType targetStat)
        {
            if (statType == targetStat && generatedObjects.Count > 0)
                return;
            statType = targetStat;
            if (Application.isPlaying)
                Rebuild();
        }

        private void Rebuild()
        {
            for (int i = 0; i < generatedObjects.Count; i++)
            {
                if (generatedObjects[i] != null)
                    Destroy(generatedObjects[i]);
            }
            generatedObjects.Clear();

            if (runtimeMaterial == null)
                runtimeMaterial = CreateRuntimeMaterial();

            Color color = GetColor(statType);
            Vector3[] diamond =
            {
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0.42f, 0f, 0f),
                new Vector3(0f, -0.5f, 0f),
                new Vector3(-0.42f, 0f, 0f)
            };
            LineRenderer outline = CreateLine("TokenOutline", sortingOrder, diamond.Length, true, color);
            outline.SetPositions(diamond);

            LineRenderer core = CreateLine("TokenCore", sortingOrder + 1, 2, false, Color.white);
            core.SetPosition(0, new Vector3(0f, -0.22f, 0f));
            core.SetPosition(1, new Vector3(0f, 0.22f, 0f));
        }

        private LineRenderer CreateLine(
            string objectName,
            int order,
            int pointCount,
            bool loop,
            Color color)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);
            generatedObjects.Add(lineObject);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.positionCount = pointCount;
            line.loop = loop;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = order;
            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;
            return line;
        }

        private static Color GetColor(PlayerStatType stat)
        {
            switch (stat)
            {
                case PlayerStatType.MovementSpeed:
                    return new Color(0.2f, 0.9f, 1f, 1f);
                case PlayerStatType.JumpForce:
                    return new Color(0.75f, 0.4f, 1f, 1f);
                case PlayerStatType.MaxHealth:
                    return new Color(1f, 0.25f, 0.3f, 1f);
                case PlayerStatType.MaxStamina:
                    return new Color(0.25f, 1f, 0.45f, 1f);
                default:
                    return Color.white;
            }
        }

        private static Material CreateRuntimeMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            return shader != null ? new Material(shader) : null;
        }
    }
}
