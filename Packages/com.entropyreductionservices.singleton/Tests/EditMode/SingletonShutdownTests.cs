using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The shutdown allowance: once the singleton's GameObject is gone, Instance hands back the
    /// destroyed component instead of null, so teardown code that dereferences it does not throw.
    ///
    /// These tests exercise LastKnown directly rather than Instance. Reaching Instance's shutdown
    /// branch needs SingletonRuntime.IsQuitting, which is driven by Application.quitting and has
    /// no setter — it cannot be raised from a test, in either EditMode or PlayMode. LastKnown is
    /// the whole of the new behaviour; the branch that returns it is one line.
    /// </summary>
    public class SingletonShutdownTests
    {
        [TearDown]
        public void TearDown()
        {
            Fixtures.PurgeAll();
        }

        [Test]
        public void LastKnown_BeforeAnyInstance_IsNull()
        {
            Assert.IsNull(TombstoneVirgin.Exposed);
        }

        /// <summary>
        /// The property the whole change rests on: after destruction the managed wrapper is still
        /// a live C# object, so a bare dereference has something to land on.
        /// </summary>
        [Test]
        public void LastKnown_AfterDestruction_IsStillTheSameReference()
        {
            var created = TombstoneReachable.Instance;
            Object.DestroyImmediate(created.gameObject);

            Assert.IsFalse(TombstoneReachable.Exists, "the slot should have been released");
            Assert.IsTrue(ReferenceEquals(created, TombstoneReachable.Exposed));
        }

        /// <summary>
        /// Calling a plain C# member on the destroyed component is the recurring teardown case —
        /// an OnDisable unregistering itself from a manager. It must not throw.
        /// </summary>
        [Test]
        public void LastKnown_AfterDestruction_AcceptsPlainMemberCalls()
        {
            TombstoneReachable.Bumps = 0;
            Object.DestroyImmediate(TombstoneReachable.Instance.gameObject);

            Assert.DoesNotThrow(() => TombstoneReachable.Exposed.Bump());
            Assert.AreEqual(1, TombstoneReachable.Bumps);
        }

        /// <summary>
        /// Unity's == overload still reports the tombstone as null, which is what keeps every
        /// existing '!= null' guard and '?.' call site meaning exactly what it meant before.
        /// </summary>
        [Test]
        public void LastKnown_AfterDestruction_StillReportsAsNullToUnityEquality()
        {
            Object.DestroyImmediate(TombstoneUnityEquality.Instance.gameObject);

            var tombstone = TombstoneUnityEquality.Exposed;
            Assert.IsFalse(ReferenceEquals(tombstone, null), "the managed reference should survive");
            Assert.IsTrue(tombstone == null, "Unity equality should still call it null");
        }

        /// <summary>The passive flavour shares the tombstone, since it shares the base's cache.</summary>
        [Test]
        public void LastKnown_OnPassiveFlavour_SurvivesDestruction()
        {
            var created = Fixtures.Author<TombstonePassive>(nameof(LastKnown_OnPassiveFlavour_SurvivesDestruction));
            Object.DestroyImmediate(created.gameObject);

            Assert.IsFalse(TombstonePassive.Exists);
            Assert.IsTrue(ReferenceEquals(created, TombstonePassive.Exposed));
        }
    }
}
