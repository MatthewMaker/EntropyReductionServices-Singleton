using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Unloading a scene that is not the active one. The singleton recognises a scene unload from
    /// inside its own OnDestroy (a scene under unload reports gameObject.scene.isLoaded == false)
    /// and reports it to SingletonRuntime — but the teardown window only opens while the unloading
    /// scene is the active one, the scene a replacement would be created in. Here it is not, so a
    /// read builds the replacement in the live active scene. SceneChangeTests covers the unload
    /// that does open the window: a single-mode load.
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
        /// The unloading scene is not where a replacement would go, so the window stays shut:
        /// Instance read during the unload builds the replacement in the active scene, and
        /// IsAvailable says so.
        /// </summary>
        [UnityTest]
        public IEnumerator InstanceReadDuringAnInactiveScenesUnload_BuildsTheReplacementInTheActiveScene()
        {
            var original = Probes.AuthorIn<UnloadInactiveWitness>(_scene, "bus");
            Assert.AreSame(original, UnloadInactiveWitness.Instance,
                "precondition: the probe must own the slot, or OnDestroy releases nothing");
            Assert.AreNotEqual(_scene, SceneManager.GetActiveScene(), "precondition: not the active scene");

            yield return Probes.UnloadAndWait(_scene);
            UnloadInactiveWitness.Armed = false;

            Assert.AreEqual(1, UnloadInactiveWitness.Destroys, "the witness must have been destroyed once");
            Assert.IsFalse(UnloadInactiveWitness.SawWindow, "the window must stay shut for an inactive scene");
            Assert.IsTrue(UnloadInactiveWitness.WasAvailable, "IsAvailable must agree that Instance can create");
            Assert.IsTrue(UnloadInactiveWitness.GotFreshObject, "Instance must have built a replacement");
            Assert.AreEqual(SceneManager.GetActiveScene().name, UnloadInactiveWitness.FreshScene,
                "in the active scene, not the one being unloaded");
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
