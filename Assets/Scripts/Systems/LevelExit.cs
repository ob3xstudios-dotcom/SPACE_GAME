using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Game.Player;

namespace Game.Systems
{
    [RequireComponent(typeof(Collider2D))]
    public class LevelExit : MonoBehaviour
    {
        private const string SpawnPointPrefix = "Spawn_";

        public static string PendingSpawnPointName { get; private set; }
        public static string PendingSpawnId => PendingSpawnPointName;

        private static bool sceneLoadedSubscribed;

        [Header("Destination")]
        [SerializeField] private string sceneToLoad;
        [FormerlySerializedAs("spawnPointName")]
        [SerializeField] private string targetSpawnId;

        [Header("Detection")]
        [SerializeField] private string playerTag = "Player";

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            PendingSpawnPointName = null;
            sceneLoadedSubscribed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SubscribeSceneLoaded()
        {
            if (sceneLoadedSubscribed) return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrWhiteSpace(PendingSpawnPointName)) return;

            SpawnPoint spawnPoint = FindSpawnPoint(scene, PendingSpawnPointName);
            Transform spawnTransform = spawnPoint != null ? spawnPoint.transform : FindLegacySpawnTransform(scene, PendingSpawnPointName);

            if (spawnTransform == null)
            {
                Debug.LogWarning($"[LEVEL EXIT] Spawn point not found: {PendingSpawnPointName}");
                PendingSpawnPointName = null;
                return;
            }

            GameObject player = FindPlayer(scene);
            if (player == null)
            {
                Debug.LogWarning($"[LEVEL EXIT] Player not found for spawn point: {spawnTransform.name}");
                PendingSpawnPointName = null;
                return;
            }

            MovePlayerToSpawn(player, spawnTransform, spawnPoint);
            PendingSpawnPointName = null;
        }

        private static SpawnPoint FindSpawnPoint(Scene scene, string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId)) return null;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                SpawnPoint[] spawnPoints = roots[i].GetComponentsInChildren<SpawnPoint>(true);
                for (int j = 0; j < spawnPoints.Length; j++)
                {
                    SpawnPoint candidate = spawnPoints[j];
                    if (candidate == null) continue;
                    if (string.Equals(candidate.SpawnId, spawnId, System.StringComparison.Ordinal))
                        return candidate;
                }
            }

            return null;
        }

        private static Transform FindLegacySpawnTransform(Scene scene, string spawnPointName)
        {
            if (string.IsNullOrWhiteSpace(spawnPointName)) return null;

            string prefixedName = spawnPointName.StartsWith(SpawnPointPrefix)
                ? spawnPointName
                : SpawnPointPrefix + spawnPointName;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform root = roots[i].transform;

                Transform exact = FindTransformByName(root, spawnPointName);
                if (exact != null) return exact;

                Transform prefixed = FindTransformByName(root, prefixedName);
                if (prefixed != null) return prefixed;
            }

            return null;
        }

        private static GameObject FindPlayer(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root.CompareTag("Player")) return root;

                PlayerController controller = root.GetComponentInChildren<PlayerController>(true);
                if (controller != null) return controller.gameObject;

                PlayerHealth health = root.GetComponentInChildren<PlayerHealth>(true);
                if (health != null) return health.gameObject;
            }

            return null;
        }

        private static Transform FindTransformByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName)) return null;
            if (root.name == targetName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransformByName(root.GetChild(i), targetName);
                if (found != null) return found;
            }

            return null;
        }

        private static void MovePlayerToSpawn(GameObject player, Transform spawnTransform, SpawnPoint spawnPoint)
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>() ?? player.GetComponentInChildren<PlayerHealth>(true);
            if (spawnPoint == null)
            {
                MovePlayerToSpawn(player, spawnTransform);
                return;
            }

            PlayerController controller = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);
            if (health != null)
                health.RespawnAt(spawnTransform, false);
            else if (controller != null)
                MovePlayerWithController(player, controller, spawnTransform.position);
            else
                MovePlayerTransformOnly(player, spawnTransform.position);

            if (controller != null && spawnPoint.HasFacingDirection)
                controller.FaceDirection(spawnPoint.FacingSign);
        }

        private static void MovePlayerToSpawn(GameObject player, Transform spawnPoint)
        {
            PlayerController controller = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);
            MovePlayerWithController(player, controller, spawnPoint.position);
        }

        private static void MovePlayerWithController(GameObject player, PlayerController controller, Vector3 position)
        {
            if (controller != null)
            {
                controller.enabled = true;
                controller.ResetTransientState();
            }

            Rigidbody2D rb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInChildren<Rigidbody2D>(true);
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.position = position;
                return;
            }

            player.transform.position = position;
        }

        private static void MovePlayerTransformOnly(GameObject player, Vector3 position)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInChildren<Rigidbody2D>(true);
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.position = position;
                return;
            }

            player.transform.position = position;
        }

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
        }

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null && !col.isTrigger)
                col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (loading) return;
            if (!IsPlayer(other)) return;

            LoadDestination();
        }

        private bool IsPlayer(Collider2D other)
        {
            if (other == null) return false;
            if (other.CompareTag(playerTag)) return true;

            Transform root = other.transform.root;
            if (root != null && root.CompareTag(playerTag)) return true;

            return other.GetComponentInParent<PlayerController>() != null
                || other.GetComponentInParent<PlayerHealth>() != null;
        }

        private void LoadDestination()
        {
            if (string.IsNullOrWhiteSpace(sceneToLoad))
            {
                Debug.LogWarning($"[LEVEL EXIT] {name} has no sceneToLoad.");
                return;
            }

            loading = true;
            PendingSpawnPointName = targetSpawnId;

            if (debugLogs)
                Debug.Log($"[LEVEL EXIT] Loading scene={sceneToLoad} spawn={targetSpawnId}");

            SceneManager.LoadScene(sceneToLoad);
        }
    }
}
