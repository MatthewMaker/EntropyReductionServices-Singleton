using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Behaviour that differs between play mode and edit mode for reasons other than scene loads:
    /// destruction is deferred to end of frame rather than immediate, created objects are real
    /// scene members rather than DontSave transients, and IsAvailable short-circuits on
    /// Application.isPlaying.
    /// </summary>
    public class PlayModeLifecycleTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("SingletonLifecycleScene");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Probes.PurgeAll();

            if (_scene.IsValid() && _scene.isLoaded) yield return Probes.UnloadAndWait(_scene);
        }

        [UnityTest]
        public IEnumerator Duplicate_IsStillAliveInTheSameFrameAndGoneByTheNext()
        {
            // Object.Destroy is deferred to end of frame in play mode, where edit mode uses
            // DestroyImmediate. Code that checks for the duplicate synchronously will still see
            // it; that is a property of Unity, not a bug in the package, and it is worth pinning.
            var first = Probes.AuthorIn<PersistentDeferredDedupe>(_scene, "First");
            var second = Probes.AuthorIn<PersistentDeferredDedupe>(_scene, "Second");

            Assert.AreSame(first, PersistentDeferredDedupe.Instance, "the first arrival keeps the slot");
            Assert.IsFalse(second == null, "destruction is deferred, so the duplicate is still alive now");

            yield return null;

            Assert.IsTrue(second == null, "and gone by the next frame");
            Assert.AreSame(first, PersistentDeferredDedupe.Instance, "without disturbing the winner");
        }

        [UnityTest]
        public IEnumerator PassivePersistentDuplicate_NeverEntersTheDontDestroyOnLoadScene()
        {
            // PassivePersistent calls DontDestroy() only when it is the current instance. An
            // unconditional call would briefly move a doomed duplicate into DontDestroyOnLoad,
            // where an end-of-frame Destroy still finds it but anything enumerating that scene
            // in between would not.
            var first = Probes.AuthorIn<PassivePersistentDuplicate>(_scene, "First");
            var second = Probes.AuthorIn<PassivePersistentDuplicate>(_scene, "Second");

            Assert.AreSame(first, PassivePersistentDuplicate.Instance);
            Assert.IsTrue(Probes.IsPersistent(first.gameObject), "the winner persists");
            Assert.IsFalse(Probes.IsPersistent(second.gameObject), "the duplicate never does");

            yield return null;

            Assert.IsTrue(second == null);
        }

        [Test]
        public void AutoCreatedInstance_InPlayMode_IsNotMarkedDontSave()
        {
            // DontSave is an edit-mode-only precaution against serializing a transient into the
            // open scene. A play-mode instance is an ordinary scene object.
            var instance = AutoPlayMode.Instance;
            Assert.IsNotNull(instance);
            Assert.AreEqual(HideFlags.None, instance.gameObject.hideFlags);
        }

        [Test]
        public void IsAvailable_DuringPlay_IsTrueEvenBeforeAnythingExists()
        {
            // The contract is that Instance is non-null for the whole time the application runs,
            // so IsAvailable answers true without resolving or creating anything.
            Assert.IsFalse(AutoAvailable.Exists, "precondition: nothing resolved yet");
            Assert.IsTrue(AutoAvailable.IsAvailable);
            Assert.IsFalse(SingletonRuntime.IsQuitting);
        }

        [Test]
        public void FramePromoted_RecordsTheFrameTheSlotWasClaimed()
        {
            var probe = Probes.AuthorIn<PersistentFrame>(_scene, "Probe");
            Assert.AreEqual(Time.frameCount, probe.FramePromoted);
        }
    }
}
