using NUnit.Framework;
using UnityEngine;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The passive flavours never create anything. Instance is null until some component's Awake
    /// claims the slot, which is the whole point: scene-authored objects carrying inspector state
    /// cannot be conjured out of a bare GameObject.
    /// </summary>
    public class MonoBehaviourSingletonPassiveTests
    {
        [TearDown]
        public void TearDown()
        {
            Fixtures.PurgeAll();
        }

        [Test]
        public void Instance_BeforeAnyAwake_IsNull()
        {
            Assert.IsNull(PassiveNullUntilAwake.Instance);
        }

        [Test]
        public void Instance_DoesNotAutoCreate()
        {
            _ = PassiveNeverCreates.Instance;
            _ = PassiveNeverCreates.Instance;

            Assert.IsNull(PassiveNeverCreates.Instance);
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<PassiveNeverCreates>().Length,
                "reading Instance must not have brought anything into existence");
        }

        [Test]
        public void Awake_ClaimsTheSlot()
        {
            var authored = Fixtures.Author<PassiveClaims>("Authored PassiveClaims");
            Assert.AreSame(authored, PassiveClaims.Instance);
        }

        [Test]
        public void SecondInstance_DestroysItself()
        {
            var first = Fixtures.Author<PassiveDedupes>("First");
            var second = Fixtures.Author<PassiveDedupes>("Second");

            Assert.AreSame(first, PassiveDedupes.Instance);
            Assert.IsTrue(second == null);
        }

        [Test]
        public void OnDestroy_ReleasesTheSlot()
        {
            var authored = Fixtures.Author<PassiveReleases>("Authored PassiveReleases");
            Assert.IsNotNull(PassiveReleases.Instance);

            Object.DestroyImmediate(authored.gameObject);
            Assert.IsNull(PassiveReleases.Instance);
        }

        [Test]
        public void PassivePersistent_Awake_ClaimsTheSlot()
        {
            var authored = Fixtures.Author<PassivePersistentClaims>("Authored");
            Assert.AreSame(authored, PassivePersistentClaims.Instance);
        }

        [Test]
        public void PassivePersistent_SecondInstance_DestroysItself()
        {
            var first = Fixtures.Author<PassivePersistentDedupes>("First");
            var second = Fixtures.Author<PassivePersistentDedupes>("Second");

            Assert.AreSame(first, PassivePersistentDedupes.Instance);
            Assert.IsTrue(second == null);
        }

        [Test]
        public void PassivePersistent_Instance_DoesNotAutoCreate()
        {
            Assert.IsNull(PassivePersistentNeverCreates.Instance);
            Assert.AreEqual(0, Resources.FindObjectsOfTypeAll<PassivePersistentNeverCreates>().Length);
        }
    }
}
