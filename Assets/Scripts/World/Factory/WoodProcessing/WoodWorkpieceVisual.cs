using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Replaceable visual layer for WoodWorkpiece. Assigned sprites take priority;
    /// otherwise a lightweight line placeholder distinguishes every process state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WoodWorkpieceVisual : MonoBehaviour
    {
        [Header("Optional state sprites")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite verticalLogSprite;
        [SerializeField] private Sprite verticalBoardsSprite;
        [SerializeField] private Sprite horizontalBoardsSprite;
        [SerializeField] private Sprite crossCut1Sprite;
        [SerializeField] private Sprite crossCut2Sprite;
        [SerializeField] private Sprite crossCut3Sprite;

        [Header("Placeholder rendering")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color logColor = new Color(0.48f, 0.25f, 0.08f, 1f);
        [SerializeField] private Color boardsColor = new Color(0.76f, 0.49f, 0.2f, 1f);
        [SerializeField] private Color cutColor = new Color(1f, 0.82f, 0.38f, 1f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.055f;
        [SerializeField] private Vector2 localSize = new Vector2(0.7f, 2.8f);

        private readonly LineRenderer[] boardLines = new LineRenderer[2];
        private readonly LineRenderer[] cutLines = new LineRenderer[3];
        private LineRenderer outline;
        private Material runtimeMaterial;
        private WoodWorkpieceState currentState;
        private bool initialized;

        private void Awake()
        {
            EnsureVisuals();
            ApplyState();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void SetState(WoodWorkpieceState state)
        {
            currentState = state;

            if (Application.isPlaying)
            {
                EnsureVisuals();
                ApplyState();
            }
        }

        private void EnsureVisuals()
        {
            if (initialized)
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            runtimeMaterial = CreateRuntimeMaterial();
            outline = CreateLine("Outline", sortingOrder, 4, true);
            boardLines[0] = CreateLine("BoardDivider_1", sortingOrder + 1, 2, false);
            boardLines[1] = CreateLine("BoardDivider_2", sortingOrder + 1, 2, false);

            for (int i = 0; i < cutLines.Length; i++)
                cutLines[i] = CreateLine($"CrossCut_{i + 1}", sortingOrder + 2, 2, false);

            initialized = true;
            UpdateGeometry();
        }

        private void ApplyState()
        {
            if (!initialized)
                return;

            Sprite stateSprite = GetStateSprite(currentState);
            bool useSprite = spriteRenderer != null && stateSprite != null;

            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = stateSprite;
                spriteRenderer.enabled = useSprite && currentState != WoodWorkpieceState.Disposed;
            }

            bool showPlaceholder = !useSprite && currentState != WoodWorkpieceState.Disposed;
            outline.enabled = showPlaceholder;

            bool showBoards = showPlaceholder && currentState != WoodWorkpieceState.VerticalLog;
            boardLines[0].enabled = showBoards;
            boardLines[1].enabled = showBoards;

            int visibleCuts = GetVisibleCutCount(currentState);
            for (int i = 0; i < cutLines.Length; i++)
                cutLines[i].enabled = showPlaceholder && i < visibleCuts;

            Color bodyColor = currentState == WoodWorkpieceState.VerticalLog ? logColor : boardsColor;
            outline.startColor = bodyColor;
            outline.endColor = bodyColor;

            for (int i = 0; i < boardLines.Length; i++)
            {
                boardLines[i].startColor = bodyColor;
                boardLines[i].endColor = bodyColor;
            }

            for (int i = 0; i < cutLines.Length; i++)
            {
                cutLines[i].startColor = cutColor;
                cutLines[i].endColor = cutColor;
            }
        }

        private void UpdateGeometry()
        {
            float halfWidth = Mathf.Max(0.1f, localSize.x) * 0.5f;
            float halfHeight = Mathf.Max(0.2f, localSize.y) * 0.5f;

            outline.widthMultiplier = lineWidth;
            outline.SetPosition(0, new Vector3(-halfWidth, -halfHeight, 0f));
            outline.SetPosition(1, new Vector3(halfWidth, -halfHeight, 0f));
            outline.SetPosition(2, new Vector3(halfWidth, halfHeight, 0f));
            outline.SetPosition(3, new Vector3(-halfWidth, halfHeight, 0f));

            for (int i = 0; i < boardLines.Length; i++)
            {
                float x = Mathf.Lerp(-halfWidth, halfWidth, (i + 1f) / 3f);
                boardLines[i].widthMultiplier = lineWidth * 0.75f;
                boardLines[i].SetPosition(0, new Vector3(x, -halfHeight, 0f));
                boardLines[i].SetPosition(1, new Vector3(x, halfHeight, 0f));
            }

            for (int i = 0; i < cutLines.Length; i++)
            {
                float y = Mathf.Lerp(-halfHeight, halfHeight, (i + 1f) / 4f);
                cutLines[i].widthMultiplier = lineWidth * 1.4f;
                cutLines[i].SetPosition(0, new Vector3(-halfWidth, y, 0f));
                cutLines[i].SetPosition(1, new Vector3(halfWidth, y, 0f));
            }
        }

        private LineRenderer CreateLine(string objectName, int order, int pointCount, bool loop)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = pointCount;
            line.loop = loop;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = order;

            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;

            return line;
        }

        private Sprite GetStateSprite(WoodWorkpieceState state)
        {
            switch (state)
            {
                case WoodWorkpieceState.VerticalLog:
                    return verticalLogSprite;
                case WoodWorkpieceState.VerticalBoards:
                case WoodWorkpieceState.Turning:
                    return verticalBoardsSprite;
                case WoodWorkpieceState.HorizontalBoards:
                    return horizontalBoardsSprite;
                case WoodWorkpieceState.CrossCut1:
                    return crossCut1Sprite;
                case WoodWorkpieceState.CrossCut2:
                    return crossCut2Sprite;
                case WoodWorkpieceState.CrossCut3:
                    return crossCut3Sprite;
                default:
                    return null;
            }
        }

        private static int GetVisibleCutCount(WoodWorkpieceState state)
        {
            switch (state)
            {
                case WoodWorkpieceState.CrossCut1:
                    return 1;
                case WoodWorkpieceState.CrossCut2:
                    return 2;
                case WoodWorkpieceState.CrossCut3:
                    return 3;
                default:
                    return 0;
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
