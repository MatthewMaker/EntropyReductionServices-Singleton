using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Probe types and helpers for the play-mode half of the contract.
    ///
    /// Unlike the EditMode fixtures these are NOT [ExecuteAlways]: in play mode Unity calls Awake
    /// on its own, which is the arrangement the package actually ships for. One type per test,
    /// for the same reason as in EditMode — the cache is a static on the closed generic type and
    /// the whole run shares one domain.
    /// </summary>
    internal static class Probes
    {
        /// <summary>
        /// Destroys every probe left behind by a test. DestroyImmediate rather than Destroy so
        /// OnDestroy — and therefore the slot release — happens before the next test starts,
        /// instead of at the end of the frame.
        ///
        /// Sweeps by assembly rather than by an explicit type list, so a probe added without a
        /// matching teardown line cannot silently leak static state into the next test.
        /// </summary>
        public static void PurgeAll()
        {
            foreach (var probe in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (probe == null) continue;
                if (probe.GetType().Assembly != typeof(Probes).Assembly) continue;
                if (!probe.gameObject.scene.IsValid()) continue;
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>
        /// Creates a probe inside a scene of our own, so a later unload of that scene is a real
        /// test of survival rather than a test of the test runner's scene.
        /// </summary>
        public static T AuthorIn<T>(Scene scene, string name) where T : MonoBehaviour
        {
            var host = new GameObject(name);
            SceneManager.MoveGameObjectToScene(host, scene);
            return host.AddComponent<T>();   // Awake runs here
        }

        /// <summary>True when the object sits in Unity's DontDestroyOnLoad scene.</summary>
        public static bool IsPersistent(GameObject go) =>
            go != null && go.scene.buildIndex == -1 && go.scene.name == "DontDestroyOnLoad";

        /// <summary>
        /// Unloads a scene and gives deferred destruction a frame to land — the trailing yield is
        /// load-bearing for the end-of-frame Destroy assertions, not padding.
        /// </summary>
        public static IEnumerator UnloadAndWait(Scene scene)
        {
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }
    }

    internal class PersistentSurvives : MonoBehaviourSingletonPersistent<PersistentSurvives> { }
    internal class PersistentGoesToDdol : MonoBehaviourSingletonPersistent<PersistentGoesToDdol> { }
    internal class PersistentDetaches : MonoBehaviourSingletonPersistent<PersistentDetaches> { }
    internal class PersistentDeferredDedupe : MonoBehaviourSingletonPersistent<PersistentDeferredDedupe> { }
    internal class PersistentFrame : MonoBehaviourSingletonPersistent<PersistentFrame> { }

    internal class PassivePersistentGoesToDdol : MonoBehaviourSingletonPassivePersistent<PassivePersistentGoesToDdol> { }
    internal class PassivePersistentDuplicate : MonoBehaviourSingletonPassivePersistent<PassivePersistentDuplicate> { }
    internal class PassivePersistentSurvives : MonoBehaviourSingletonPassivePersistent<PassivePersistentSurvives> { }

    internal class AutoPlayMode : MonoBehaviourSingleton<AutoPlayMode> { }
    internal class AutoDiesWithScene : MonoBehaviourSingleton<AutoDiesWithScene> { }
    internal class AutoAvailable : MonoBehaviourSingleton<AutoAvailable> { }
}
