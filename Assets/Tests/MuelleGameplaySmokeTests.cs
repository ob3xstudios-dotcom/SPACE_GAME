using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SpaceWorld.Tests.PlayMode
{
    [Category("GameplaySmoke")]
    public sealed class MuelleGameplaySmokeTests
    {
        private const string SceneName = "MUELLE";
        private const string PlayerTag = "Player";
        private const float SceneLoadTimeout = 15f;
        private const float SettleTimeout = 6f;
        private const float ActionTimeout = 5f;
        private const float InputPipelineTimeout = 1f;

        private Keyboard keyboard;
        private GameObject player;
        private Rigidbody2D playerBody;
        private Behaviour playerController;
        private Behaviour inputReader;
        private PropertyInfo moveInputProperty;
        private PropertyInfo jumpHeldProperty;
        private InputSettings.BackgroundBehavior originalBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
        private bool inputSettingsOverridden;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            bool setupCompleted = false;
            try
            {
                yield return LoadFreshMuelle();
                ResolvePlayer();
                OverrideInputFocusPolicy();

                keyboard = InputSystem.AddDevice<Keyboard>();
                Assert.That(keyboard, Is.Not.Null, "Could not create the test keyboard.");
                Assert.That(keyboard.added, Is.True,
                    $"The synthetic keyboard was not added. {GetInputDiagnostics(Key.D)}");
                Assert.That(keyboard.enabled, Is.True,
                    $"The synthetic keyboard was disabled immediately after creation. {GetInputDiagnostics(Key.D)}");

                yield return WaitForPlayerToStabilize("initial scene load");
                setupCompleted = true;
            }
            finally
            {
                if (!setupCompleted)
                    CleanupAfterFailedSetup();
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (keyboard != null && keyboard.added)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    yield return null;
                }
            }
            finally
            {
                try
                {
                    if (keyboard != null && keyboard.added)
                        InputSystem.RemoveDevice(keyboard);
                }
                finally
                {
                    try
                    {
                        RestoreInputFocusPolicy();
                    }
                    finally
                    {
                        ClearRuntimeReferences();
                        Time.timeScale = 1f;
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator PlayerMovesAndJumpsFromInput()
        {
            float startX = playerBody.position.x;
            float maxVelocityX = playerBody.velocity.x;
            bool moved = false;
            float deadline;

            yield return PressKeyAndWaitForInputPipeline(Key.D, playerBody.position, "initial right movement");
            deadline = Time.realtimeSinceStartup + ActionTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                maxVelocityX = Mathf.Max(maxVelocityX, playerBody.velocity.x);
                if (playerBody.position.x - startX >= 0.4f && maxVelocityX >= 0.2f)
                {
                    moved = true;
                    break;
                }

                yield return null;
            }

            Assert.That(moved, Is.True,
                $"Player did not move right through input within {ActionTimeout:0.0}s. " +
                $"deltaX={playerBody.position.x - startX:0.000}, maxVelocityX={maxVelocityX:0.000}. " +
                GetInputDiagnostics(Key.D));

            yield return ReleaseKeyAndWaitForInputReader(Key.D, "after initial right movement");

            yield return WaitForPlayerToStabilize("after releasing Move");

            float baselineY = playerBody.position.y;
            float maxVelocityY = playerBody.velocity.y;
            float maxY = baselineY;
            bool jumped = false;

            yield return PressKeyAndWaitForInputPipeline(Key.Space, playerBody.position, "jump");
            deadline = Time.realtimeSinceStartup + ActionTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                maxVelocityY = Mathf.Max(maxVelocityY, playerBody.velocity.y);
                maxY = Mathf.Max(maxY, playerBody.position.y);
                if (maxVelocityY >= 0.5f && maxY - baselineY >= 0.15f)
                {
                    jumped = true;
                    break;
                }

                yield return null;
            }

            Assert.That(jumped, Is.True,
                $"Player did not jump through input within {ActionTimeout:0.0}s. " +
                $"rise={maxY - baselineY:0.000}, maxVelocityY={maxVelocityY:0.000}. " +
                GetInputDiagnostics(Key.Space));

            yield return ReleaseKeyAndWaitForInputReader(Key.Space, "after jump");
        }

        [UnityTest]
        public IEnumerator CameraFollowsRuntimePlayerMotion()
        {
            Camera mainCamera = Camera.main;
            Assert.That(mainCamera, Is.Not.Null, "MUELLE has no active MainCamera in Play Mode.");
            yield return WaitForCameraToStabilize(mainCamera);

            float startPlayerX = playerBody.position.x;
            float startCameraX = mainCamera.transform.position.x;
            float maxPlayerDelta = 0f;
            float maxCameraDelta = 0f;
            bool cameraResponded = false;
            float deadline;

            yield return PressKeyAndWaitForInputPipeline(Key.D, playerBody.position, "camera follow movement");
            deadline = Time.realtimeSinceStartup + ActionTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                maxPlayerDelta = Mathf.Max(maxPlayerDelta, playerBody.position.x - startPlayerX);
                maxCameraDelta = Mathf.Max(maxCameraDelta, mainCamera.transform.position.x - startCameraX);
                if (maxPlayerDelta >= 1f && maxCameraDelta >= 0.1f)
                {
                    cameraResponded = true;
                    break;
                }

                yield return null;
            }

            Assert.That(cameraResponded, Is.True,
                $"Main Camera did not follow rightward Player motion within {ActionTimeout:0.0}s. " +
                $"playerDeltaX={maxPlayerDelta:0.000}, cameraDeltaX={maxCameraDelta:0.000}. " +
                GetInputDiagnostics(Key.D));

            yield return ReleaseKeyAndWaitForInputReader(Key.D, "after camera follow movement");
        }

        [UnityTest]
        public IEnumerator FallRespawnsAtMuelleStart()
        {
            GameObject respawnPoint = GameObject.Find("RespawnPointStart");
            Assert.That(respawnPoint, Is.Not.Null,
                "MUELLE's configured RespawnPointStart was not active in Play Mode.");

            Vector2 expectedRespawn = respawnPoint.transform.position;
            playerBody.position = new Vector2(playerBody.position.x, -40f);
            Physics2D.SyncTransforms();

            bool respawned = false;
            float closestDistance = float.PositiveInfinity;
            float deadline = Time.realtimeSinceStartup + ActionTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                float distance = Vector2.Distance(playerBody.position, expectedRespawn);
                closestDistance = Mathf.Min(closestDistance, distance);
                if (distance <= 0.75f && playerBody.position.y > -35f)
                {
                    respawned = true;
                    break;
                }

                yield return null;
            }

            Assert.That(respawned, Is.True,
                $"Player did not respawn near RespawnPointStart within {ActionTimeout:0.0}s. " +
                $"closestDistance={closestDistance:0.000}, currentPosition={playerBody.position}.");
            Assert.That(playerBody.velocity.magnitude, Is.LessThan(1f),
                $"Player movement was not reset by respawn. velocity={playerBody.velocity}.");
            Assert.That(playerController.enabled, Is.True,
                "PlayerController was not enabled after respawn.");

            yield return WaitForPlayerToStabilize("after respawn");

            float startX = playerBody.position.x;
            float maxVelocityX = playerBody.velocity.x;
            bool controlRecovered = false;

            yield return PressKeyAndWaitForInputPipeline(Key.D, playerBody.position, "movement after respawn");
            deadline = Time.realtimeSinceStartup + ActionTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                maxVelocityX = Mathf.Max(maxVelocityX, playerBody.velocity.x);
                if (playerBody.position.x - startX >= 0.25f && maxVelocityX >= 0.2f)
                {
                    controlRecovered = true;
                    break;
                }

                yield return null;
            }

            Assert.That(controlRecovered, Is.True,
                $"Player did not recover input-driven movement after respawn within {ActionTimeout:0.0}s. " +
                $"deltaX={playerBody.position.x - startX:0.000}, maxVelocityX={maxVelocityX:0.000}. " +
                GetInputDiagnostics(Key.D));

            yield return ReleaseKeyAndWaitForInputReader(Key.D, "after movement following respawn");
        }

        private IEnumerator LoadFreshMuelle()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null,
                "SceneManager could not start loading MUELLE. Verify Build Settings.");

            float deadline = Time.realtimeSinceStartup + SceneLoadTimeout;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(operation.isDone, Is.True,
                $"MUELLE did not finish loading within {SceneLoadTimeout:0.0}s.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneName),
                "MUELLE was loaded but did not become the active scene.");

            yield return null;
        }

        private void ResolvePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(PlayerTag);
            Assert.That(players.Length, Is.EqualTo(1),
                $"Expected exactly one active Player in MUELLE; found {players.Length}.");

            player = players[0];
            playerBody = player.GetComponent<Rigidbody2D>();
            Assert.That(playerBody, Is.Not.Null, "The active Player has no Rigidbody2D.");
            Assert.That(playerBody.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic),
                "The active Player Rigidbody2D is not Dynamic.");

            playerController = FindBehaviourByTypeName(player, "PlayerController");
            Assert.That(playerController, Is.Not.Null,
                "The active Player has no PlayerController behaviour.");
            Assert.That(playerController.enabled, Is.True,
                "PlayerController is disabled at scene start.");

            inputReader = FindBehaviourByTypeName(player, "InputReader");
            Assert.That(inputReader, Is.Not.Null,
                "The active Player has no InputReader behaviour.");
            Assert.That(inputReader.enabled, Is.True,
                "InputReader is disabled at scene start.");

            Type inputReaderType = inputReader.GetType();
            moveInputProperty = inputReaderType.GetProperty("MoveInput", BindingFlags.Instance | BindingFlags.Public);
            jumpHeldProperty = inputReaderType.GetProperty("JumpHeld", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(moveInputProperty, Is.Not.Null,
                "InputReader.MoveInput is not an accessible public property.");
            Assert.That(moveInputProperty.PropertyType, Is.EqualTo(typeof(Vector2)),
                "InputReader.MoveInput is not a Vector2 property.");
            Assert.That(jumpHeldProperty, Is.Not.Null,
                "InputReader.JumpHeld is not an accessible public property.");
            Assert.That(jumpHeldProperty.PropertyType, Is.EqualTo(typeof(bool)),
                "InputReader.JumpHeld is not a Boolean property.");
        }

        private IEnumerator WaitForPlayerToStabilize(string phase)
        {
            const float requiredStableTime = 0.25f;
            const float maxVerticalSpeed = 0.1f;
            const float maxPositionStep = 0.01f;

            float deadline = Time.realtimeSinceStartup + SettleTimeout;
            float stableTime = 0f;
            float lastY = playerBody.position.y;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();

                float currentY = playerBody.position.y;
                bool stable = Mathf.Abs(playerBody.velocity.y) <= maxVerticalSpeed &&
                              Mathf.Abs(currentY - lastY) <= maxPositionStep;
                stableTime = stable ? stableTime + Time.fixedDeltaTime : 0f;
                if (stableTime >= requiredStableTime)
                    yield break;

                lastY = currentY;
            }

            Assert.Fail(
                $"Player did not become stable during {phase} within {SettleTimeout:0.0}s. " +
                $"position={playerBody.position}, velocity={playerBody.velocity}.");
        }

        private static IEnumerator WaitForCameraToStabilize(Camera camera)
        {
            const float timeout = 4f;
            const float requiredStableTime = 0.25f;
            const float maxPositionStep = 0.01f;

            float deadline = Time.realtimeSinceStartup + timeout;
            float stableTime = 0f;
            Vector3 lastPosition = camera.transform.position;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                Vector3 currentPosition = camera.transform.position;
                bool stable = Vector2.Distance(currentPosition, lastPosition) <= maxPositionStep;
                stableTime = stable ? stableTime + Time.unscaledDeltaTime : 0f;
                if (stableTime >= requiredStableTime)
                    yield break;

                lastPosition = currentPosition;
            }

            Assert.Fail(
                $"Main Camera did not stabilize before the follow check within {timeout:0.0}s. " +
                $"position={camera.transform.position}.");
        }

        private IEnumerator PressKeyAndWaitForInputPipeline(Key key, Vector2 startPosition, string phase)
        {
            QueueKeyboardState(key, key);

            bool keyPressed = false;
            bool inputReceived = false;
            bool bodyResponded = false;
            float deadline = Time.realtimeSinceStartup + InputPipelineTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                keyPressed = IsKeyPressed(key);
                Vector2 moveInput = ReadMoveInput();
                bool jumpHeld = ReadJumpHeld();
                if (key == Key.D)
                {
                    inputReceived |= moveInput.x > 0.5f;
                    bodyResponded |= playerBody.position.x - startPosition.x > 0.01f ||
                                     playerBody.velocity.x > 0.05f;
                }
                else if (key == Key.Space)
                {
                    inputReceived |= jumpHeld;
                    bodyResponded |= playerBody.position.y - startPosition.y > 0.01f ||
                                     playerBody.velocity.y > 0.05f;
                }

                if (keyboard != null && keyboard.added && keyboard.enabled &&
                    keyPressed && inputReceived && bodyResponded)
                {
                    yield break;
                }
            }

            Assert.Fail(
                $"Synthetic input pipeline did not become observable during {phase} " +
                $"within {InputPipelineTimeout:0.0}s. keyPressed={keyPressed}, " +
                $"inputReaderReceived={inputReceived}, rigidbodyResponded={bodyResponded}. " +
                GetInputDiagnostics(key));
        }

        private IEnumerator ReleaseKeyAndWaitForInputReader(Key key, string phase)
        {
            QueueKeyboardState(key);

            float deadline = Time.realtimeSinceStartup + InputPipelineTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                bool inputReleased = key == Key.D
                    ? Mathf.Abs(ReadMoveInput().x) <= 0.01f
                    : !ReadJumpHeld();
                if (!IsKeyPressed(key) && inputReleased)
                    yield break;
            }

            Assert.Fail(
                $"Synthetic input was not released during {phase} within {InputPipelineTimeout:0.0}s. " +
                GetInputDiagnostics(key));
        }

        private void QueueKeyboardState(Key diagnosticKey, params Key[] pressedKeys)
        {
            Assert.That(keyboard, Is.Not.Null, "The test keyboard is unavailable.");
            Assert.That(keyboard.added, Is.True, "The test keyboard was removed unexpectedly.");
            Assert.That(keyboard.enabled, Is.True,
                $"The test keyboard is disabled before queuing input. {GetInputDiagnostics(diagnosticKey)}");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressedKeys));
        }

        private bool IsKeyPressed(Key key)
        {
            if (keyboard == null || !keyboard.added)
                return false;

            if (key == Key.D)
                return keyboard.dKey.isPressed;
            if (key == Key.Space)
                return keyboard.spaceKey.isPressed;

            Assert.Fail($"Unsupported synthetic key {key}.");
            return false;
        }

        private Vector2 ReadMoveInput()
        {
            Assert.That(inputReader, Is.Not.Null, "InputReader is unavailable.");
            Assert.That(moveInputProperty, Is.Not.Null, "InputReader.MoveInput property metadata is unavailable.");
            return (Vector2)moveInputProperty.GetValue(inputReader);
        }

        private bool ReadJumpHeld()
        {
            Assert.That(inputReader, Is.Not.Null, "InputReader is unavailable.");
            Assert.That(jumpHeldProperty, Is.Not.Null, "InputReader.JumpHeld property metadata is unavailable.");
            return (bool)jumpHeldProperty.GetValue(inputReader);
        }

        private string GetInputDiagnostics(Key key)
        {
            bool keyboardAdded = keyboard != null && keyboard.added;
            bool keyboardEnabled = keyboardAdded && keyboard.enabled;
            bool keyPressed = keyboardAdded && IsKeyPressed(key);
            Vector2 moveInput = inputReader != null && moveInputProperty != null
                ? ReadMoveInput()
                : Vector2.zero;
            bool jumpHeld = inputReader != null && jumpHeldProperty != null && ReadJumpHeld();
            InputSettings settings = InputSystem.settings;
            string bodyState = playerBody == null
                ? "Rigidbody2D=<unavailable>"
                : $"Rigidbody2D.position=({playerBody.position.x:0.000},{playerBody.position.y:0.000}), " +
                  $"Rigidbody2D.velocity=({playerBody.velocity.x:0.000},{playerBody.velocity.y:0.000})";

            return
                $"Application.isFocused={Application.isFocused}, " +
                $"Application.runInBackground={Application.runInBackground}, " +
                $"keyboard.added={keyboardAdded}, keyboard.enabled={keyboardEnabled}, " +
                $"key={key}, keyPressed={keyPressed}, " +
                $"InputReader.MoveInput=({moveInput.x:0.000},{moveInput.y:0.000}), " +
                $"InputReader.JumpHeld={jumpHeld}, " +
                $"backgroundBehavior={settings.backgroundBehavior}, " +
                $"editorInputBehaviorInPlayMode={settings.editorInputBehaviorInPlayMode}, " +
                bodyState + ".";
        }

        private void OverrideInputFocusPolicy()
        {
            InputSettings settings = InputSystem.settings;
            Assert.That(settings, Is.Not.Null, "InputSystem.settings is unavailable.");

            originalBackgroundBehavior = settings.backgroundBehavior;
            originalEditorInputBehavior = settings.editorInputBehaviorInPlayMode;
            inputSettingsOverridden = true;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        private void RestoreInputFocusPolicy()
        {
            if (!inputSettingsOverridden)
                return;

            InputSettings settings = InputSystem.settings;
            try
            {
                settings.backgroundBehavior = originalBackgroundBehavior;
            }
            finally
            {
                try
                {
                    settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
                }
                finally
                {
                    inputSettingsOverridden = false;
                }
            }
        }

        private void CleanupAfterFailedSetup()
        {
            try
            {
                if (keyboard != null && keyboard.added)
                    InputSystem.RemoveDevice(keyboard);
            }
            finally
            {
                try
                {
                    RestoreInputFocusPolicy();
                }
                finally
                {
                    ClearRuntimeReferences();
                    Time.timeScale = 1f;
                }
            }
        }

        private void ClearRuntimeReferences()
        {
            keyboard = null;
            player = null;
            playerBody = null;
            playerController = null;
            inputReader = null;
            moveInputProperty = null;
            jumpHeldProperty = null;
        }

        private static Behaviour FindBehaviourByTypeName(GameObject target, string typeName)
        {
            Component[] components = target.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component != null && component.GetType().Name == typeName)
                    return component as Behaviour;
            }

            return null;
        }
    }
}
