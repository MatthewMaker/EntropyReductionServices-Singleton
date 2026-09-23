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

        /// <summary>
        /// A duplicate must not take an unrelated singleton down with it. Play mode specifically,
        /// because Destroy is deferred to end of frame: the bystander's Awake runs and claims its
        /// slot before the host would have gone, so the loss only shows up a frame later. In edit
        /// mode DestroyImmediate would have removed the host before that Awake ever ran.
        /// </summary>
        [UnityTest]
        public IEnumerator DuplicateSharingAHost_DoesNotDestroyTheBystander()
        {
            var winner = new GameObject("winner");
            winner.AddComponent<SharedHostDuplicate>();          // claims the slot

            // Built inactive so both components exist before either Awake runs, which is what a
            // scene-authored host looks like. Adding them to a live object instead would run the
            // duplicate's Awake while it was still alone, and the blast radius is decided there.
            var loser = new GameObject("loser");
            loser.SetActive(false);
            loser.AddComponent<SharedHostDuplicate>();                   // the duplicate
            var bystander = loser.AddComponent<SharedHostBystander>();   // the only one of its type
            loser.SetActive(true);

            yield return null;   // end of frame: any deferred Destroy lands

            Assert.IsTrue(loser != null, "the host must survive, because the bystander is on it");
            Assert.IsTrue(bystander != null, "the bystander must survive");
            Assert.AreSame(bystander, SharedHostBystander.Instance);
            Assert.IsTrue(SharedHostDuplicate.Instance != null, "the winner still holds the slot");

            Object.DestroyImmediate(winner);
        }

        /// <summary>The lone-singleton case still takes its shell with it.</summary>
        [UnityTest]
        public IEnumerator DuplicateAloneOnItsHost_StillDestroysTheHost()
        {
            var winner = new GameObject("winner");
            winner.AddComponent<SharedHostAlone>();

            var loser = new GameObject("loser");
            loser.AddComponent<SharedHostAlone>();

            yield return null;

            Assert.IsTrue(loser == null, "nothing else was on the host, so the shell goes too");
            Assert.IsTrue(SharedHostAlone.Instance != null);

            Object.DestroyImmediate(winner);
        }
    }
}
