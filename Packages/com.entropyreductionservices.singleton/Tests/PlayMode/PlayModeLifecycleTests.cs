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
        public void PassiveIsAvailable_DuringPlay_IsFalseUntilSomethingClaimsTheSlot()
        {
            // The play-mode shortcut above is only honest because Instance creates on demand. A
            // passive Instance never does, so IsAvailable must track the slot instead.
            Assert.IsFalse(PassiveAvailable.IsAvailable, "nothing has claimed the slot");
            Assert.IsNull(PassiveAvailable.Instance);

            Probes.AuthorIn<PassiveAvailable>(_scene, "Probe");
            Assert.IsTrue(PassiveAvailable.IsAvailable, "and true once Awake has claimed it");
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
        /// slot before the GameObject would have gone, so the loss only shows up a frame later. In edit
        /// mode DestroyImmediate would have removed the GameObject before that Awake ever ran.
        /// </summary>
        [UnityTest]
        public IEnumerator DuplicateSharingAGameObject_DoesNotDestroyTheBystander()
        {
            var winner = new GameObject("winner");
            winner.AddComponent<SharedObjectDuplicate>();          // claims the slot

            // Built inactive so both components exist before either Awake runs, which is what a
            // scene-authored GameObject looks like. Adding them to a live object instead would run the
            // duplicate's Awake while it was still alone, and the blast radius is decided there.
            var loser = new GameObject("loser");
            loser.SetActive(false);
            loser.AddComponent<SharedObjectDuplicate>();                   // the duplicate
            var bystander = loser.AddComponent<SharedObjectBystander>();   // the only one of its type
            loser.SetActive(true);

            yield return null;   // end of frame: any deferred Destroy lands

            Assert.IsTrue(loser != null, "the GameObject must survive, because the bystander is on it");
            Assert.IsTrue(bystander != null, "the bystander must survive");
            Assert.AreSame(bystander, SharedObjectBystander.Instance);
            Assert.IsTrue(SharedObjectDuplicate.Instance != null, "the winner still holds the slot");

            Object.DestroyImmediate(winner);
        }

        /// <summary>The lone-singleton case still takes its shell with it.</summary>
        [UnityTest]
        public IEnumerator DuplicateAloneOnItsGameObject_StillDestroysIt()
        {
            var winner = new GameObject("winner");
            winner.AddComponent<SharedObjectAlone>();

            var loser = new GameObject("loser");
            loser.AddComponent<SharedObjectAlone>();

            yield return null;

            Assert.IsTrue(loser == null, "nothing else was on the GameObject, so the shell goes too");
            Assert.IsTrue(SharedObjectAlone.Instance != null);

            Object.DestroyImmediate(winner);
        }

        /// <summary>
        /// A stateless persistent singleton on a shared GameObject rebuilds itself on a GameObject named
        /// for the type, leaving the GameObject and its siblings in the scene.
        /// </summary>
        [UnityTest]
        public IEnumerator PersistentOnASharedGameObject_RebuildsOnItsOwnObject()
        {
            var owner = new GameObject("Managers");
            owner.SetActive(false);
            owner.AddComponent<ExtractStateless>();
            var sibling = owner.AddComponent<AudioSource>();
            owner.SetActive(true);

            yield return null;

            Assert.IsTrue(owner != null, "the shared GameObject must stay in the scene");
            Assert.IsTrue(sibling != null, "the sibling must stay on it");
            Assert.IsFalse(Probes.IsPersistent(owner), "the shared GameObject must not be persisted");

            var instance = ExtractStateless.Instance;
            Assert.IsTrue(instance != null);
            Assert.AreNotSame(owner, instance.gameObject, "the singleton must be on a new object");
            Assert.AreEqual(nameof(ExtractStateless), instance.gameObject.name);
            Assert.IsTrue(Probes.IsPersistent(instance.gameObject), "the new object must be persisted");

            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(instance.gameObject);
        }

        /// <summary>
        /// With serialized fields there is authored state to lose, so extraction declines and the
        /// old behaviour stands: the whole GameObject is persisted, siblings included.
        /// </summary>
        [UnityTest]
        public IEnumerator PersistentWithSerializedFields_DeclinesToRebuild()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"ExtractStateful is persistent but shares 'Stateful Managers'"));

            var owner = new GameObject("Stateful Managers");
            owner.SetActive(false);
            var singleton = owner.AddComponent<ExtractStateful>();
            owner.AddComponent<AudioSource>();
            owner.SetActive(true);

            yield return null;

            Assert.AreSame(singleton, ExtractStateful.Instance, "the authored component must survive");
            Assert.AreSame(owner, ExtractStateful.Instance.gameObject);
            Assert.IsTrue(Probes.IsPersistent(owner), "the shared GameObject is persisted, as before");

            Object.DestroyImmediate(owner);
        }
    }
}
