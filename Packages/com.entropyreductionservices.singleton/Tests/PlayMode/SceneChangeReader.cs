using UnityEngine;
using UnityEngine.SceneManagement;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Authored in the harness's SceneChangeReader scene. Its Awake reads a Scene-lifetime
    /// singleton that the new scene does not author, the moment a lazy singleton should be
    /// created afresh, and one the scene change never touched; and records what it got and
    /// whether the teardown window was open.
    ///
    /// Alone in this file for the same reason as SceneChangeHandoff.
    /// </summary>
    internal class SceneChangeReader : MonoBehaviour
    {
        public static bool Ran;
        public static bool GotLive;
        public static bool GotLiveUnrelated;
        public static bool WasTearingDown;
        public static int ReadFrame = -1;
        public static string ActiveScene;

        /// <summary>Clears the recorded read, since the statics outlive each test.</summary>
        public static void Reset()
        {
            Ran = false;
            GotLive = false;
            GotLiveUnrelated = false;
            WasTearingDown = false;
            ReadFrame = -1;
            ActiveScene = null;
        }

        private void Awake()
        {
            Ran = true;
            ReadFrame = Time.frameCount;
            WasTearingDown = SingletonRuntime.IsTearingDown;
            var active = SceneManager.GetActiveScene();
            ActiveScene = $"{active.name} (isLoaded {active.isLoaded})";
            GotLive = SceneChangeLazy.Instance != null;   // Unity ==: false for the tombstone
            GotLiveUnrelated = SceneChangeUnrelated.Instance != null;
        }
    }
}
