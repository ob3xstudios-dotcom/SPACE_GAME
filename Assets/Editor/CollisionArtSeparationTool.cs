using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.EditorTools
{
    public static class CollisionArtSeparationTool
    {
        private const string RootName = "Gameplay Tilemaps";
        private const string GroundMapName = "Ground Collision Tilemap";
        private const string WallMapName = "Wall Collision Tilemap";
        private const string CollisionTilePath = "Assets/Generated/Collision/GameplayCollisionTile.asset";
        private const string EnvironmentSortingLayer = "Environment Art";
        private const string ForegroundSortingLayer = "Foreground";
        private const string CollisionSortingLayer = "Collision Hidden";

        [MenuItem("Tools/SpaceGame/Collision/Build Gameplay Tilemaps From Ground Wall Art")]
        public static void BuildGameplayTilemapsFromArt()
        {
            EnsureSortingLayers();
            Tile collisionTile = EnsureCollisionTile();
            Grid grid = EnsureGridRoot();
            Tilemap groundMap = EnsureCollisionTilemap(grid.transform, GroundMapName, LayerMask.NameToLayer("Ground"));
            Tilemap wallMap = EnsureCollisionTilemap(grid.transform, WallMapName, LayerMask.NameToLayer("Wall"));

            groundMap.ClearAllTiles();
            wallMap.ClearAllTiles();

            int converted = 0;
            int skipped = 0;
            foreach (Collider2D collider in Object.FindObjectsOfType<Collider2D>())
            {
                if (!IsConvertibleCollider(collider))
                {
                    skipped++;
                    continue;
                }

                Tilemap targetMap = collider.gameObject.layer == LayerMask.NameToLayer("Wall") ? wallMap : groundMap;
                if (!TryGetColliderBounds(collider, out Bounds bounds))
                {
                    skipped++;
                    continue;
                }

                PaintBounds(targetMap, collisionTile, bounds);
                DisableGameplayCollider(collider);
                MoveRendererToArtLayer(collider);
                converted++;
            }

            CompressBounds(groundMap);
            CompressBounds(wallMap);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                $"[CollisionArtSeparation] Built gameplay tilemaps. Converted={converted}, skipped={skipped}. " +
                "Original sprites remain visual only; Ground/Wall gameplay now lives on TilemapCollider2D + CompositeCollider2D.");
        }

        private static bool IsConvertibleCollider(Collider2D collider)
        {
            if (collider == null || collider is TilemapCollider2D)
                return false;

            if (collider.transform.GetComponentInParent<Tilemap>() != null)
                return false;

            if (collider.GetComponentInParent<Grid>() != null &&
                collider.transform.root != null &&
                collider.transform.root.name == RootName)
                return false;

            int layer = collider.gameObject.layer;
            return layer == LayerMask.NameToLayer("Ground") || layer == LayerMask.NameToLayer("Wall");
        }

        private static Tile EnsureCollisionTile()
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(CollisionTilePath);
            if (tile != null)
                return tile;

            string directory = Path.GetDirectoryName(CollisionTilePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            tile = ScriptableObject.CreateInstance<Tile>();
            tile.colliderType = Tile.ColliderType.Grid;
            AssetDatabase.CreateAsset(tile, CollisionTilePath);
            AssetDatabase.SaveAssets();
            return tile;
        }

        private static Grid EnsureGridRoot()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName, typeof(Grid));
                Undo.RegisterCreatedObjectUndo(root, "Create gameplay tilemap root");
            }

            Grid grid = root.GetComponent<Grid>();
            if (grid == null)
                grid = Undo.AddComponent<Grid>(root);

            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            return grid;
        }

        private static Tilemap EnsureCollisionTilemap(Transform parent, string name, int layer)
        {
            Transform existing = parent.Find(name);
            GameObject mapObject = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(mapObject, $"Create {name}");
                mapObject.transform.SetParent(parent, false);
            }

            mapObject.layer = layer;

            Tilemap tilemap = mapObject.GetComponent<Tilemap>();
            if (tilemap == null)
                tilemap = Undo.AddComponent<Tilemap>(mapObject);

            TilemapRenderer renderer = mapObject.GetComponent<TilemapRenderer>();
            if (renderer == null)
                renderer = Undo.AddComponent<TilemapRenderer>(mapObject);

            renderer.enabled = false;
            renderer.sortingLayerName = CollisionSortingLayer;

            Rigidbody2D rb = mapObject.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = Undo.AddComponent<Rigidbody2D>(mapObject);

            rb.bodyType = RigidbodyType2D.Static;

            CompositeCollider2D composite = mapObject.GetComponent<CompositeCollider2D>();
            if (composite == null)
                composite = Undo.AddComponent<CompositeCollider2D>(mapObject);

            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            TilemapCollider2D collider = mapObject.GetComponent<TilemapCollider2D>();
            if (collider == null)
                collider = Undo.AddComponent<TilemapCollider2D>(mapObject);

            collider.usedByComposite = true;

            return tilemap;
        }

        private static bool TryGetColliderBounds(Collider2D collider, out Bounds bounds)
        {
            if (collider is BoxCollider2D box)
            {
                Vector2 half = box.size * 0.5f;
                Vector2 offset = box.offset;
                Vector3[] corners =
                {
                    new Vector3(offset.x - half.x, offset.y - half.y, 0f),
                    new Vector3(offset.x - half.x, offset.y + half.y, 0f),
                    new Vector3(offset.x + half.x, offset.y - half.y, 0f),
                    new Vector3(offset.x + half.x, offset.y + half.y, 0f)
                };

                bounds = new Bounds(box.transform.TransformPoint(corners[0]), Vector3.zero);
                for (int i = 1; i < corners.Length; i++)
                    bounds.Encapsulate(box.transform.TransformPoint(corners[i]));

                return true;
            }

            bounds = collider.bounds;
            return bounds.size.sqrMagnitude > 0f;
        }

        private static void PaintBounds(Tilemap tilemap, Tile tile, Bounds bounds)
        {
            Vector3Int min = tilemap.WorldToCell(bounds.min);
            Vector3Int max = tilemap.WorldToCell(bounds.max);

            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    Vector3 cellCenter = tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));
                    if (bounds.Contains(cellCenter))
                        tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        private static void DisableGameplayCollider(Collider2D collider)
        {
            Undo.RecordObject(collider, "Disable art collider");
            collider.enabled = false;
        }

        private static void MoveRendererToArtLayer(Collider2D collider)
        {
            SpriteRenderer renderer = collider.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = collider.GetComponentInChildren<SpriteRenderer>();

            if (renderer == null)
                return;

            Undo.RecordObject(renderer, "Move renderer to art sorting layer");
            bool wall = collider.gameObject.layer == LayerMask.NameToLayer("Wall");
            renderer.sortingLayerName = wall ? ForegroundSortingLayer : EnvironmentSortingLayer;
            renderer.sortingOrder = wall ? 20 : 0;
        }

        private static void CompressBounds(Tilemap tilemap)
        {
            tilemap.RefreshAllTiles();
            tilemap.CompressBounds();
        }

        private static void EnsureSortingLayers()
        {
            EnsureSortingLayer("Background");
            EnsureSortingLayer(EnvironmentSortingLayer);
            EnsureSortingLayer("Characters");
            EnsureSortingLayer(ForegroundSortingLayer);
            EnsureSortingLayer(CollisionSortingLayer);
        }

        private static void EnsureSortingLayer(string layerName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");

            for (int i = 0; i < sortingLayers.arraySize; i++)
            {
                SerializedProperty layer = sortingLayers.GetArrayElementAtIndex(i);
                if (layer.FindPropertyRelative("name").stringValue == layerName)
                    return;
            }

            sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
            SerializedProperty newLayer = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
            newLayer.FindPropertyRelative("name").stringValue = layerName;
            newLayer.FindPropertyRelative("uniqueID").intValue = GenerateSortingLayerId(layerName);
            newLayer.FindPropertyRelative("locked").intValue = 0;
            tagManager.ApplyModifiedProperties();
        }

        private static int GenerateSortingLayerId(string layerName)
        {
            int hash = layerName.GetHashCode();
            return hash == 0 ? 1 : Mathf.Abs(hash);
        }
    }
}
