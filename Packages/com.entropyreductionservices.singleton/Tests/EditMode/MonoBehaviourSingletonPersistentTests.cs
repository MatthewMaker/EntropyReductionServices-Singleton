using NUnit.Framework;
using UnityEngine;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The persistent flavour adds an Awake that claims the slot and destroys later arrivals.
    /// Surviving a scene load is the other half of its contract and is not reachable from edit
    /// mode — DontDestroy() returns early when Application.isPlaying is false, because
    /// DontDestroyOnLoad is a play-mode-only API. That half lives in
    /// Tests/PlayMode/ScenePersistenceTests.cs.
    /// </summary>
    public class MonoBehaviourSingletonPersistentTests
    {
        [TearDown]
        public void TearDown()
        {
            Fixtures.PurgeAll();
        }

        [Test]
        public void Awake_ClaimsTheSlot()
        {
            var authored = Fixtures.Author<PersistentClaims>("Authored PersistentClaims");
            Assert.IsTrue(PersistentClaims.Exists, "Awake should have claimed the slot already");
            Assert.AreSame(authored, PersistentClaims.Instance);
        }

        [Test]
        public void SecondInstance_DestroysItsOwnGameObject()
        {
            var first = Fixtures.Author<PersistentDedupes>("First");
            var firstObject = first.gameObject;
            var second = Fixtures.Author<PersistentDedupes>("Second");

            Assert.AreSame(first, PersistentDedupes.Instance, "the first arrival keeps the slot");
            Assert.IsTrue(second == null, "the duplicate component should be gone");
            Assert.IsTrue(firstObject != null, "the winner should be untouched");
        }

        [Test]
        public void DuplicateOptingOutOfTheBlastRadius_LosesOnlyItsComponent()
        {
            Fixtures.Author<PersistentComponentOnly>("First");

            var host = new GameObject("Shared Host");
            var bystander = host.AddComponent<BoxCollider>();
            host.AddComponent<PersistentComponentOnly>();

            Assert.IsTrue(host != null, "DestroyWholeGameObject=false must spare the host object");
            Assert.IsTrue(bystander != null, "and everything else living on it");

            Object.DestroyImmediate(host);
        }

        [Test]
        public void OnDestroy_ReleasesTheSlot()
        {
            var authored = Fixtures.Author<PersistentReleases>("Authored PersistentReleases");
            Assert.IsTrue(PersistentReleases.Exists);

            Object.DestroyImmediate(authored.gameObject);
            Assert.IsFalse(PersistentReleases.Exists, "OnDestroy should have cleared the cache");
        }

        [Test]
        public void EditMode_DoesNotMoveTheInstanceToTheDontDestroyOnLoadScene()
        {
            // Pins the deliberate early return in DontDestroy(): calling DontDestroyOnLoad outside
            // play mode logs an error, and an edit-mode transient is already effectively
            // persistent via HideFlags.DontSave.
            var authored = Fixtures.Author<PersistentEditMode>("Authored PersistentEditMode");
            Assert.AreNotEqual("DontDestroyOnLoad", authored.gameObject.scene.name);
        }
    }
}
