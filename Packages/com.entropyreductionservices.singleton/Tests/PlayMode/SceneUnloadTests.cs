using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Scene unload is the second teardown window, and the one Unity gives no usable event for —
    /// sceneUnloaded fires after the objects are already gone. The singleton recognises it from
    /// inside its own OnDestroy (a scene under unload reports gameObject.scene.isLoaded == false)
    /// and reports it to SingletonRuntime, which holds the window open for the rest of that frame.
    ///
    /// Without it, a teardown callback reading Instance after the singleton had been destroyed
    /// walked the full resolution chain and built a replacement GameObject inside the scene being
    /// unloaded.
    ///
    /// These assert from inside OnDestroy rather than after the unload completes. The window is a
    /// frame stamp and UnloadSceneAsync resumes its caller on a later frame, so a test that yields
    /// on the unload is already past the window it means to observe — which is also the only
    /// vantage point where the hazardous read actually happens.
    /// </summary>
    public class SceneUnloadTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("SingletonUnloadScene");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return Probes.UnloadAndWait(_scene);
            Probes.PurgeAll();
        }

        /// <summary>
        /// The whole point: Instance read during the unload hands back the destroyed component
        /// and builds nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator InstanceReadDuringUnload_ReturnsTheTombstoneAndCreatesNothing()
        {
            var original = Probes.AuthorIn<UnloadTombstoneWitness>(_scene, "bus");
            Assert.AreSame(original, UnloadTombstoneWitness.Instance,
                "precondition: the probe must own the slot, or OnDestroy releases nothing");

            yield return Probes.UnloadAndWait(_scene);

            Assert.AreEqual(1, UnloadTombstoneWitness.Destroys, "the witness must have been destroyed once");
            Assert.IsTrue(UnloadTombstoneWitness.SawWindow, "the unload window must be open inside OnDestroy");
            Assert.IsTrue(UnloadTombstoneWitness.GotTombstone, "Instance must hand back the destroyed component");
            Assert.IsFalse(UnloadTombstoneWitness.GotFreshObject, "Instance must not have built a replacement");
        }

        /// <summary>
        /// IsAvailable's "true while playing" shortcut is only honest because Instance creates on
        /// demand. During the unload window it does not, so IsAvailable must not claim otherwise.
        /// </summary>
        [UnityTest]
        public IEnumerator IsAvailable_DuringUnload_IsFalse()
        {
            var original = Probes.AuthorIn<UnloadAvailabilityWitness>(_scene, "bus");
            Assert.AreSame(original, UnloadAvailabilityWitness.Instance, "precondition: owns the slot");
            Assert.IsTrue(UnloadAvailabilityWitness.IsAvailable, "precondition: available while loaded");

            yield return Probes.UnloadAndWait(_scene);

            Assert.IsTrue(UnloadAvailabilityWitness.SawWindow, "precondition: the window was open");
            Assert.IsFalse(UnloadAvailabilityWitness.WasAvailable);
        }

        /// <summary>
        /// Destroying the singleton on its own is not a teardown: isLoaded stays true, the window
        /// never opens, and resurrection still works.
        /// </summary>
        [UnityTest]
        public IEnumerator IndividualDestroy_DoesNotOpenTheWindow()
        {
            var original = Probes.AuthorIn<UnloadIndividualWitness>(_scene, "bus");
            Assert.AreSame(original, UnloadIndividualWitness.Instance, "precondition: owns the slot");

            Object.DestroyImmediate(original.gameObject);

            Assert.AreEqual(1, UnloadIndividualWitness.Destroys, "precondition: OnDestroy ran and owned the slot");
            Assert.IsFalse(UnloadIndividualWitness.SawWindow, "an ordinary destroy is not a scene unload");
            Assert.IsNotNull(UnloadIndividualWitness.Instance, "and resurrection still works");
            yield break;
        }

        /// <summary>
        /// The stamp has to expire. If it did not, a scene load would permanently disable
        /// resurrection — silently, and only visibly on the second load.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWindow_ClosesAfterTheUnloadFrame()
        {
            Probes.AuthorIn<UnloadWindowCloses>(_scene, "bus");

            yield return Probes.UnloadAndWait(_scene);

            Assert.IsFalse(SingletonRuntime.IsUnloadingScene, "the stamp must not outlive its frame");
            Assert.IsFalse(UnloadWindowCloses.Exists, "precondition: the slot was released");
            Assert.IsNotNull(UnloadWindowCloses.Instance, "creation must work again afterwards");
        }

        /// <summary>
        /// The find path now drops candidates whose scene is unloading. That filter must not also
        /// drop a persistent singleton: SceneManager never enumerates the DontDestroyOnLoad scene,
        /// so an acceptance test against the loaded-scene list would reject it and break
        /// re-adoption after a domain reload. Hence a rejection test on IsValid() && !isLoaded.
        /// </summary>
        [UnityTest]
        public IEnumerator UnloadFilter_StillAdoptsADontDestroyOnLoadInstance()
        {
            var persistent = Probes.AuthorIn<UnloadDdolSurvivesFilter>(_scene, "bus");
            yield return null;

            Assert.IsTrue(Probes.IsPersistent(persistent.gameObject), "precondition: moved to DDOL");
            Assert.IsTrue(persistent.gameObject.scene.isLoaded,
                "the DontDestroyOnLoad scene must report isLoaded, or the filter would reject it");

            yield return Probes.UnloadAndWait(_scene);

            Assert.IsTrue(UnloadDdolSurvivesFilter.ExistsOrFindInScene(),
                "the persistent instance must still be findable after an unrelated scene unloads");
        }
    }
}
