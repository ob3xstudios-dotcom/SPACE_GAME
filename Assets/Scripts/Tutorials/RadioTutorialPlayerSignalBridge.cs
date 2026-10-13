using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tutorials
{
    [DisallowMultipleComponent]
    public sealed class RadioTutorialPlayerSignalBridge : MonoBehaviour
    {
        private PlayerController player;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            BindToCurrentPlayer();
        }

        private void Start()
        {
            BindToCurrentPlayer();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Bind(null);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindToCurrentPlayer();
        }

        private void BindToCurrentPlayer()
        {
            Bind(FindFirstObjectByType<PlayerController>());
        }

        private void Bind(PlayerController nextPlayer)
        {
            if (player == nextPlayer) return;

            if (player != null)
                player.NormalJumpPerformed -= HandleNormalJumpPerformed;

            player = nextPlayer;

            if (player != null)
                player.NormalJumpPerformed += HandleNormalJumpPerformed;
        }

        private static void HandleNormalJumpPerformed()
        {
            RadioTutorialManager.Report(RadioTutorialAction.Jump);
        }
    }
}
