using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceWorld.Tests.PlayMode
{
    [Category("DeathVolume2D")]
    public sealed class DeathVolume2DPlayModeTests
    {
        private const string SceneName = "MUELLE";
        private const float Timeout = 5f;
        private const string SceneFallRespawnTypeName =
            "Game.Systems.SceneFallRespawn, Assembly-CSharp";
        private const string DeathVolumeTypeName =
            "Game.World.Traps.Generic.DeathVolume2D, Assembly-CSharp";
        private const string PlayerHealthTypeName =
            "Game.Player.PlayerHealth, Assembly-CSharp";

        private Type sceneFallRespawnType;
        private Type deathVolumeType;
        private Type playerHealthType;
        private GameObject player;
        private Rigidbody2D playerBody;
        private Component coordinator;
        private Component playerHealth;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return LoadFreshMuelle();

            sceneFallRespawnType = RequireType(SceneFallRespawnTypeName);
            deathVolumeType = RequireType(DeathVolumeTypeName);
            playerHealthType = RequireType(PlayerHealthTypeName);

            player = GameObject.FindGameObjectWithTag("Player");
            Assert.That(player, Is.Not.Null, "MUELLE has no active Player.");
            playerBody = player.GetComponent<Rigidbody2D>();
            Assert.That(playerBody, Is.Not.Null, "The Player has no Rigidbody2D.");
            playerHealth = player.GetComponent(playerHealthType);
            Assert.That(playerHealth, Is.Not.Null, "The Player has no PlayerHealth.");

            GameObject coordinatorObject = GameObject.Find("SceneFallRespawn_MUELLE");
            Assert.That(coordinatorObject, Is.Not.Null, "MUELLE has no SceneFallRespawn_MUELLE object.");
            coordinator = coordinatorObject.GetComponent(sceneFallRespawnType);
            Assert.That(coordinator, Is.Not.Null, "SceneFallRespawn_MUELLE has no SceneFallRespawn component.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledGlobalFallCheckDoesNotRespawnBelowFallY()
        {
            SetPrivateField(coordinator, "enableGlobalFallCheck", false);
            float fallY = (float)GetPrivateField(coordinator, "fallY");

            Behaviour controller = player.GetComponent(
                RequireType("Game.Player.PlayerController, Assembly-CSharp")) as Behaviour;
            Assert.That(controller, Is.Not.Null, "The Player has no PlayerController.");
            controller.enabled = false;
            playerBody.gravityScale = 0f;
            playerBody.velocity = Vector2.zero;
            playerBody.position = new Vector2(playerBody.position.x, fallY - 5f);
            Physics2D.SyncTransforms();

            yield return new WaitForSecondsRealtime(0.6f);

            Assert.That(playerBody.position.y, Is.LessThan(fallY - 4f),
                "Disabling the global fall check still respawned the Player below fallY.");
        }

        [UnityTest]
        public IEnumerator VolumeDeduplicatesPlayerCollidersAndRearmsForAnotherVolume()
        {
            SetPrivateField(coordinator, "enableGlobalFallCheck", false);

            int respawnCount = 0;
            Action handleRespawned = () => respawnCount++;
            EventInfo respawnedEvent = playerHealthType.GetEvent("OnRespawned");
            Assert.That(respawnedEvent, Is.Not.Null, "PlayerHealth.OnRespawned was not found.");
            respawnedEvent.AddEventHandler(playerHealth, handleRespawned);

            try
            {
                GameObject extraColliderObject = new GameObject("DeathVolumeTest_PlayerChildCollider");
                extraColliderObject.layer = player.layer;
                extraColliderObject.transform.SetParent(player.transform, false);
                BoxCollider2D extraCollider = extraColliderObject.AddComponent<BoxCollider2D>();
                extraCollider.isTrigger = true;
                extraCollider.size = new Vector2(0.25f, 0.25f);

                Vector2 firstVolumePosition = playerBody.position + new Vector2(8f, 4f);
                GameObject firstVolume = CreateDeathVolume("DeathVolumeTest_First", firstVolumePosition);

                playerBody.velocity = Vector2.zero;
                playerBody.position = firstVolumePosition;
                Physics2D.SyncTransforms();

                Collider2D[] contacts = Physics2D.OverlapBoxAll(firstVolumePosition, new Vector2(6f, 6f), 0f);
                int playerColliderCount = 0;
                for (int i = 0; i < contacts.Length; i++)
                {
                    if (contacts[i] != null &&
                        (contacts[i].transform == player.transform ||
                         contacts[i].transform.IsChildOf(player.transform)))
                    {
                        playerColliderCount++;
                    }
                }

                Assert.That(playerColliderCount, Is.GreaterThanOrEqualTo(2),
                    "The test did not expose the volume to multiple Player colliders.");

                yield return WaitFor(() => respawnCount >= 1, "first DeathVolume2D respawn");
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(respawnCount, Is.EqualTo(1),
                    "Multiple Player colliders produced duplicate fatal-fall requests.");

                GameObject respawnPoint = GameObject.Find("RespawnPointStart");
                Assert.That(respawnPoint, Is.Not.Null, "MUELLE has no RespawnPointStart.");
                CreateDeathVolume("DeathVolumeTest_Second", respawnPoint.transform.position);
                Physics2D.SyncTransforms();

                yield return WaitFor(() => respawnCount >= 2, "second DeathVolume2D respawn");
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(respawnCount, Is.EqualTo(2),
                    "The second volume duplicated the fatal fall or the coordinator did not rearm cleanly.");
            }
            finally
            {
                respawnedEvent.RemoveEventHandler(playerHealth, handleRespawned);
            }
        }

        [UnityTest]
        public IEnumerator MissingGameManagerKeepsEmergencySceneReload()
        {
            GameObject gameManager = GameObject.Find("GameManager");
            Assert.That(gameManager, Is.Not.Null, "MUELLE has no GameManager to remove for this test.");
            int previousCoordinatorId = coordinator.GetInstanceID();

            UnityEngine.Object.Destroy(gameManager);
            yield return null;

            int reloadCount = 0;
            void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (scene.name == SceneName)
                    reloadCount++;
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            try
            {
                MethodInfo requestMethod = sceneFallRespawnType.GetMethod("RequestFatalFall");
                Assert.That(requestMethod, Is.Not.Null, "SceneFallRespawn.RequestFatalFall was not found.");
                bool accepted = (bool)requestMethod.Invoke(coordinator, new object[] { player.transform });
                Assert.That(accepted, Is.True, "The coordinator rejected the emergency fatal fall.");

                yield return WaitFor(() => reloadCount > 0, "emergency MUELLE reload");

                GameObject reloadedCoordinatorObject = GameObject.Find("SceneFallRespawn_MUELLE");
                Assert.That(reloadedCoordinatorObject, Is.Not.Null,
                    "MUELLE reloaded without SceneFallRespawn_MUELLE.");
                Component reloadedCoordinator = reloadedCoordinatorObject.GetComponent(sceneFallRespawnType);
                Assert.That(reloadedCoordinator, Is.Not.Null);
                Assert.That(reloadedCoordinator.GetInstanceID(), Is.Not.EqualTo(previousCoordinatorId),
                    "The emergency path did not create a fresh scene coordinator.");
            }
            finally
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
            }
        }

        private GameObject CreateDeathVolume(string objectName, Vector2 position)
        {
            GameObject volume = new GameObject(objectName);
            volume.transform.position = position;
            BoxCollider2D box = volume.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(6f, 6f);
            Component component = volume.AddComponent(deathVolumeType);
            Assert.That(component, Is.Not.Null, $"Could not add {deathVolumeType.FullName}.");
            return volume;
        }

        private static IEnumerator LoadFreshMuelle()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null, "Could not start loading MUELLE.");
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(operation.isDone, Is.True, "MUELLE did not load within 15 seconds.");
            yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> predicate, string operation)
        {
            float deadline = Time.realtimeSinceStartup + Timeout;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(predicate(), Is.True, $"Timed out waiting for {operation}.");
        }

        private static Type RequireType(string assemblyQualifiedName)
        {
            Type type = Type.GetType(assemblyQualifiedName);
            Assert.That(type, Is.Not.Null, $"Could not resolve {assemblyQualifiedName}.");
            return type;
        }

        private static object GetPrivateField(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field {fieldName} was not found on {target.GetType().Name}.");
            return field.GetValue(target);
        }

        private static void SetPrivateField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }

    [Category("DeathVolume2D")]
    public sealed class DeathVolume2DPrefabTests
    {
        private const string PrefabPath = "Assets/Prefabs/Traps/Generic/PF_DeathVolume2D.prefab";

        [Test]
        public void PrefabHasTriggerAndSerializedColliderReference()
        {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Prefab was not found at {PrefabPath}.");

            BoxCollider2D box = prefab.GetComponent<BoxCollider2D>();
            Assert.That(box, Is.Not.Null, "PF_DeathVolume2D has no BoxCollider2D.");
            Assert.That(box.isTrigger, Is.True, "PF_DeathVolume2D's collider is not a trigger.");

            Type componentType = Type.GetType(
                "Game.World.Traps.Generic.DeathVolume2D, Assembly-CSharp");
            Assert.That(componentType, Is.Not.Null, "DeathVolume2D did not compile.");
            Component component = prefab.GetComponent(componentType);
            Assert.That(component, Is.Not.Null, "PF_DeathVolume2D has no DeathVolume2D component.");

            SerializedObject serializedComponent = new SerializedObject(component);
            Assert.That(
                serializedComponent.FindProperty("deathCollider").objectReferenceValue,
                Is.SameAs(box),
                "PF_DeathVolume2D does not serialize its BoxCollider2D reference.");
            Assert.That(
                serializedComponent.FindProperty("targetLayers").intValue,
                Is.EqualTo(1 << 9),
                "PF_DeathVolume2D does not target the Player layer by default.");
#else
            Assert.Ignore("Prefab asset validation requires the Unity Editor.");
#endif
        }
    }
}
