using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core
{
    public static class RenderingDebugRuntimeUI
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DisableRuntimeUI()
        {
            DebugManager.instance.enableRuntimeUI = false;
        }
    }
}
