using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools.GroundCollisionRegions
{
    [Serializable]
    public sealed class GroundCollisionRegionDefinition
    {
        public string regionId = string.Empty;
        public string regionName = "New Region";
        public Color color = new Color(0.2f, 0.75f, 1f, 1f);
        public bool isConnectionsRegion;
        public List<BoundsInt> rectangles = new List<BoundsInt>();
    }

    [Serializable]
    public sealed class GroundCollisionSurfaceAssignment
    {
        public string surfaceId = string.Empty;
        public string sourceFingerprint = string.Empty;
        public string destinationRegionId = string.Empty;
        public string destinationRegionName = string.Empty;
        public int tileCount;
        public BoundsInt cellBounds;
    }

    [CreateAssetMenu(
        fileName = "GroundCollisionRegionPlan",
        menuName = "SpaceGame/Collision/Ground Collision Region Plan")]
    public sealed class GroundCollisionRegionPlan : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string sourceScenePath = string.Empty;
        [SerializeField] private string sourceSceneGuid = string.Empty;
        [SerializeField] private string sourceTilemapHierarchyPath = string.Empty;
        [SerializeField] private BoundsInt occupiedCellBounds;
        [SerializeField] private int occupiedTileCount;
        [SerializeField] private int collidableTileCount;
        [SerializeField] private string sourceFingerprint = string.Empty;
        [SerializeField] private List<GroundCollisionRegionDefinition> regions =
            new List<GroundCollisionRegionDefinition>();
        [SerializeField] private List<GroundCollisionSurfaceAssignment> surfaceAssignments =
            new List<GroundCollisionSurfaceAssignment>();

        public int SchemaVersion => schemaVersion;
        public string SourceScenePath => sourceScenePath;
        public string SourceSceneGuid => sourceSceneGuid;
        public string SourceTilemapHierarchyPath => sourceTilemapHierarchyPath;
        public BoundsInt OccupiedCellBounds => occupiedCellBounds;
        public int OccupiedTileCount => occupiedTileCount;
        public int CollidableTileCount => collidableTileCount;
        public string SourceFingerprint => sourceFingerprint;
        public List<GroundCollisionRegionDefinition> Regions => regions;
        public List<GroundCollisionSurfaceAssignment> SurfaceAssignments
        {
            get
            {
                if (surfaceAssignments == null)
                    surfaceAssignments = new List<GroundCollisionSurfaceAssignment>();
                return surfaceAssignments;
            }
        }

        public void MarkSchemaCurrent()
        {
            schemaVersion = CurrentSchemaVersion;
        }

        public void SetSourceSnapshot(
            string scenePath,
            string sceneGuid,
            string hierarchyPath,
            BoundsInt cellBounds,
            int tileCount,
            int colliderTileCount,
            string fingerprint)
        {
            schemaVersion = CurrentSchemaVersion;
            sourceScenePath = scenePath ?? string.Empty;
            sourceSceneGuid = sceneGuid ?? string.Empty;
            sourceTilemapHierarchyPath = hierarchyPath ?? string.Empty;
            occupiedCellBounds = cellBounds;
            occupiedTileCount = tileCount;
            collidableTileCount = colliderTileCount;
            sourceFingerprint = fingerprint ?? string.Empty;
        }
    }
}
