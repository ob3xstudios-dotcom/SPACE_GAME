using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Game.EditorTools.GroundCollisionRegions
{
    public sealed class GroundCollisionRegionPlannerWindow : EditorWindow
    {
        private const string MenuPath = "Tools/SpaceGame/Collision/Ground Region Planner";
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static readonly Vector3Int[] CardinalDirections =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.up,
            Vector3Int.down
        };

        private static readonly string[] SuggestedRegionNames =
        {
            "Puerto117",
            "Montañas",
            "Fábrica",
            "Cueva",
            "Conexiones"
        };

        private static readonly Color[] SuggestedRegionColors =
        {
            new Color(0.15f, 0.65f, 1f, 1f),
            new Color(0.35f, 0.8f, 0.35f, 1f),
            new Color(1f, 0.55f, 0.15f, 1f),
            new Color(0.65f, 0.4f, 0.9f, 1f),
            new Color(1f, 0.82f, 0.15f, 1f)
        };

        [SerializeField] private GroundCollisionRegionPlan plan;
        [SerializeField] private Tilemap sourceTilemap;
        [SerializeField] private int selectedRegionIndex = -1;
        [SerializeField] private int selectedRectangleIndex = -1;
        [SerializeField] private bool showCellOverlay = true;
        [SerializeField] private bool showOccupiedBounds = true;
        [SerializeField] private bool showRegionLabels = true;
        [SerializeField, Range(0.05f, 0.8f)] private float overlayOpacity = 0.3f;
        [SerializeField] private string selectedSurfaceId = string.Empty;
        [SerializeField] private bool surfaceHighlightEnabled = true;
        [SerializeField] private bool showUnassignedSurfaces = true;
        [SerializeField] private int unassignedSurfaceDisplayLimit = 25;

        private readonly List<Vector3Int> occupiedCells = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> occupiedCellSet = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> collidableCellSet = new HashSet<Vector3Int>();
        private readonly List<SurfaceComponent> surfaceComponents = new List<SurfaceComponent>();
        private readonly Dictionary<string, SurfaceComponent> surfacesById =
            new Dictionary<string, SurfaceComponent>(StringComparer.Ordinal);
        private readonly Dictionary<Vector3Int, int> cellAssignments = new Dictionary<Vector3Int, int>();
        private readonly HashSet<Vector3Int> crossingCells = new HashSet<Vector3Int>();
        private readonly List<SurfaceConflict> surfaceConflicts = new List<SurfaceConflict>();
        private readonly List<UnassignedSurface> unassignedSurfaces = new List<UnassignedSurface>();
        private readonly List<SurfaceAssignmentSuggestion> remainingAssignmentSuggestions =
            new List<SurfaceAssignmentSuggestion>();
        private readonly Dictionary<string, int> pendingDestinationBySurface =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, GroundCollisionSurfaceAssignment> validAssignmentsBySurface =
            new Dictionary<string, GroundCollisionSurfaceAssignment>(StringComparer.Ordinal);

        private Vector2 scrollPosition;
        private BoundsInt occupiedBounds;
        private int collidableTileCount;
        private int unassignedTileCount;
        private int multiplyAssignedTileCount;
        private int unassignedTilesOutsideCollidableSurfaces;
        private int staleSurfaceAssignmentCount;
        private int validSurfaceAssignmentCount;
        private string currentFingerprint = string.Empty;
        private string suggestionsFingerprint = string.Empty;
        private bool analysisReady;
        private bool showBatchSuggestions = true;

        private int drawingRegionIndex = -1;
        private bool drawingRectangle;
        private Vector3Int drawStartCell;
        private Vector3Int drawCurrentCell;

        private Material overlayMaterial;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            GroundCollisionRegionPlannerWindow window =
                GetWindow<GroundCollisionRegionPlannerWindow>("Ground Regions");
            window.minSize = new Vector2(650f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyOverlayMaterial();
        }

        private void OnUndoRedo()
        {
            RefreshAssignments();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Analysis and preview only. This window never writes to the selected Tilemap, " +
                "its colliders, its scene, or its prefab.",
                MessageType.Info);

            DrawPlanControls();
            EditorGUILayout.Space();
            DrawSourceControls();

            if (!analysisReady)
                return;

            EditorGUILayout.Space();
            DrawAnalysisSummary();
            EditorGUILayout.Space();
            DrawDisplayControls();
            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawSelectedSurfacePanel();
            DrawRegionEditor();
            DrawConflictReport();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlanControls()
        {
            EditorGUILayout.LabelField("Region plan asset", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GroundCollisionRegionPlan newPlan = (GroundCollisionRegionPlan)EditorGUILayout.ObjectField(
                    plan,
                    typeof(GroundCollisionRegionPlan),
                    false);
                if (newPlan != plan)
                {
                    plan = newPlan;
                    selectedRegionIndex = -1;
                    selectedRectangleIndex = -1;
                    selectedSurfaceId = string.Empty;
                    pendingDestinationBySurface.Clear();
                    CancelRectangleDrawing();
                    RefreshAssignments();
                }

                if (GUILayout.Button("Create", GUILayout.Width(64f)))
                    CreatePlanAsset();

                using (new EditorGUI.DisabledScope(plan == null))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(58f)))
                        AssetDatabase.SaveAssetIfDirty(plan);
                }
            }

            if (plan == null)
            {
                EditorGUILayout.HelpBox(
                    "Create or select an independent plan asset before defining regions.",
                    MessageType.None);
                return;
            }

            if (analysisReady && !PlanMatchesCurrentAnalysis())
            {
                EditorGUILayout.HelpBox(
                    "The plan snapshot does not match the current analysis. Region editing is still " +
                    "available, but save the current source snapshot before approval.",
                    MessageType.Warning);
                if (GUILayout.Button("Associate current analysis with this plan"))
                    StoreCurrentAnalysisInPlan();
            }

            if (analysisReady && staleSurfaceAssignmentCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{staleSurfaceAssignmentCount} saved surface assignment(s) are inactive because " +
                    "their source fingerprint no longer matches, their surface no longer exists, or " +
                    "their destination region is missing. Review them before treating the plan as resolved.",
                    MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Revalidate matching surfaces"))
                        RevalidateMatchingSurfaceAssignments();
                    if (GUILayout.Button("Remove stale assignments"))
                        RemoveStaleSurfaceAssignments();
                }
            }
        }

        private void DrawSourceControls()
        {
            EditorGUILayout.LabelField("Source Tilemap (read-only)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                Tilemap newSource = (Tilemap)EditorGUILayout.ObjectField(
                    sourceTilemap,
                    typeof(Tilemap),
                    true);
                if (newSource != sourceTilemap)
                {
                    sourceTilemap = newSource;
                    ClearAnalysis();
                }

                if (GUILayout.Button("Use Selection", GUILayout.Width(104f)))
                {
                    GameObject selected = Selection.activeGameObject;
                    Tilemap selectedTilemap = selected != null ? selected.GetComponent<Tilemap>() : null;
                    if (selectedTilemap != null)
                    {
                        sourceTilemap = selectedTilemap;
                        ClearAnalysis();
                    }
                    else
                    {
                        ShowNotification(new GUIContent("Select a GameObject with a Tilemap component."));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(sourceTilemap == null))
            {
                if (GUILayout.Button(analysisReady ? "Re-analyze Tilemap" : "Analyze Tilemap"))
                    AnalyzeTilemap();
            }

            if (sourceTilemap != null)
            {
                string scenePath = sourceTilemap.gameObject.scene.path;
                EditorGUILayout.LabelField("Scene", string.IsNullOrEmpty(scenePath) ? "Unsaved scene" : scenePath);
                EditorGUILayout.LabelField("Hierarchy", GetHierarchyPath(sourceTilemap.transform));
            }
        }

        private void DrawAnalysisSummary()
        {
            EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Occupied tiles", occupiedCells.Count.ToString());
            EditorGUILayout.LabelField("Tiles with collision", collidableTileCount.ToString());
            EditorGUILayout.LabelField("Occupied cell bounds", FormatBounds(occupiedBounds));
            EditorGUILayout.LabelField("Continuous surfaces", surfaceComponents.Count.ToString());
            EditorGUILayout.LabelField("Unassigned tiles", unassignedTileCount.ToString());
            EditorGUILayout.LabelField("Multiply assigned tiles", multiplyAssignedTileCount.ToString());
            EditorGUILayout.LabelField("Surfaces crossing regions", surfaceConflicts.Count.ToString());
            EditorGUILayout.LabelField("Manual surface assignments", validSurfaceAssignmentCount.ToString());
            if (staleSurfaceAssignmentCount > 0)
                EditorGUILayout.LabelField("Assignments requiring review", staleSurfaceAssignmentCount.ToString());

            if (plan != null && PlanMatchesCurrentAnalysis())
                EditorGUILayout.HelpBox("The plan snapshot matches this Tilemap analysis.", MessageType.Info);
        }

        private void DrawDisplayControls()
        {
            EditorGUILayout.LabelField("Scene View preview", EditorStyles.boldLabel);
            showCellOverlay = EditorGUILayout.Toggle("Colored tile overlay", showCellOverlay);
            showOccupiedBounds = EditorGUILayout.Toggle("Real occupied bounds", showOccupiedBounds);
            showRegionLabels = EditorGUILayout.Toggle("Region labels", showRegionLabels);
            overlayOpacity = EditorGUILayout.Slider("Overlay opacity", overlayOpacity, 0.05f, 0.8f);

            SurfaceComponent selectedSurface = GetSelectedSurface();
            if (selectedSurface != null)
            {
                surfaceHighlightEnabled = EditorGUILayout.Toggle(
                    "Highlight selected surface",
                    surfaceHighlightEnabled);
            }

            if (drawingRegionIndex >= 0)
            {
                string regionName = GetRegionName(drawingRegionIndex);
                EditorGUILayout.HelpBox(
                    $"Drawing a rectangle for '{regionName}'. Drag with the left mouse button in " +
                    "Scene View. Press Escape to cancel.",
                    MessageType.Info);
                if (GUILayout.Button("Cancel rectangle drawing"))
                    CancelRectangleDrawing();
            }
        }

        private void DrawSelectedSurfacePanel()
        {
            SurfaceComponent component = GetSelectedSurface();
            if (component == null)
                return;

            EditorGUILayout.LabelField("Selected surface", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("ID", component.SurfaceId);
                EditorGUILayout.LabelField("Tiles", component.Cells.Count.ToString());
                EditorGUILayout.LabelField("Cell bounds", FormatBounds(component.Bounds));
                EditorGUILayout.LabelField("Regions", GetSurfaceRegionSummary(component));

                GroundCollisionSurfaceAssignment assignment = GetValidSurfaceAssignment(component);
                if (assignment != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Manual destination: {GetRegionNameById(assignment.destinationRegionId)}",
                        MessageType.Info);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Focus selected surface"))
                        FocusSurface(component);

                    if (surfaceHighlightEnabled)
                    {
                        if (GUILayout.Button("Disable highlight"))
                            surfaceHighlightEnabled = false;
                    }
                    else if (GUILayout.Button("Enable highlight"))
                    {
                        surfaceHighlightEnabled = true;
                        SceneView.RepaintAll();
                    }
                }

                DrawSurfaceAssignmentControls(component);
            }

            EditorGUILayout.Space();
        }

        private void DrawRegionEditor()
        {
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            if (plan == null)
            {
                EditorGUILayout.HelpBox("No region plan selected.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add empty region"))
                    AddRegion("New Region", GetNextRegionColor(), false);

                if (GUILayout.Button("Add suggested names"))
                    AddSuggestedRegions();
            }

            if (plan.Regions.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The suggested names are optional. You can start with any empty region and rename it.",
                    MessageType.None);
                return;
            }

            for (int regionIndex = 0; regionIndex < plan.Regions.Count; regionIndex++)
            {
                GroundCollisionRegionDefinition region = plan.Regions[regionIndex];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool selected = GUILayout.Toggle(
                            selectedRegionIndex == regionIndex,
                            (regionIndex + 1).ToString(),
                            "Button",
                            GUILayout.Width(30f));
                        if (selected && selectedRegionIndex != regionIndex)
                        {
                            selectedRegionIndex = regionIndex;
                            selectedRectangleIndex = -1;
                        }

                        string newName = EditorGUILayout.TextField(region.regionName);
                        if (!string.Equals(newName, region.regionName, StringComparison.Ordinal))
                            ModifyPlan("Rename collision region", () => region.regionName = newName);

                        Color newColor = EditorGUILayout.ColorField(region.color, GUILayout.Width(66f));
                        if (newColor != region.color)
                            ModifyPlan("Change collision region color", () => region.color = newColor);

                        bool newConnections = GUILayout.Toggle(
                            region.isConnectionsRegion,
                            "Connections",
                            GUILayout.Width(92f));
                        if (newConnections != region.isConnectionsRegion)
                            ModifyPlan(
                                "Set connections region",
                                () => region.isConnectionsRegion = newConnections);

                        if (GUILayout.Button("X", GUILayout.Width(24f)))
                        {
                            int removeIndex = regionIndex;
                            ModifyPlan(
                                "Remove collision region",
                                () => plan.Regions.RemoveAt(removeIndex));
                            selectedRegionIndex = -1;
                            selectedRectangleIndex = -1;
                            CancelRectangleDrawing();
                            break;
                        }
                    }

                    if (selectedRegionIndex == regionIndex)
                        DrawRectangleEditor(regionIndex, region);
                }
            }
        }

        private void DrawRectangleEditor(int regionIndex, GroundCollisionRegionDefinition region)
        {
            EditorGUILayout.LabelField("Cell rectangles", EditorStyles.miniBoldLabel);
            for (int rectangleIndex = 0; rectangleIndex < region.rectangles.Count; rectangleIndex++)
            {
                BoundsInt bounds = NormalizeBounds(region.rectangles[rectangleIndex]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = GUILayout.Toggle(
                        selectedRectangleIndex == rectangleIndex,
                        string.Empty,
                        GUILayout.Width(18f));
                    if (selected)
                        selectedRectangleIndex = rectangleIndex;

                    GUILayout.Label("x", GUILayout.Width(12f));
                    int x = EditorGUILayout.IntField(bounds.xMin, GUILayout.Width(52f));
                    GUILayout.Label("y", GUILayout.Width(12f));
                    int y = EditorGUILayout.IntField(bounds.yMin, GUILayout.Width(52f));
                    GUILayout.Label("w", GUILayout.Width(16f));
                    int width = Mathf.Max(1, EditorGUILayout.IntField(bounds.size.x, GUILayout.Width(48f)));
                    GUILayout.Label("h", GUILayout.Width(16f));
                    int height = Mathf.Max(1, EditorGUILayout.IntField(bounds.size.y, GUILayout.Width(48f)));

                    BoundsInt edited = CreateBounds(x, y, width, height);
                    if (!BoundsEqual(edited, bounds))
                    {
                        int editIndex = rectangleIndex;
                        ModifyPlan(
                            "Edit collision region rectangle",
                            () => region.rectangles[editIndex] = edited);
                    }

                    if (GUILayout.Button("Focus", GUILayout.Width(50f)))
                    {
                        selectedRectangleIndex = rectangleIndex;
                        FocusBounds(bounds);
                    }

                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        int removeIndex = rectangleIndex;
                        ModifyPlan(
                            "Remove collision region rectangle",
                            () => region.rectangles.RemoveAt(removeIndex));
                        selectedRectangleIndex = -1;
                        break;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Draw rectangle in Scene View"))
                {
                    drawingRegionIndex = regionIndex;
                    drawingRectangle = false;
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Add 16 x 16 at bounds center"))
                {
                    Vector3Int center = occupiedBounds.size == Vector3Int.zero
                        ? Vector3Int.zero
                        : new Vector3Int(
                            occupiedBounds.xMin + occupiedBounds.size.x / 2,
                            occupiedBounds.yMin + occupiedBounds.size.y / 2,
                            occupiedBounds.zMin);
                    BoundsInt newBounds = CreateBounds(center.x - 8, center.y - 8, 16, 16);
                    ModifyPlan(
                        "Add collision region rectangle",
                        () => region.rectangles.Add(newBounds));
                    selectedRectangleIndex = region.rectangles.Count - 1;
                }
            }
        }

        private void DrawConflictReport()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Coverage and continuity", EditorStyles.boldLabel);

            if (unassignedTileCount == 0 &&
                multiplyAssignedTileCount == 0 &&
                surfaceConflicts.Count == 0 &&
                staleSurfaceAssignmentCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Every tile has exactly one effective destination and no continuous surface " +
                    "crosses regions.",
                    MessageType.Info);
                return;
            }

            if (unassignedTileCount > 0)
                EditorGUILayout.HelpBox(
                    $"{unassignedTileCount} occupied tiles have no effective destination (gray overlay).",
                    MessageType.Warning);

            if (unassignedTilesOutsideCollidableSurfaces > 0)
                EditorGUILayout.HelpBox(
                    $"{unassignedTilesOutsideCollidableSurfaces} unassigned tiles have no collider and " +
                    "therefore are not part of a collidable surface group. Cover them with a region " +
                    "rectangle if they must be included in the migration plan.",
                    MessageType.Warning);

            if (multiplyAssignedTileCount > 0)
                EditorGUILayout.HelpBox(
                    $"{multiplyAssignedTileCount} occupied tiles have more than one effective " +
                    "destination (red overlay).",
                    MessageType.Error);

            if (surfaceConflicts.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Each orange entry is one complete collidable surface crossing region boundaries. " +
                    "Select it to isolate its tiles, then choose one final destination below it.",
                    MessageType.Warning);

                GroundCollisionRegionDefinition connections = FindConnectionsRegion();
                if (connections == null)
                {
                    if (GUILayout.Button("Create an empty suggested Connections region"))
                        AddRegion("Conexiones", SuggestedRegionColors[4], true);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        $"Available connection owner: {connections.regionName}",
                        EditorStyles.miniBoldLabel);
                }

                for (int i = 0; i < surfaceConflicts.Count; i++)
                {
                    SurfaceConflict conflict = surfaceConflicts[i];
                    DrawSurfaceDecisionEntry(
                        conflict.Component,
                        $"{string.Join(" / ", conflict.RegionNames)} — " +
                        $"{conflict.Component.Cells.Count} tiles",
                        "Cross-region surface");
                }
            }

            DrawBatchAssignmentSuggestions();

            if (unassignedSurfaces.Count > 0)
            {
                EditorGUILayout.Space();
                showUnassignedSurfaces = EditorGUILayout.Foldout(
                    showUnassignedSurfaces,
                    $"Surfaces containing unassigned tiles ({unassignedSurfaces.Count})",
                    true);

                if (showUnassignedSurfaces)
                {
                    EditorGUILayout.HelpBox(
                        "These groups are connected collidable surfaces. Assigning one sets a single " +
                        "destination for its complete surface; it does not alter any region rectangle.",
                        MessageType.Warning);

                    int shown = Mathf.Min(unassignedSurfaces.Count, unassignedSurfaceDisplayLimit);
                    for (int i = 0; i < shown; i++)
                    {
                        UnassignedSurface unassigned = unassignedSurfaces[i];
                        DrawSurfaceDecisionEntry(
                            unassigned.Component,
                            $"{unassigned.UnassignedTileCount} unassigned / " +
                            $"{unassigned.Component.Cells.Count} surface tiles",
                            "Unassigned surface");
                    }

                    if (shown < unassignedSurfaces.Count)
                    {
                        EditorGUILayout.LabelField(
                            $"Showing {shown} of {unassignedSurfaces.Count}, largest first.");
                        if (GUILayout.Button("Show 25 more unassigned surfaces"))
                            unassignedSurfaceDisplayLimit += 25;
                    }
                }
            }

            if (staleSurfaceAssignmentCount > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Inactive saved assignments never resolve a conflict. Revalidate matching surfaces " +
                    "or make a new explicit assignment after reviewing the changed source.",
                    MessageType.Warning);
            }
        }

        private void DrawBatchAssignmentSuggestions()
        {
            if (unassignedSurfaces.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Assisted batch assignment", EditorStyles.boldLabel);

            bool canSuggest = plan != null &&
                              plan.Regions.Count > 0 &&
                              PlanMatchesCurrentAnalysis() &&
                              staleSurfaceAssignmentCount == 0;
            using (new EditorGUI.DisabledScope(!canSuggest))
            {
                if (GUILayout.Button("Suggest remaining assignments"))
                    SuggestRemainingAssignments();
            }

            if (!canSuggest)
            {
                EditorGUILayout.HelpBox(
                    "Suggestions require a matching source fingerprint, at least one region, and no " +
                    "saved assignments awaiting review.",
                    MessageType.Warning);
                return;
            }

            if (remainingAssignmentSuggestions.Count == 0 ||
                !string.Equals(
                    suggestionsFingerprint,
                    currentFingerprint,
                    StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    "Generate suggestions to review them. This analysis is temporary and does not " +
                    "modify the plan asset.",
                    MessageType.None);
                return;
            }

            int clearCount = 0;
            int ambiguousCount = 0;
            for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
            {
                if (remainingAssignmentSuggestions[i].Confidence == SuggestionConfidence.Ambiguous)
                    ambiguousCount++;
                else
                    clearCount++;
            }

            showBatchSuggestions = EditorGUILayout.Foldout(
                showBatchSuggestions,
                $"Review suggestions ({clearCount} clear, {ambiguousCount} ambiguous)",
                true);
            if (!showBatchSuggestions)
                return;

            EditorGUILayout.HelpBox(
                "Partial membership in one region is treated as direct evidence. Fully unassigned " +
                "surfaces are compared by their minimum cell distance to every existing region " +
                "rectangle. Clear proposals start selected; ambiguous proposals never do.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all clear"))
                {
                    for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
                    {
                        SurfaceAssignmentSuggestion suggestion = remainingAssignmentSuggestions[i];
                        suggestion.Selected =
                            suggestion.Confidence != SuggestionConfidence.Ambiguous;
                    }
                }

                if (GUILayout.Button("Clear selection"))
                {
                    for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
                        remainingAssignmentSuggestions[i].Selected = false;
                }

                if (GUILayout.Button("Discard suggestions"))
                    InvalidateRemainingAssignmentSuggestions();
            }

            if (remainingAssignmentSuggestions.Count == 0)
                return;

            string[] regionNames = new string[plan.Regions.Count];
            for (int i = 0; i < regionNames.Length; i++)
                regionNames[i] = GetRegionName(i);

            DrawSuggestionGroup(
                "Clear proposals",
                false,
                regionNames);
            DrawSuggestionGroup(
                "Ambiguous proposals — review and select explicitly",
                true,
                regionNames);

            GetSelectedSuggestionTotals(
                out int selectedSurfaceCount,
                out int selectedTileCount,
                out int selectedAmbiguousCount);
            int pendingAfterApproval = Mathf.Max(0, unassignedTileCount - selectedTileCount);
            EditorGUILayout.HelpBox(
                $"Selected: {selectedSurfaceCount} surfaces / {selectedTileCount} currently " +
                $"unassigned tiles. Pending after approval: {pendingAfterApproval} tiles.",
                pendingAfterApproval == 0 ? MessageType.Info : MessageType.Warning);

            using (new EditorGUI.DisabledScope(selectedSurfaceCount == 0))
            {
                string label = selectedAmbiguousCount > 0
                    ? $"Approve selected assignments ({selectedAmbiguousCount} ambiguous)"
                    : "Approve selected assignments";
                if (GUILayout.Button(label))
                    ApproveSelectedSuggestions();
            }
        }

        private void DrawSuggestionGroup(
            string title,
            bool ambiguousGroup,
            string[] regionNames)
        {
            bool hasRows = remainingAssignmentSuggestions.Exists(suggestion =>
                (suggestion.Confidence == SuggestionConfidence.Ambiguous) == ambiguousGroup);
            if (!hasRows)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Use", EditorStyles.miniLabel, GUILayout.Width(28f));
                GUILayout.Label("Surface", EditorStyles.miniLabel, GUILayout.Width(178f));
                GUILayout.Label("Tiles", EditorStyles.miniLabel, GUILayout.Width(42f));
                GUILayout.Label("Destination", EditorStyles.miniLabel, GUILayout.MinWidth(130f));
                GUILayout.Label("Confidence", EditorStyles.miniLabel, GUILayout.Width(92f));
                GUILayout.Label(string.Empty, GUILayout.Width(52f));
            }

            for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
            {
                SurfaceAssignmentSuggestion suggestion = remainingAssignmentSuggestions[i];
                bool isAmbiguous =
                    suggestion.Confidence == SuggestionConfidence.Ambiguous;
                if (isAmbiguous != ambiguousGroup)
                    continue;

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    suggestion.Selected = GUILayout.Toggle(
                        suggestion.Selected,
                        GUIContent.none,
                        GUILayout.Width(28f));
                    GUILayout.Label(
                        new GUIContent(
                            GetShortSurfaceId(suggestion.Component.SurfaceId),
                            suggestion.Component.SurfaceId),
                        GUILayout.Width(178f));
                    GUILayout.Label(
                        suggestion.UnassignedTileCount.ToString(),
                        GUILayout.Width(42f));

                    int newRegionIndex = EditorGUILayout.Popup(
                        suggestion.DestinationRegionIndex,
                        regionNames,
                        GUILayout.MinWidth(130f));
                    if (newRegionIndex != suggestion.DestinationRegionIndex)
                    {
                        suggestion.DestinationRegionIndex = newRegionIndex;
                        suggestion.DestinationEdited = true;
                    }

                    Color previousContentColor = GUI.contentColor;
                    GUI.contentColor = GetSuggestionConfidenceColor(suggestion.Confidence);
                    GUILayout.Label(
                        new GUIContent(
                            GetSuggestionConfidenceLabel(suggestion.Confidence) +
                            (suggestion.DestinationEdited ? " *" : string.Empty),
                            suggestion.Reason +
                            (suggestion.DestinationEdited
                                ? "\nDestination changed manually during review."
                                : string.Empty)),
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(92f));
                    GUI.contentColor = previousContentColor;

                    if (GUILayout.Button("View", GUILayout.Width(52f)))
                        SelectSurface(suggestion.Component, true);
                }
            }
        }

        private void GetSelectedSuggestionTotals(
            out int selectedSurfaceCount,
            out int selectedTileCount,
            out int selectedAmbiguousCount)
        {
            selectedSurfaceCount = 0;
            selectedTileCount = 0;
            selectedAmbiguousCount = 0;
            for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
            {
                SurfaceAssignmentSuggestion suggestion = remainingAssignmentSuggestions[i];
                if (!suggestion.Selected)
                    continue;

                selectedSurfaceCount++;
                selectedTileCount += suggestion.UnassignedTileCount;
                if (suggestion.Confidence == SuggestionConfidence.Ambiguous)
                    selectedAmbiguousCount++;
            }
        }

        private void DrawSurfaceDecisionEntry(
            SurfaceComponent component,
            string title,
            string category)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
                    bool isSelected = string.Equals(
                        selectedSurfaceId,
                        component.SurfaceId,
                        StringComparison.Ordinal);
                    if (GUILayout.Button(isSelected ? "Selected" : "Select", GUILayout.Width(66f)))
                        SelectSurface(component, true);
                }

                EditorGUILayout.LabelField(
                    $"{category} | ID {GetShortSurfaceId(component.SurfaceId)} | " +
                    FormatBounds(component.Bounds),
                    EditorStyles.miniLabel);
                DrawSurfaceAssignmentControls(component);
            }
        }

        private void CreatePlanAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Ground Collision Region Plan",
                "MUELLE_GroundCollisionRegions",
                "asset",
                "Choose where to store the independent region plan asset.");
            if (string.IsNullOrEmpty(path))
                return;

            GroundCollisionRegionPlan newPlan = CreateInstance<GroundCollisionRegionPlan>();
            if (analysisReady)
                WriteAnalysisSnapshot(newPlan);

            AssetDatabase.CreateAsset(newPlan, path);
            AssetDatabase.SaveAssetIfDirty(newPlan);
            plan = newPlan;
            Selection.activeObject = newPlan;
            selectedRegionIndex = -1;
            selectedRectangleIndex = -1;
            RefreshAssignments();
        }

        private void StoreCurrentAnalysisInPlan()
        {
            if (plan == null || !analysisReady)
                return;

            Undo.RecordObject(plan, "Associate collision region plan source");
            WriteAnalysisSnapshot(plan);
            EditorUtility.SetDirty(plan);
            RefreshAssignments();
            Repaint();
        }

        private void WriteAnalysisSnapshot(GroundCollisionRegionPlan targetPlan)
        {
            string scenePath = sourceTilemap != null ? sourceTilemap.gameObject.scene.path : string.Empty;
            string sceneGuid = string.IsNullOrEmpty(scenePath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(scenePath);
            string hierarchyPath = sourceTilemap != null
                ? GetHierarchyPath(sourceTilemap.transform)
                : string.Empty;

            targetPlan.SetSourceSnapshot(
                scenePath,
                sceneGuid,
                hierarchyPath,
                occupiedBounds,
                occupiedCells.Count,
                collidableTileCount,
                currentFingerprint);
        }

        private bool PlanMatchesCurrentAnalysis()
        {
            if (plan == null || !analysisReady || sourceTilemap == null)
                return false;

            string scenePath = sourceTilemap.gameObject.scene.path;
            string sceneGuid = string.IsNullOrEmpty(scenePath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(scenePath);

            return string.Equals(plan.SourceSceneGuid, sceneGuid, StringComparison.Ordinal) &&
                   string.Equals(
                       plan.SourceTilemapHierarchyPath,
                       GetHierarchyPath(sourceTilemap.transform),
                       StringComparison.Ordinal) &&
                   string.Equals(plan.SourceFingerprint, currentFingerprint, StringComparison.Ordinal);
        }

        private void AnalyzeTilemap()
        {
            if (sourceTilemap == null)
                return;

            if (!sourceTilemap.gameObject.scene.IsValid())
            {
                EditorUtility.DisplayDialog(
                    "Scene Tilemap required",
                    "Select a Tilemap instance from an open scene, not a prefab asset.",
                    "OK");
                return;
            }

            occupiedCells.Clear();
            occupiedCellSet.Clear();
            collidableCellSet.Clear();
            surfaceComponents.Clear();
            currentFingerprint = string.Empty;

            Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            Dictionary<TileBase, string> tileIdentities = new Dictionary<TileBase, string>();
            ulong fingerprint = FnvOffsetBasis;

            BoundsInt scanBounds = sourceTilemap.cellBounds;
            foreach (Vector3Int cell in scanBounds.allPositionsWithin)
            {
                if (!sourceTilemap.HasTile(cell))
                    continue;

                occupiedCells.Add(cell);
                occupiedCellSet.Add(cell);
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);

                Tile.ColliderType colliderType = sourceTilemap.GetColliderType(cell);
                if (colliderType != Tile.ColliderType.None)
                    collidableCellSet.Add(cell);

                HashInt(ref fingerprint, cell.x);
                HashInt(ref fingerprint, cell.y);
                HashInt(ref fingerprint, cell.z);
                HashInt(ref fingerprint, (int)colliderType);
                HashInt(ref fingerprint, (int)sourceTilemap.GetTileFlags(cell));
                HashInt(ref fingerprint, sourceTilemap.GetColor(cell).GetHashCode());
                HashInt(ref fingerprint, sourceTilemap.GetTransformMatrix(cell).GetHashCode());

                TileBase tile = sourceTilemap.GetTile(cell);
                if (tile != null)
                {
                    if (!tileIdentities.TryGetValue(tile, out string identity))
                    {
                        identity = GlobalObjectId.GetGlobalObjectIdSlow(tile).ToString();
                        tileIdentities.Add(tile, identity);
                    }

                    HashString(ref fingerprint, identity);
                }
            }

            occupiedBounds = occupiedCells.Count == 0
                ? new BoundsInt(Vector3Int.zero, Vector3Int.zero)
                : new BoundsInt(min, max - min + Vector3Int.one);
            collidableTileCount = collidableCellSet.Count;
            currentFingerprint = fingerprint.ToString("X16");
            BuildSurfaceComponents();
            analysisReady = true;
            RefreshAssignments();
            Repaint();
            SceneView.RepaintAll();
        }

        private void BuildSurfaceComponents()
        {
            surfaceComponents.Clear();
            surfacesById.Clear();
            HashSet<Vector3Int> remaining = new HashSet<Vector3Int>(collidableCellSet);
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            while (remaining.Count > 0)
            {
                Vector3Int seed = default;
                foreach (Vector3Int cell in remaining)
                {
                    seed = cell;
                    break;
                }

                SurfaceComponent component = new SurfaceComponent();
                Vector3Int min = seed;
                Vector3Int max = seed;
                remaining.Remove(seed);
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    Vector3Int cell = queue.Dequeue();
                    component.Cells.Add(cell);
                    component.CellSet.Add(cell);
                    min = Vector3Int.Min(min, cell);
                    max = Vector3Int.Max(max, cell);

                    for (int i = 0; i < CardinalDirections.Length; i++)
                    {
                        Vector3Int neighbor = cell + CardinalDirections[i];
                        if (!remaining.Remove(neighbor))
                            continue;

                        queue.Enqueue(neighbor);
                    }
                }

                component.Bounds = new BoundsInt(min, max - min + Vector3Int.one);
                component.Cells.Sort(CompareCells);
                component.SurfaceId = BuildSurfaceId(component.Cells);
                surfaceComponents.Add(component);
                surfacesById[component.SurfaceId] = component;
            }

            surfaceComponents.Sort((a, b) => b.Cells.Count.CompareTo(a.Cells.Count));
        }

        private void RefreshAssignments()
        {
            InvalidateRemainingAssignmentSuggestions();
            cellAssignments.Clear();
            crossingCells.Clear();
            surfaceConflicts.Clear();
            unassignedSurfaces.Clear();
            validAssignmentsBySurface.Clear();
            unassignedTileCount = 0;
            multiplyAssignedTileCount = 0;
            unassignedTilesOutsideCollidableSurfaces = 0;
            staleSurfaceAssignmentCount = 0;
            validSurfaceAssignmentCount = 0;

            if (!analysisReady)
                return;

            // First calculate rectangle-only ownership. A valid manual surface decision is applied
            // afterwards as the single effective destination for every tile in that component.
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                Vector3Int cell = occupiedCells[i];
                int firstRegion;
                int membershipCount = GetRegionMembership(cell, out firstRegion, null);
                if (membershipCount == 0)
                    cellAssignments[cell] = -1;
                else if (membershipCount == 1)
                    cellAssignments[cell] = firstRegion;
                else
                    cellAssignments[cell] = -2;
            }

            BuildValidSurfaceAssignmentLookup();
            foreach (KeyValuePair<string, GroundCollisionSurfaceAssignment> pair in validAssignmentsBySurface)
            {
                if (!surfacesById.TryGetValue(pair.Key, out SurfaceComponent component))
                    continue;

                int destinationRegion = GetRegionIndexById(pair.Value.destinationRegionId);
                if (destinationRegion < 0)
                    continue;

                for (int cellIndex = 0; cellIndex < component.Cells.Count; cellIndex++)
                    cellAssignments[component.Cells[cellIndex]] = destinationRegion;
            }

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                Vector3Int cell = occupiedCells[i];
                if (!cellAssignments.TryGetValue(cell, out int assignment) || assignment == -1)
                {
                    unassignedTileCount++;
                    if (!collidableCellSet.Contains(cell))
                        unassignedTilesOutsideCollidableSurfaces++;
                }
                else if (assignment == -2)
                {
                    multiplyAssignedTileCount++;
                }
            }

            if (plan == null)
                return;

            HashSet<int> componentRegions = new HashSet<int>();
            List<int> memberships = new List<int>();
            for (int componentIndex = 0; componentIndex < surfaceComponents.Count; componentIndex++)
            {
                SurfaceComponent component = surfaceComponents[componentIndex];
                componentRegions.Clear();
                int componentUnassignedCount = 0;
                bool hasDuplicateDestination = false;

                for (int cellIndex = 0; cellIndex < component.Cells.Count; cellIndex++)
                {
                    Vector3Int cell = component.Cells[cellIndex];
                    if (!cellAssignments.TryGetValue(cell, out int assignment) || assignment == -1)
                    {
                        componentUnassignedCount++;
                    }
                    else if (assignment == -2)
                    {
                        hasDuplicateDestination = true;
                        memberships.Clear();
                        int unused;
                        GetRegionMembership(cell, out unused, memberships);
                        for (int membershipIndex = 0;
                             membershipIndex < memberships.Count;
                             membershipIndex++)
                        {
                            componentRegions.Add(memberships[membershipIndex]);
                        }
                    }
                    else
                    {
                        componentRegions.Add(assignment);
                    }
                }

                List<string> componentRegionNames = new List<string>();
                foreach (int regionIndex in componentRegions)
                    componentRegionNames.Add(GetRegionName(regionIndex));
                componentRegionNames.Sort(StringComparer.OrdinalIgnoreCase);
                if (componentUnassignedCount > 0)
                    componentRegionNames.Add("Unassigned");
                component.RegionSummary = componentRegionNames.Count > 0
                    ? string.Join(" / ", componentRegionNames)
                    : "Unassigned";

                bool crossesRegions = componentRegions.Count > 1 || hasDuplicateDestination;
                if (crossesRegions)
                {
                    crossingCells.UnionWith(component.Cells);
                    SurfaceConflict conflict = new SurfaceConflict
                    {
                        Component = component,
                        UnassignedTileCount = componentUnassignedCount
                    };
                    conflict.RegionNames.AddRange(componentRegionNames);
                    surfaceConflicts.Add(conflict);
                }
                else if (componentUnassignedCount > 0)
                {
                    unassignedSurfaces.Add(new UnassignedSurface
                    {
                        Component = component,
                        UnassignedTileCount = componentUnassignedCount
                    });
                }
            }

            surfaceConflicts.Sort((a, b) =>
                b.Component.Cells.Count.CompareTo(a.Component.Cells.Count));
            unassignedSurfaces.Sort((a, b) =>
            {
                int byUnassigned = b.UnassignedTileCount.CompareTo(a.UnassignedTileCount);
                return byUnassigned != 0
                    ? byUnassigned
                    : b.Component.Cells.Count.CompareTo(a.Component.Cells.Count);
            });

            SceneView.RepaintAll();
        }

        private void SuggestRemainingAssignments()
        {
            remainingAssignmentSuggestions.Clear();
            suggestionsFingerprint = string.Empty;

            if (plan == null ||
                plan.Regions.Count == 0 ||
                !PlanMatchesCurrentAnalysis() ||
                staleSurfaceAssignmentCount > 0)
            {
                return;
            }

            for (int i = 0; i < unassignedSurfaces.Count; i++)
            {
                UnassignedSurface unassigned = unassignedSurfaces[i];
                if (HasAnySavedAssignment(unassigned.Component.SurfaceId))
                    continue;

                remainingAssignmentSuggestions.Add(
                    BuildRemainingAssignmentSuggestion(unassigned));
            }

            remainingAssignmentSuggestions.Sort((left, right) =>
            {
                int byConfidence = left.Confidence.CompareTo(right.Confidence);
                if (byConfidence != 0)
                    return byConfidence;
                return right.UnassignedTileCount.CompareTo(left.UnassignedTileCount);
            });
            suggestionsFingerprint = currentFingerprint;
            showBatchSuggestions = true;
            Repaint();
        }

        private SurfaceAssignmentSuggestion BuildRemainingAssignmentSuggestion(
            UnassignedSurface unassigned)
        {
            SurfaceComponent component = unassigned.Component;
            int[] membershipCounts = new int[plan.Regions.Count];
            List<int> memberships = new List<int>();
            for (int cellIndex = 0; cellIndex < component.Cells.Count; cellIndex++)
            {
                memberships.Clear();
                int unused;
                GetRegionMembership(component.Cells[cellIndex], out unused, memberships);
                for (int membershipIndex = 0;
                     membershipIndex < memberships.Count;
                     membershipIndex++)
                {
                    membershipCounts[memberships[membershipIndex]]++;
                }
            }

            int touchedRegionCount = 0;
            int touchedRegionIndex = -1;
            int greatestMembershipCount = 0;
            for (int regionIndex = 0; regionIndex < membershipCounts.Length; regionIndex++)
            {
                if (membershipCounts[regionIndex] <= 0)
                    continue;

                touchedRegionCount++;
                if (membershipCounts[regionIndex] > greatestMembershipCount)
                {
                    greatestMembershipCount = membershipCounts[regionIndex];
                    touchedRegionIndex = regionIndex;
                }
            }

            if (touchedRegionCount == 1)
            {
                return new SurfaceAssignmentSuggestion
                {
                    Component = component,
                    UnassignedTileCount = unassigned.UnassignedTileCount,
                    DestinationRegionIndex = touchedRegionIndex,
                    Confidence = SuggestionConfidence.High,
                    Selected = true,
                    Reason =
                        $"Direct partial membership: {greatestMembershipCount} of " +
                        $"{component.Cells.Count} surface tiles are already inside " +
                        $"'{GetRegionName(touchedRegionIndex)}', and no tile touches another region."
                };
            }

            if (touchedRegionCount > 1)
            {
                return new SurfaceAssignmentSuggestion
                {
                    Component = component,
                    UnassignedTileCount = unassigned.UnassignedTileCount,
                    DestinationRegionIndex = Mathf.Max(0, touchedRegionIndex),
                    Confidence = SuggestionConfidence.Ambiguous,
                    Selected = false,
                    Reason =
                        "The surface has partial membership in more than one region. " +
                        "It requires an explicit destination review."
                };
            }

            int nearestRegionIndex = 0;
            float nearestDistance = float.PositiveInfinity;
            float secondDistance = float.PositiveInfinity;
            for (int regionIndex = 0; regionIndex < plan.Regions.Count; regionIndex++)
            {
                float distance = GetMinimumDistanceToRegion(component, plan.Regions[regionIndex]);
                if (distance < nearestDistance)
                {
                    secondDistance = nearestDistance;
                    nearestDistance = distance;
                    nearestRegionIndex = regionIndex;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }

            SuggestionConfidence confidence;
            string reason;
            if (float.IsPositiveInfinity(nearestDistance))
            {
                confidence = SuggestionConfidence.Ambiguous;
                reason = "No region has a rectangle that can be used for a spatial suggestion.";
            }
            else
            {
                float separation = secondDistance - nearestDistance;
                if (nearestDistance <= 2f && separation >= 2f)
                {
                    confidence = SuggestionConfidence.High;
                }
                else if (separation >= Mathf.Max(2f, nearestDistance * 0.5f))
                {
                    confidence = SuggestionConfidence.Medium;
                }
                else
                {
                    confidence = SuggestionConfidence.Ambiguous;
                }

                string secondText = float.IsPositiveInfinity(secondDistance)
                    ? "none"
                    : $"{secondDistance:0.0} cells";
                reason =
                    $"Nearest rectangle: '{GetRegionName(nearestRegionIndex)}' at " +
                    $"{nearestDistance:0.0} cells; next candidate: {secondText}.";
            }

            return new SurfaceAssignmentSuggestion
            {
                Component = component,
                UnassignedTileCount = unassigned.UnassignedTileCount,
                DestinationRegionIndex = nearestRegionIndex,
                Confidence = confidence,
                Selected = confidence != SuggestionConfidence.Ambiguous,
                Reason = reason
            };
        }

        private static float GetMinimumDistanceToRegion(
            SurfaceComponent component,
            GroundCollisionRegionDefinition region)
        {
            float minimumSquaredDistance = float.PositiveInfinity;
            for (int rectangleIndex = 0;
                 rectangleIndex < region.rectangles.Count;
                 rectangleIndex++)
            {
                BoundsInt bounds = NormalizeBounds(region.rectangles[rectangleIndex]);
                for (int cellIndex = 0; cellIndex < component.Cells.Count; cellIndex++)
                {
                    Vector3Int cell = component.Cells[cellIndex];
                    int deltaX = cell.x < bounds.xMin
                        ? bounds.xMin - cell.x
                        : cell.x >= bounds.xMax ? cell.x - bounds.xMax + 1 : 0;
                    int deltaY = cell.y < bounds.yMin
                        ? bounds.yMin - cell.y
                        : cell.y >= bounds.yMax ? cell.y - bounds.yMax + 1 : 0;
                    float squaredDistance = deltaX * deltaX + deltaY * deltaY;
                    if (squaredDistance < minimumSquaredDistance)
                        minimumSquaredDistance = squaredDistance;
                }
            }

            return float.IsPositiveInfinity(minimumSquaredDistance)
                ? float.PositiveInfinity
                : Mathf.Sqrt(minimumSquaredDistance);
        }

        private bool HasAnySavedAssignment(string surfaceId)
        {
            return plan != null && plan.SurfaceAssignments.Exists(assignment =>
                assignment != null &&
                string.Equals(assignment.surfaceId, surfaceId, StringComparison.Ordinal));
        }

        private void ApproveSelectedSuggestions()
        {
            if (plan == null ||
                !PlanMatchesCurrentAnalysis() ||
                staleSurfaceAssignmentCount > 0 ||
                !string.Equals(
                    suggestionsFingerprint,
                    currentFingerprint,
                    StringComparison.Ordinal))
            {
                ShowNotification(new GUIContent("Suggestions are stale. Generate them again."));
                InvalidateRemainingAssignmentSuggestions();
                return;
            }

            GetSelectedSuggestionTotals(
                out int selectedSurfaceCount,
                out int unusedSelectedTileCount,
                out int selectedAmbiguousCount);
            if (selectedSurfaceCount == 0)
                return;

            if (selectedAmbiguousCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "Approve ambiguous assignments?",
                    $"You explicitly selected {selectedAmbiguousCount} ambiguous proposal(s). " +
                    "Confirm that you reviewed their destination regions.",
                    "Approve selected",
                    "Cancel"))
            {
                return;
            }

            int addedCount = 0;
            int skippedCount = 0;
            ModifyPlan(
                "Approve suggested collision surface destinations",
                () =>
                {
                    EnsureRegionIds();
                    for (int i = 0; i < remainingAssignmentSuggestions.Count; i++)
                    {
                        SurfaceAssignmentSuggestion suggestion =
                            remainingAssignmentSuggestions[i];
                        if (!suggestion.Selected)
                            continue;

                        if (suggestion.DestinationRegionIndex < 0 ||
                            suggestion.DestinationRegionIndex >= plan.Regions.Count ||
                            !surfacesById.TryGetValue(
                                suggestion.Component.SurfaceId,
                                out SurfaceComponent component) ||
                            validAssignmentsBySurface.ContainsKey(component.SurfaceId) ||
                            HasAnySavedAssignment(component.SurfaceId) ||
                            CountUnassignedSurfaceTiles(component) == 0)
                        {
                            skippedCount++;
                            continue;
                        }

                        GroundCollisionRegionDefinition destination =
                            plan.Regions[suggestion.DestinationRegionIndex];
                        plan.SurfaceAssignments.Add(new GroundCollisionSurfaceAssignment
                        {
                            surfaceId = component.SurfaceId,
                            sourceFingerprint = currentFingerprint,
                            destinationRegionId = destination.regionId,
                            destinationRegionName = destination.regionName,
                            tileCount = component.Cells.Count,
                            cellBounds = component.Bounds
                        });
                        addedCount++;
                    }

                    plan.MarkSchemaCurrent();
                });

            string notification = skippedCount > 0
                ? $"Added {addedCount} assignment(s); skipped {skippedCount}."
                : $"Added {addedCount} assignment(s).";
            ShowNotification(new GUIContent(notification));
        }

        private int CountUnassignedSurfaceTiles(SurfaceComponent component)
        {
            int count = 0;
            for (int i = 0; i < component.Cells.Count; i++)
            {
                if (!cellAssignments.TryGetValue(component.Cells[i], out int assignment) ||
                    assignment == -1)
                {
                    count++;
                }
            }

            return count;
        }

        private void InvalidateRemainingAssignmentSuggestions()
        {
            remainingAssignmentSuggestions.Clear();
            suggestionsFingerprint = string.Empty;
        }

        private static string GetSuggestionConfidenceLabel(SuggestionConfidence confidence)
        {
            switch (confidence)
            {
                case SuggestionConfidence.High:
                    return "High";
                case SuggestionConfidence.Medium:
                    return "Medium";
                default:
                    return "Ambiguous";
            }
        }

        private static Color GetSuggestionConfidenceColor(SuggestionConfidence confidence)
        {
            switch (confidence)
            {
                case SuggestionConfidence.High:
                    return new Color(0.3f, 0.85f, 0.35f, 1f);
                case SuggestionConfidence.Medium:
                    return new Color(1f, 0.78f, 0.2f, 1f);
                default:
                    return new Color(1f, 0.4f, 0.2f, 1f);
            }
        }

        private void BuildValidSurfaceAssignmentLookup()
        {
            if (plan == null)
                return;

            Dictionary<string, List<GroundCollisionSurfaceAssignment>> candidates =
                new Dictionary<string, List<GroundCollisionSurfaceAssignment>>(StringComparer.Ordinal);
            List<GroundCollisionSurfaceAssignment> assignments = plan.SurfaceAssignments;
            for (int i = 0; i < assignments.Count; i++)
            {
                GroundCollisionSurfaceAssignment assignment = assignments[i];
                if (assignment == null ||
                    string.IsNullOrEmpty(assignment.surfaceId) ||
                    !string.Equals(
                        assignment.sourceFingerprint,
                        currentFingerprint,
                        StringComparison.Ordinal) ||
                    !surfacesById.ContainsKey(assignment.surfaceId) ||
                    GetRegionIndexById(assignment.destinationRegionId) < 0)
                {
                    staleSurfaceAssignmentCount++;
                    continue;
                }

                if (!candidates.TryGetValue(
                        assignment.surfaceId,
                        out List<GroundCollisionSurfaceAssignment> surfaceCandidates))
                {
                    surfaceCandidates = new List<GroundCollisionSurfaceAssignment>();
                    candidates.Add(assignment.surfaceId, surfaceCandidates);
                }

                surfaceCandidates.Add(assignment);
            }

            foreach (KeyValuePair<string, List<GroundCollisionSurfaceAssignment>> pair in candidates)
            {
                if (pair.Value.Count != 1)
                {
                    staleSurfaceAssignmentCount += pair.Value.Count;
                    continue;
                }

                validAssignmentsBySurface.Add(pair.Key, pair.Value[0]);
            }

            validSurfaceAssignmentCount = validAssignmentsBySurface.Count;
        }

        private GroundCollisionSurfaceAssignment GetValidSurfaceAssignment(
            SurfaceComponent component)
        {
            if (component == null)
                return null;

            validAssignmentsBySurface.TryGetValue(
                component.SurfaceId,
                out GroundCollisionSurfaceAssignment assignment);
            return assignment;
        }

        private void DrawSurfaceAssignmentControls(SurfaceComponent component)
        {
            if (plan == null || component == null)
                return;

            if (plan.Regions.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Create at least one destination region before assigning a surface.",
                    MessageType.None);
                return;
            }

            int destinationIndex = GetPendingDestinationIndex(component);
            string[] regionNames = new string[plan.Regions.Count];
            for (int i = 0; i < regionNames.Length; i++)
                regionNames[i] = GetRegionName(i);

            using (new EditorGUI.DisabledScope(!PlanMatchesCurrentAnalysis()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int newDestination = EditorGUILayout.Popup(
                        "Assign whole surface to",
                        destinationIndex,
                        regionNames);
                    if (newDestination != destinationIndex)
                    {
                        destinationIndex = newDestination;
                        pendingDestinationBySurface[component.SurfaceId] = destinationIndex;
                    }

                    if (GUILayout.Button("Assign", GUILayout.Width(58f)))
                        AssignSurface(component, destinationIndex);
                }
            }

            if (!PlanMatchesCurrentAnalysis())
            {
                EditorGUILayout.LabelField(
                    "Associate the current analysis with the plan before assigning.",
                    EditorStyles.miniLabel);
            }

            bool hasSavedAssignment = plan.SurfaceAssignments.Exists(assignment =>
                assignment != null &&
                string.Equals(
                    assignment.surfaceId,
                    component.SurfaceId,
                    StringComparison.Ordinal));
            if (hasSavedAssignment && GUILayout.Button("Clear saved assignment for this surface"))
                ClearSurfaceAssignment(component.SurfaceId);
        }

        private int GetPendingDestinationIndex(SurfaceComponent component)
        {
            if (plan == null || plan.Regions.Count == 0)
                return 0;

            if (pendingDestinationBySurface.TryGetValue(component.SurfaceId, out int pendingIndex))
                return Mathf.Clamp(pendingIndex, 0, plan.Regions.Count - 1);

            GroundCollisionSurfaceAssignment assignment = GetValidSurfaceAssignment(component);
            int assignedIndex = assignment != null
                ? GetRegionIndexById(assignment.destinationRegionId)
                : -1;
            if (assignedIndex >= 0)
                return assignedIndex;

            int suggestedIndex = GetDominantRectangleRegion(component);
            return suggestedIndex >= 0 ? suggestedIndex : 0;
        }

        private int GetDominantRectangleRegion(SurfaceComponent component)
        {
            if (plan == null)
                return -1;

            int[] counts = new int[plan.Regions.Count];
            List<int> memberships = new List<int>();
            for (int i = 0; i < component.Cells.Count; i++)
            {
                memberships.Clear();
                int unused;
                GetRegionMembership(component.Cells[i], out unused, memberships);
                for (int membershipIndex = 0;
                     membershipIndex < memberships.Count;
                     membershipIndex++)
                {
                    counts[memberships[membershipIndex]]++;
                }
            }

            int bestIndex = -1;
            int bestCount = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= bestCount)
                    continue;

                bestIndex = i;
                bestCount = counts[i];
            }

            return bestIndex;
        }

        private void AssignSurface(SurfaceComponent component, int destinationRegionIndex)
        {
            if (plan == null ||
                component == null ||
                destinationRegionIndex < 0 ||
                destinationRegionIndex >= plan.Regions.Count ||
                !PlanMatchesCurrentAnalysis())
            {
                return;
            }

            ModifyPlan(
                "Assign collision surface destination",
                () =>
                {
                    EnsureRegionIds();
                    GroundCollisionRegionDefinition destination =
                        plan.Regions[destinationRegionIndex];
                    plan.SurfaceAssignments.RemoveAll(assignment =>
                        assignment != null &&
                        string.Equals(
                            assignment.surfaceId,
                            component.SurfaceId,
                            StringComparison.Ordinal));
                    plan.SurfaceAssignments.Add(new GroundCollisionSurfaceAssignment
                    {
                        surfaceId = component.SurfaceId,
                        sourceFingerprint = currentFingerprint,
                        destinationRegionId = destination.regionId,
                        destinationRegionName = destination.regionName,
                        tileCount = component.Cells.Count,
                        cellBounds = component.Bounds
                    });
                    plan.MarkSchemaCurrent();
                });
        }

        private void ClearSurfaceAssignment(string surfaceId)
        {
            if (plan == null || string.IsNullOrEmpty(surfaceId))
                return;

            ModifyPlan(
                "Clear collision surface destination",
                () => plan.SurfaceAssignments.RemoveAll(assignment =>
                    assignment != null &&
                    string.Equals(assignment.surfaceId, surfaceId, StringComparison.Ordinal)));
        }

        private void EnsureRegionIds()
        {
            if (plan == null)
                return;

            for (int i = 0; i < plan.Regions.Count; i++)
            {
                GroundCollisionRegionDefinition region = plan.Regions[i];
                if (string.IsNullOrEmpty(region.regionId))
                    region.regionId = Guid.NewGuid().ToString("N");
            }
        }

        private int GetRegionIndexById(string regionId)
        {
            if (plan == null || string.IsNullOrEmpty(regionId))
                return -1;

            for (int i = 0; i < plan.Regions.Count; i++)
            {
                if (string.Equals(plan.Regions[i].regionId, regionId, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private string GetRegionNameById(string regionId)
        {
            int regionIndex = GetRegionIndexById(regionId);
            return regionIndex >= 0 ? GetRegionName(regionIndex) : "Missing region";
        }

        private void RevalidateMatchingSurfaceAssignments()
        {
            if (plan == null || !analysisReady || !PlanMatchesCurrentAnalysis())
            {
                ShowNotification(new GUIContent(
                    "Associate the current analysis with the plan before revalidating."));
                return;
            }

            int revalidated = 0;
            ModifyPlan(
                "Revalidate matching collision surfaces",
                () =>
                {
                    List<GroundCollisionSurfaceAssignment> assignments = plan.SurfaceAssignments;
                    for (int i = 0; i < assignments.Count; i++)
                    {
                        GroundCollisionSurfaceAssignment assignment = assignments[i];
                        if (assignment == null ||
                            !surfacesById.TryGetValue(
                                assignment.surfaceId,
                                out SurfaceComponent component) ||
                            GetRegionIndexById(assignment.destinationRegionId) < 0)
                        {
                            continue;
                        }

                        assignment.sourceFingerprint = currentFingerprint;
                        assignment.tileCount = component.Cells.Count;
                        assignment.cellBounds = component.Bounds;
                        assignment.destinationRegionName =
                            GetRegionNameById(assignment.destinationRegionId);
                        revalidated++;
                    }
                });

            ShowNotification(new GUIContent($"Revalidated {revalidated} matching assignment(s)."));
        }

        private void RemoveStaleSurfaceAssignments()
        {
            if (plan == null || staleSurfaceAssignmentCount == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Remove stale surface assignments?",
                    "This removes only inactive saved decisions. It does not change regions, scenes, " +
                    "Tilemaps, or colliders.",
                    "Remove stale",
                    "Cancel"))
            {
                return;
            }

            ModifyPlan(
                "Remove stale collision surface assignments",
                () => plan.SurfaceAssignments.RemoveAll(assignment =>
                {
                    if (assignment == null || string.IsNullOrEmpty(assignment.surfaceId))
                        return true;

                    return !validAssignmentsBySurface.TryGetValue(
                               assignment.surfaceId,
                               out GroundCollisionSurfaceAssignment active) ||
                           !ReferenceEquals(active, assignment);
                }));
        }

        private int GetRegionMembership(Vector3Int cell, out int firstRegion, List<int> results)
        {
            firstRegion = -1;
            if (plan == null)
                return 0;

            int count = 0;
            for (int regionIndex = 0; regionIndex < plan.Regions.Count; regionIndex++)
            {
                GroundCollisionRegionDefinition region = plan.Regions[regionIndex];
                bool contains = false;
                for (int rectangleIndex = 0; rectangleIndex < region.rectangles.Count; rectangleIndex++)
                {
                    BoundsInt bounds = NormalizeBounds(region.rectangles[rectangleIndex]);
                    if (bounds.Contains(cell))
                    {
                        contains = true;
                        break;
                    }
                }

                if (!contains)
                    continue;

                if (firstRegion < 0)
                    firstRegion = regionIndex;
                results?.Add(regionIndex);
                count++;
            }

            return count;
        }

        private void DrawDisplayOverlay()
        {
            if (!showCellOverlay || sourceTilemap == null || !analysisReady || occupiedCells.Count == 0)
                return;

            EnsureOverlayMaterial();
            if (overlayMaterial == null || !overlayMaterial.SetPass(0))
                return;

            GL.PushMatrix();
            GL.MultMatrix(sourceTilemap.transform.localToWorldMatrix);
            GL.Begin(GL.QUADS);

            for (int i = 0; i < occupiedCells.Count; i++)
            {
                Vector3Int cell = occupiedCells[i];
                GL.Color(GetCellOverlayColor(cell));
                GL.Vertex(sourceTilemap.CellToLocal(cell));
                GL.Vertex(sourceTilemap.CellToLocal(cell + Vector3Int.right));
                GL.Vertex(sourceTilemap.CellToLocal(cell + Vector3Int.right + Vector3Int.up));
                GL.Vertex(sourceTilemap.CellToLocal(cell + Vector3Int.up));
            }

            GL.End();
            GL.PopMatrix();
        }

        private Color GetCellOverlayColor(Vector3Int cell)
        {
            SurfaceComponent selectedSurface = GetSelectedSurface();
            if (surfaceHighlightEnabled && selectedSurface != null)
            {
                if (selectedSurface.CellSet.Contains(cell))
                    return new Color(1f, 0.9f, 0.05f, 0.88f);

                return new Color(0.08f, 0.1f, 0.12f, 0.055f);
            }

            if (cellAssignments.TryGetValue(cell, out int assignment) && assignment == -2)
                return new Color(1f, 0.05f, 0.05f, Mathf.Max(overlayOpacity, 0.5f));

            if (crossingCells.Contains(cell))
                return new Color(1f, 0.48f, 0.05f, Mathf.Max(overlayOpacity, 0.45f));

            if (assignment < 0 || plan == null || assignment >= plan.Regions.Count)
                return new Color(0.6f, 0.6f, 0.6f, overlayOpacity);

            Color color = plan.Regions[assignment].color;
            color.a = overlayOpacity;
            return color;
        }

        private void DrawBoundsAndRegions()
        {
            if (sourceTilemap == null || !analysisReady)
                return;

            if (showOccupiedBounds && occupiedCells.Count > 0)
            {
                bool dimmed = surfaceHighlightEnabled && GetSelectedSurface() != null;
                DrawBoundsOutline(
                    occupiedBounds,
                    new Color(0f, 1f, 1f, dimmed ? 0.18f : 1f),
                    dimmed ? 1.5f : 4f);
                if (showRegionLabels && !dimmed)
                    Handles.Label(
                        GetBoundsLabelPosition(occupiedBounds),
                        $"Occupied: {FormatBounds(occupiedBounds)}",
                        EditorStyles.whiteBoldLabel);
            }

            if (plan == null)
                return;

            for (int regionIndex = 0; regionIndex < plan.Regions.Count; regionIndex++)
            {
                GroundCollisionRegionDefinition region = plan.Regions[regionIndex];
                Color outlineColor = region.color;
                bool dimmed = surfaceHighlightEnabled && GetSelectedSurface() != null;
                outlineColor.a = dimmed ? 0.18f : 1f;
                for (int rectangleIndex = 0; rectangleIndex < region.rectangles.Count; rectangleIndex++)
                {
                    BoundsInt bounds = NormalizeBounds(region.rectangles[rectangleIndex]);
                    float thickness = regionIndex == selectedRegionIndex &&
                                      rectangleIndex == selectedRectangleIndex
                        ? 5f
                        : dimmed ? 1f : 2.5f;
                    DrawBoundsOutline(bounds, outlineColor, thickness);

                    if (showRegionLabels && !dimmed)
                        Handles.Label(
                            GetBoundsLabelPosition(bounds),
                            region.regionName,
                            EditorStyles.whiteBoldLabel);
                }
            }
        }

        private void DrawSelectedRectangleHandles()
        {
            if (drawingRegionIndex >= 0 || plan == null || sourceTilemap == null)
                return;
            if (selectedRegionIndex < 0 || selectedRegionIndex >= plan.Regions.Count)
                return;

            GroundCollisionRegionDefinition region = plan.Regions[selectedRegionIndex];
            if (selectedRectangleIndex < 0 || selectedRectangleIndex >= region.rectangles.Count)
                return;

            BoundsInt bounds = NormalizeBounds(region.rectangles[selectedRectangleIndex]);
            Vector3 worldMin = sourceTilemap.CellToWorld(bounds.min);
            Vector3 worldMax = sourceTilemap.CellToWorld(bounds.max);
            Vector3 center = (worldMin + worldMax) * 0.5f;
            float handleSize = HandleUtility.GetHandleSize(center) * 0.075f;
            Handles.color = region.color;

            EditorGUI.BeginChangeCheck();
            Vector3 movedCenter = Handles.FreeMoveHandle(
                center,
                handleSize,
                Vector3.zero,
                Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 projectedCenter = ProjectToTilemapPlane(movedCenter, center);
                Vector3 delta = projectedCenter - center;
                Vector3Int newMin = sourceTilemap.WorldToCell(worldMin + delta);
                newMin.z = bounds.zMin;
                BoundsInt moved = new BoundsInt(newMin, bounds.size);
                SetSelectedRectangle(moved, "Move collision region rectangle");
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 movedMin = Handles.FreeMoveHandle(
                worldMin,
                handleSize,
                Vector3.zero,
                Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3Int newMin = sourceTilemap.WorldToCell(ProjectToTilemapPlane(movedMin, worldMin));
                newMin.x = Mathf.Min(newMin.x, bounds.xMax - 1);
                newMin.y = Mathf.Min(newMin.y, bounds.yMax - 1);
                newMin.z = bounds.zMin;
                BoundsInt resized = new BoundsInt(newMin, bounds.max - newMin);
                resized.size = new Vector3Int(resized.size.x, resized.size.y, bounds.size.z);
                SetSelectedRectangle(resized, "Resize collision region rectangle");
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 movedMax = Handles.FreeMoveHandle(
                worldMax,
                handleSize,
                Vector3.zero,
                Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3Int newMax = sourceTilemap.WorldToCell(ProjectToTilemapPlane(movedMax, worldMax));
                newMax.x = Mathf.Max(newMax.x, bounds.xMin + 1);
                newMax.y = Mathf.Max(newMax.y, bounds.yMin + 1);
                newMax.z = bounds.zMax;
                BoundsInt resized = new BoundsInt(bounds.min, newMax - bounds.min);
                SetSelectedRectangle(resized, "Resize collision region rectangle");
            }
        }

        private void SetSelectedRectangle(BoundsInt bounds, string undoName)
        {
            if (plan == null || selectedRegionIndex < 0 || selectedRegionIndex >= plan.Regions.Count)
                return;

            GroundCollisionRegionDefinition region = plan.Regions[selectedRegionIndex];
            if (selectedRectangleIndex < 0 || selectedRectangleIndex >= region.rectangles.Count)
                return;

            BoundsInt normalized = NormalizeBounds(bounds);
            int rectangleIndex = selectedRectangleIndex;
            ModifyPlan(undoName, () => region.rectangles[rectangleIndex] = normalized);
        }

        private void HandleRectangleDrawing(Event currentEvent)
        {
            if (drawingRegionIndex < 0 || plan == null || sourceTilemap == null)
                return;
            if (drawingRegionIndex >= plan.Regions.Count)
            {
                CancelRectangleDrawing();
                return;
            }

            if (currentEvent.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                CancelRectangleDrawing();
                currentEvent.Use();
                return;
            }

            if (currentEvent.alt || !TryGetMouseCell(currentEvent.mousePosition, out Vector3Int mouseCell))
                return;

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                drawStartCell = mouseCell;
                drawCurrentCell = mouseCell;
                drawingRectangle = true;
                currentEvent.Use();
                SceneView.RepaintAll();
            }
            else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && drawingRectangle)
            {
                drawCurrentCell = mouseCell;
                currentEvent.Use();
                SceneView.RepaintAll();
            }
            else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && drawingRectangle)
            {
                drawCurrentCell = mouseCell;
                BoundsInt bounds = BoundsFromInclusiveCells(drawStartCell, drawCurrentCell);
                int regionIndex = drawingRegionIndex;
                ModifyPlan(
                    "Draw collision region rectangle",
                    () => plan.Regions[regionIndex].rectangles.Add(bounds));
                selectedRegionIndex = regionIndex;
                selectedRectangleIndex = plan.Regions[regionIndex].rectangles.Count - 1;
                CancelRectangleDrawing();
                currentEvent.Use();
            }

            if (drawingRectangle && currentEvent.type == EventType.Repaint)
            {
                BoundsInt previewBounds = BoundsFromInclusiveCells(drawStartCell, drawCurrentCell);
                DrawBoundsOutline(previewBounds, Color.white, 4f);
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (!analysisReady || sourceTilemap == null)
                return;

            Event currentEvent = Event.current;
            HandleRectangleDrawing(currentEvent);

            if (currentEvent.type == EventType.Repaint)
            {
                DrawDisplayOverlay();
                DrawBoundsAndRegions();
                DrawSelectedSurfaceOutline();
            }

            DrawSelectedRectangleHandles();

            if (drawingRegionIndex >= 0)
            {
                Handles.BeginGUI();
                GUI.Box(
                    new Rect(12f, 12f, 360f, 42f),
                    $"Draw region: {GetRegionName(drawingRegionIndex)}\nDrag left mouse; Escape cancels.",
                    EditorStyles.helpBox);
                Handles.EndGUI();
            }

            DrawSelectedSurfaceSceneInfo();
        }

        private void DrawSelectedSurfaceOutline()
        {
            SurfaceComponent component = GetSelectedSurface();
            if (!surfaceHighlightEnabled || component == null || sourceTilemap == null)
                return;

            Color previousColor = Handles.color;
            Handles.color = new Color(1f, 0.92f, 0.05f, 1f);
            for (int i = 0; i < component.Cells.Count; i++)
            {
                Vector3Int cell = component.Cells[i];
                Vector3 bottomLeft = sourceTilemap.CellToWorld(cell);
                Vector3 bottomRight = sourceTilemap.CellToWorld(cell + Vector3Int.right);
                Vector3 topRight = sourceTilemap.CellToWorld(
                    cell + Vector3Int.right + Vector3Int.up);
                Vector3 topLeft = sourceTilemap.CellToWorld(cell + Vector3Int.up);

                if (!component.CellSet.Contains(cell + Vector3Int.down))
                    Handles.DrawAAPolyLine(4f, bottomLeft, bottomRight);
                if (!component.CellSet.Contains(cell + Vector3Int.right))
                    Handles.DrawAAPolyLine(4f, bottomRight, topRight);
                if (!component.CellSet.Contains(cell + Vector3Int.up))
                    Handles.DrawAAPolyLine(4f, topRight, topLeft);
                if (!component.CellSet.Contains(cell + Vector3Int.left))
                    Handles.DrawAAPolyLine(4f, topLeft, bottomLeft);
            }

            Handles.color = previousColor;
        }

        private void DrawSelectedSurfaceSceneInfo()
        {
            SurfaceComponent component = GetSelectedSurface();
            if (!surfaceHighlightEnabled || component == null)
                return;

            float y = drawingRegionIndex >= 0 ? 62f : 12f;
            Handles.BeginGUI();
            GUI.Box(
                new Rect(12f, y, 430f, 64f),
                $"Selected surface {GetShortSurfaceId(component.SurfaceId)}\n" +
                $"{component.Cells.Count} tiles | {GetSurfaceRegionSummary(component)}\n" +
                "Yellow = this connected component; all other tiles are dimmed.",
                EditorStyles.helpBox);
            Handles.EndGUI();
        }

        private bool TryGetMouseCell(Vector2 guiPosition, out Vector3Int cell)
        {
            cell = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            Plane tilemapPlane = new Plane(sourceTilemap.transform.forward, sourceTilemap.transform.position);
            if (!tilemapPlane.Raycast(ray, out float distance))
                return false;

            cell = sourceTilemap.WorldToCell(ray.GetPoint(distance));
            cell.z = occupiedCells.Count > 0 ? occupiedBounds.zMin : 0;
            return true;
        }

        private void DrawBoundsOutline(BoundsInt bounds, Color color, float thickness)
        {
            if (sourceTilemap == null || bounds.size.x <= 0 || bounds.size.y <= 0)
                return;

            Vector3Int min = bounds.min;
            Vector3Int max = bounds.max;
            Vector3[] points =
            {
                sourceTilemap.CellToWorld(new Vector3Int(min.x, min.y, min.z)),
                sourceTilemap.CellToWorld(new Vector3Int(max.x, min.y, min.z)),
                sourceTilemap.CellToWorld(new Vector3Int(max.x, max.y, min.z)),
                sourceTilemap.CellToWorld(new Vector3Int(min.x, max.y, min.z)),
                sourceTilemap.CellToWorld(new Vector3Int(min.x, min.y, min.z))
            };

            Handles.color = color;
            Handles.DrawAAPolyLine(thickness, points);
        }

        private Vector3 GetBoundsLabelPosition(BoundsInt bounds)
        {
            Vector3 min = sourceTilemap.CellToWorld(bounds.min);
            Vector3 max = sourceTilemap.CellToWorld(bounds.max);
            return (min + max) * 0.5f;
        }

        private void FocusBounds(BoundsInt bounds)
        {
            if (sourceTilemap == null || SceneView.lastActiveSceneView == null)
                return;

            Vector3 min = sourceTilemap.CellToWorld(bounds.min);
            Vector3 max = sourceTilemap.CellToWorld(bounds.max);
            Bounds worldBounds = new Bounds((min + max) * 0.5f, Vector3.zero);
            worldBounds.Encapsulate(min);
            worldBounds.Encapsulate(max);
            if (worldBounds.size.z < 1f)
                worldBounds.Expand(new Vector3(0f, 0f, 1f));
            SceneView.lastActiveSceneView.Frame(worldBounds, false);
        }

        private void SelectSurface(SurfaceComponent component, bool focus)
        {
            if (component == null)
                return;

            selectedSurfaceId = component.SurfaceId;
            surfaceHighlightEnabled = true;
            showCellOverlay = true;
            selectedRectangleIndex = -1;
            if (focus)
                FocusSurface(component);
            Repaint();
            SceneView.RepaintAll();
        }

        private SurfaceComponent GetSelectedSurface()
        {
            if (string.IsNullOrEmpty(selectedSurfaceId))
                return null;

            surfacesById.TryGetValue(selectedSurfaceId, out SurfaceComponent component);
            return component;
        }

        private void FocusSurface(SurfaceComponent component)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (component == null || sourceTilemap == null || sceneView == null)
                return;

            bool hasPoint = false;
            Bounds worldBounds = new Bounds();
            for (int i = 0; i < component.Cells.Count; i++)
            {
                Vector3Int cell = component.Cells[i];
                Vector3[] corners =
                {
                    sourceTilemap.CellToWorld(cell),
                    sourceTilemap.CellToWorld(cell + Vector3Int.right),
                    sourceTilemap.CellToWorld(cell + Vector3Int.up),
                    sourceTilemap.CellToWorld(cell + Vector3Int.right + Vector3Int.up)
                };

                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    if (!hasPoint)
                    {
                        worldBounds = new Bounds(corners[cornerIndex], Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        worldBounds.Encapsulate(corners[cornerIndex]);
                    }
                }
            }

            if (!hasPoint)
                return;

            Vector3 margin = worldBounds.size * 0.08f;
            margin.x = Mathf.Max(margin.x, 2f);
            margin.y = Mathf.Max(margin.y, 2f);
            margin.z = Mathf.Max(margin.z, 1f);
            worldBounds.Expand(margin);
            sceneView.Frame(worldBounds, false);
        }

        private string GetSurfaceRegionSummary(SurfaceComponent component)
        {
            GroundCollisionSurfaceAssignment assignment = GetValidSurfaceAssignment(component);
            if (assignment != null)
                return $"{GetRegionNameById(assignment.destinationRegionId)} (manual)";

            return string.IsNullOrEmpty(component.RegionSummary)
                ? "Unassigned"
                : component.RegionSummary;
        }

        private Vector3 ProjectToTilemapPlane(Vector3 point, Vector3 referencePoint)
        {
            Vector3 local = sourceTilemap.transform.InverseTransformPoint(point);
            Vector3 referenceLocal = sourceTilemap.transform.InverseTransformPoint(referencePoint);
            local.z = referenceLocal.z;
            return sourceTilemap.transform.TransformPoint(local);
        }

        private void AddRegion(string regionName, Color color, bool connections)
        {
            if (plan == null)
                return;

            GroundCollisionRegionDefinition region = new GroundCollisionRegionDefinition
            {
                regionId = Guid.NewGuid().ToString("N"),
                regionName = regionName,
                color = color,
                isConnectionsRegion = connections
            };
            ModifyPlan("Add collision region", () => plan.Regions.Add(region));
            selectedRegionIndex = plan.Regions.Count - 1;
            selectedRectangleIndex = -1;
        }

        private void AddSuggestedRegions()
        {
            if (plan == null)
                return;

            ModifyPlan(
                "Add suggested collision regions",
                () =>
                {
                    for (int i = 0; i < SuggestedRegionNames.Length; i++)
                    {
                        string suggestedName = SuggestedRegionNames[i];
                        bool exists = plan.Regions.Exists(region =>
                            string.Equals(region.regionName, suggestedName, StringComparison.OrdinalIgnoreCase));
                        if (exists)
                            continue;

                        plan.Regions.Add(new GroundCollisionRegionDefinition
                        {
                            regionId = Guid.NewGuid().ToString("N"),
                            regionName = suggestedName,
                            color = SuggestedRegionColors[i],
                            isConnectionsRegion = string.Equals(
                                suggestedName,
                                "Conexiones",
                                StringComparison.OrdinalIgnoreCase)
                        });
                    }
                });
        }

        private GroundCollisionRegionDefinition FindConnectionsRegion()
        {
            if (plan == null)
                return null;

            return plan.Regions.Find(region => region.isConnectionsRegion);
        }

        private Color GetNextRegionColor()
        {
            int index = plan != null ? plan.Regions.Count : 0;
            float hue = Mathf.Repeat(index * 0.173f + 0.53f, 1f);
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }

        private void ModifyPlan(string undoName, Action mutation)
        {
            if (plan == null || mutation == null)
                return;

            Undo.RecordObject(plan, undoName);
            mutation();
            EditorUtility.SetDirty(plan);
            RefreshAssignments();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ClearAnalysis()
        {
            analysisReady = false;
            occupiedCells.Clear();
            occupiedCellSet.Clear();
            collidableCellSet.Clear();
            surfaceComponents.Clear();
            surfacesById.Clear();
            cellAssignments.Clear();
            crossingCells.Clear();
            surfaceConflicts.Clear();
            unassignedSurfaces.Clear();
            validAssignmentsBySurface.Clear();
            occupiedBounds = new BoundsInt(Vector3Int.zero, Vector3Int.zero);
            collidableTileCount = 0;
            unassignedTileCount = 0;
            multiplyAssignedTileCount = 0;
            unassignedTilesOutsideCollidableSurfaces = 0;
            staleSurfaceAssignmentCount = 0;
            validSurfaceAssignmentCount = 0;
            currentFingerprint = string.Empty;
            selectedSurfaceId = string.Empty;
            pendingDestinationBySurface.Clear();
            CancelRectangleDrawing();
            SceneView.RepaintAll();
        }

        private void CancelRectangleDrawing()
        {
            drawingRegionIndex = -1;
            drawingRectangle = false;
            SceneView.RepaintAll();
            Repaint();
        }

        private void EnsureOverlayMaterial()
        {
            if (overlayMaterial != null)
                return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return;

            overlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            overlayMaterial.SetInt("_Cull", (int)CullMode.Off);
            overlayMaterial.SetInt("_ZWrite", 0);
        }

        private void DestroyOverlayMaterial()
        {
            if (overlayMaterial == null)
                return;

            DestroyImmediate(overlayMaterial);
            overlayMaterial = null;
        }

        private string GetRegionName(int index)
        {
            if (plan == null || index < 0 || index >= plan.Regions.Count)
                return "Unknown";

            string regionName = plan.Regions[index].regionName;
            return string.IsNullOrWhiteSpace(regionName) ? $"Region {index + 1}" : regionName;
        }

        private static BoundsInt BoundsFromInclusiveCells(Vector3Int first, Vector3Int second)
        {
            int xMin = Mathf.Min(first.x, second.x);
            int yMin = Mathf.Min(first.y, second.y);
            int xMax = Mathf.Max(first.x, second.x);
            int yMax = Mathf.Max(first.y, second.y);
            return CreateBounds(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1, first.z);
        }

        private static BoundsInt CreateBounds(int x, int y, int width, int height, int z = 0)
        {
            return new BoundsInt(x, y, z, Mathf.Max(1, width), Mathf.Max(1, height), 1);
        }

        private static BoundsInt NormalizeBounds(BoundsInt bounds)
        {
            return new BoundsInt(
                bounds.xMin,
                bounds.yMin,
                bounds.zMin,
                Mathf.Max(1, bounds.size.x),
                Mathf.Max(1, bounds.size.y),
                Mathf.Max(1, bounds.size.z));
        }

        private static bool BoundsEqual(BoundsInt left, BoundsInt right)
        {
            return left.position == right.position && left.size == right.size;
        }

        private static string FormatBounds(BoundsInt bounds)
        {
            return $"min ({bounds.xMin}, {bounds.yMin}) size ({bounds.size.x}, {bounds.size.y})";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            List<string> names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private string BuildSurfaceId(List<Vector3Int> sortedCells)
        {
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < sortedCells.Count; i++)
            {
                Vector3Int cell = sortedCells[i];
                HashInt(ref hash, cell.x);
                HashInt(ref hash, cell.y);
                HashInt(ref hash, cell.z);
                HashInt(ref hash, (int)sourceTilemap.GetColliderType(cell));
            }

            return $"surface-{hash:X16}-{sortedCells.Count}";
        }

        private static int CompareCells(Vector3Int left, Vector3Int right)
        {
            int byX = left.x.CompareTo(right.x);
            if (byX != 0)
                return byX;
            int byY = left.y.CompareTo(right.y);
            return byY != 0 ? byY : left.z.CompareTo(right.z);
        }

        private static string GetShortSurfaceId(string surfaceId)
        {
            if (string.IsNullOrEmpty(surfaceId) || surfaceId.Length <= 20)
                return surfaceId;

            return surfaceId.Substring(0, 18) + "…";
        }

        private static void HashInt(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (byte)value;
                hash *= FnvPrime;
                hash ^= (byte)(value >> 8);
                hash *= FnvPrime;
                hash ^= (byte)(value >> 16);
                hash *= FnvPrime;
                hash ^= (byte)(value >> 24);
                hash *= FnvPrime;
            }
        }

        private static void HashString(ref ulong hash, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash ^= (byte)character;
                    hash *= FnvPrime;
                    hash ^= (byte)(character >> 8);
                    hash *= FnvPrime;
                }
            }
        }

        private sealed class SurfaceComponent
        {
            public readonly List<Vector3Int> Cells = new List<Vector3Int>();
            public readonly HashSet<Vector3Int> CellSet = new HashSet<Vector3Int>();
            public string SurfaceId;
            public string RegionSummary;
            public BoundsInt Bounds;
        }

        private sealed class SurfaceConflict
        {
            public SurfaceComponent Component;
            public int UnassignedTileCount;
            public readonly List<string> RegionNames = new List<string>();
        }

        private sealed class UnassignedSurface
        {
            public SurfaceComponent Component;
            public int UnassignedTileCount;
        }

        private enum SuggestionConfidence
        {
            High,
            Medium,
            Ambiguous
        }

        private sealed class SurfaceAssignmentSuggestion
        {
            public SurfaceComponent Component;
            public int UnassignedTileCount;
            public int DestinationRegionIndex;
            public SuggestionConfidence Confidence;
            public string Reason;
            public bool Selected;
            public bool DestinationEdited;
        }
    }
}
