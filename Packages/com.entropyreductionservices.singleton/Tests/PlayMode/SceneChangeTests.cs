using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// What a Scene-lifetime singleton does across a single-mode scene load, which unloading a
    /// scene the test created cannot reproduce: the old scene's objects are destroyed while it is
    /// still the active scene, and the new scene's are woken in the same frame once it has become
    /// the active one. So this is the unload that opens the teardown window, and the one where it
    /// must close again before the new scene needs its singletons.
    ///
    /// The scenes are real assets built by the harness project, like the Resources prefab, and
    /// loaded by path through the editor, so they need not be in the build settings. Absent, or
    /// outside the editor, the tests say so instead of failing.
    ///
    /// Each test runs for both the synchronous and the asynchronous load, sharing its probe types
    /// between the two — scene-authored types cannot be generic, and a type per case would need
    /// a scene per case. The teardown purge releases the slot, and each test asserts that as a
    /// precondition.
    /// </summary>
    public class SceneChangeTests
    {
        private const string Folder = "Assets/Scenes/SingletonTests/";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Probes.PurgeAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneLifetime_SingleModeLoad_HandsTheSlotToTheNextScenesInstance(
            [Values(false, true)] bool async)
        {
            Assert.IsFalse(SceneChangeHandoff.Exists, "precondition: the slot starts empty");

            var old = new GameObject("Old").AddComponent<SceneChangeHandoff>();
            Assert.AreSame(old, SceneChangeHandoff.Instance, "precondition: the old instance holds the slot");

            yield return LoadSingle(Folder + nameof(SceneChangeHandoff) + ".unity", async);

            Assert.IsTrue(old == null, "the old instance should have been destroyed with its scene");
            Assert.IsTrue(SceneChangeHandoff.Exists,
                "the next scene's authored instance should hold the slot, not have destroyed itself " +
                "as a duplicate of the old one");
            Assert.AreEqual(nameof(SceneChangeHandoff), SceneChangeHandoff.Instance.gameObject.scene.name);
        }

        /// <summary>
        /// The old scene's teardown is the hazardous read: its OnDestroy runs while the dying scene
        /// is still active, so a replacement would be built inside it. Instance must hand back the
        /// destroyed component instead, IsAvailable must say nothing is live — and once the load
        /// has landed, creation must work again.
        /// </summary>
        [UnityTest]
        public IEnumerator SingleModeLoad_ReadFromTheOldScenesOnDestroy_ReturnsTheTombstone(
            [Values(false, true)] bool async)
        {
            SceneChangeTombstoneWitness.Reset();
            Assert.IsFalse(SceneChangeTombstoneWitness.Exists, "precondition: the slot starts empty");

            var original = new GameObject("bus").AddComponent<SceneChangeTombstoneWitness>();
            Assert.AreSame(original, SceneChangeTombstoneWitness.Instance, "precondition: owns the slot");

            yield return LoadSingle(Folder + nameof(SceneChangeHandoff) + ".unity", async);
            SceneChangeTombstoneWitness.Armed = false;

            Assert.AreEqual(1, SceneChangeTombstoneWitness.Destroys, "the witness must have been destroyed once");
            Assert.IsTrue(SceneChangeTombstoneWitness.SawWindow, "the window must be open inside OnDestroy");
            Assert.IsFalse(SceneChangeTombstoneWitness.WasAvailable, "IsAvailable must not claim a live instance");
            Assert.IsTrue(SceneChangeTombstoneWitness.GotTombstone, "Instance must hand back the destroyed component");
            Assert.IsFalse(SceneChangeTombstoneWitness.GotFreshObject, "Instance must not have built a replacement");

            Assert.IsFalse(SingletonRuntime.IsUnloadingScene, "the stamp must not outlive its frame");
            Assert.IsNotNull(SceneChangeTombstoneWitness.Instance, "creation must work again afterwards");
        }

        /// <summary>
        /// The new scene's Awake runs in the frame the old scene was torn down, but with the new
        /// scene active. Creating then puts nothing into a dying scene, so a lazy singleton —
        /// the one just destroyed, or one the unload never touched — must come back live.
        /// </summary>
        [UnityTest]
        public IEnumerator SceneLifetime_SingleModeLoad_NextScenesAwakeGetsALiveInstance(
            [Values(false, true)] bool async)
        {
            Assert.IsFalse(SceneChangeLazy.Exists, "precondition: the slot starts empty");
            Assert.IsFalse(SceneChangeUnrelated.Exists, "precondition: never created before the load");

            var old = SceneChangeLazy.Instance;
            Assert.IsFalse(Probes.IsPersistent(old.gameObject), "precondition: a scene lifetime");
            SceneChangeLazy.DestroyFrame = -1;
            SceneChangeReader.Reset();

            yield return LoadSingle(Folder + nameof(SceneChangeReader) + ".unity", async);

            Assert.IsTrue(old == null, "the old instance should have been destroyed with its scene");
            Assert.IsTrue(SceneChangeReader.Ran, "the loaded scene should carry the reader");
            Assert.IsTrue(SceneChangeReader.GotLive,
                $"the reader's Awake (frame {SceneChangeReader.ReadFrame}, IsTearingDown " +
                $"{SceneChangeReader.WasTearingDown}, active scene {SceneChangeReader.ActiveScene}) " +
                $"got the destroyed instance; the old one was destroyed in frame {SceneChangeLazy.DestroyFrame}");
            Assert.IsTrue(SceneChangeReader.GotLiveUnrelated,
                "a singleton the unload did not touch must be creatable from the new scene's Awake");
        }

        /// <summary>
        /// Loads a scene asset by path in single mode and waits until the load has landed and
        /// deferred destruction has run. Ignores the test when the asset is absent or there is no
        /// editor to load by path.
        /// </summary>
        private static IEnumerator LoadSingle(string path, bool async)
        {
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path) == null)
                Assert.Ignore($"{path} is not present.");

            var parameters = new LoadSceneParameters(LoadSceneMode.Single);
            if (async)
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, parameters);
            else
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, parameters);

            yield return null;
            yield return null;
#else
            Assert.Ignore("Loading a scene by asset path needs the editor.");
            yield break;
#endif
        }
    }
}
