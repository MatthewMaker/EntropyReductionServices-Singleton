using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Surviving a scene load is the half of the persistent contract that edit mode cannot reach:
    /// DontDestroy() returns early when Application.isPlaying is false, because DontDestroyOnLoad
    /// is a play-mode-only API.
    ///
    /// These tests unload a scene they created themselves rather than calling LoadScene, which
    /// would require the scene to be in the build settings and would take the test runner's own
    /// scene with it. Unloading is the same mechanism from the object's point of view: everything
    /// in the unloaded scene is destroyed, everything in DontDestroyOnLoad is not.
    /// </summary>
    public class ScenePersistenceTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("SingletonProbeScene");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Probes.PurgeAll();

            if (_scene.IsValid() && _scene.isLoaded) yield return Probes.UnloadAndWait(_scene);
        }

        [Test]
        public void Persistent_Awake_MovesTheInstanceToDontDestroyOnLoad()
        {
            var probe = Probes.AuthorIn<PersistentGoesToDdol>(_scene, "Probe");
            Assert.IsTrue(Probes.IsPersistent(probe.gameObject),
                "Awake should have called DontDestroyOnLoad on the winning instance");
        }

        [Test]
        public void Persistent_ResolvedBeforeItsOwnAwake_IsStillMadePersistent()
        {
            // A scene-authored persistent singleton can be found by another object's Awake before
            // its own Awake has run. The search caches it, so its Awake then sees itself already
            // in the slot. Activating an inactive root wakes the children in order, which puts the
            // reader first.
            var root = new GameObject("Root");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _scene);

            var reader = new GameObject("Reader");
            reader.transform.SetParent(root.transform);
            var readerProbe = reader.AddComponent<ReadsPersistentInAwake>();

            var host = new GameObject("Singleton");
            host.transform.SetParent(root.transform);
            var singleton = host.AddComponent<PersistentReadBeforeAwake>();

            root.SetActive(true);

            Assert.IsTrue(readerProbe.ReadBeforeSingletonAwake, "the reader must wake first for this to test anything");
            Assert.AreSame(singleton, readerProbe.Seen, "the reader should have found the authored instance");
            Assert.AreSame(singleton, PersistentReadBeforeAwake.Instance);
            Assert.IsTrue(Probes.IsPersistent(singleton.gameObject),
                "resolving it before its Awake must not cost it DontDestroyOnLoad");
        }

        [UnityTest]
        public IEnumerator Persistent_SurvivesTheUnloadOfTheSceneItWasBornIn()
        {
            var probe = Probes.AuthorIn<PersistentSurvives>(_scene, "Probe");
            Assert.IsNotNull(probe);

            yield return Probes.UnloadAndWait(_scene);

            Assert.IsTrue(probe != null, "the instance should have outlived its origin scene");
            Assert.IsTrue(PersistentSurvives.Exists, "and should still hold the slot");
            Assert.AreSame(probe, PersistentSurvives.Instance);
        }

        [UnityTest]
        public IEnumerator SceneLifetime_StaysInItsSceneAndDiesWithIt()
        {
            // The contrast that gives the test above its meaning: [SingletonLifetime(Scene)] still
            // claims the slot in Awake but never calls DontDestroyOnLoad, so it goes down with the
            // scene and releases the slot.
            var probe = Probes.AuthorIn<AutoDiesWithScene>(_scene, "Probe");
            Assert.IsTrue(AutoDiesWithScene.Exists, "Awake should have claimed the slot");
            Assert.IsFalse(Probes.IsPersistent(probe.gameObject), "a scene lifetime must not persist");

            yield return Probes.UnloadAndWait(_scene);

            Assert.IsTrue(probe == null, "the instance should have been destroyed with its scene");
            Assert.IsFalse(AutoDiesWithScene.Exists, "OnDestroy should have released the slot");
        }

        [Test]
        public void Persistent_NestedInstance_IsDetachedToTheSceneRoot()
        {
            // DontDestroyOnLoad only applies to root objects and warns-then-ignores otherwise,
            // so DontDestroy() reparents first. Without that the object silently fails to persist.
            var parent = new GameObject("Parent");
            SceneManager.MoveGameObjectToScene(parent, _scene);

            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);
            var probe = child.AddComponent<PersistentDetaches>();

            Assert.IsNull(probe.transform.parent, "the instance should have been promoted to root");
            Assert.IsTrue(Probes.IsPersistent(probe.gameObject));

            Object.DestroyImmediate(parent);
        }

    }
}
