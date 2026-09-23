using NUnit.Framework;
using UnityEngine;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The lazy, auto-creating flavour: resolve from the loaded scenes, then Resources, then a
    /// bare GameObject. This flavour declares no Awake, so nothing here exercises slot-claiming;
    /// that belongs to the Persistent and Passive tests.
    /// </summary>
    public class MonoBehaviourSingletonTests
    {
        [TearDown]
        public void TearDown()
        {
            Fixtures.PurgeAll();
        }

        [Test]
        public void Instance_WhenNothingExists_CreatesOne()
        {
            Assert.IsNotNull(AutoCreates.Instance);
        }

        [Test]
        public void Instance_ReadTwice_ReturnsTheSameObject()
        {
            var first = AutoStable.Instance;
            var second = AutoStable.Instance;
            Assert.AreSame(first, second);
        }

        [Test]
        public void Exists_BeforeFirstAccess_IsFalse()
        {
            // Exists is documented as a pure check: it must not trigger the resolution chain.
            Assert.IsFalse(AutoVirgin.Exists);
            Assert.IsNotNull(AutoVirgin.Instance);
            Assert.IsTrue(AutoVirgin.Exists);
        }

        [Test]
        public void TryGetInstance_BeforeFirstAccess_ReturnsFalse()
        {
            Assert.IsFalse(AutoVirginTryGet.TryGetInstance(out var instance));
            Assert.IsNull(instance);
        }

        [Test]
        public void Instance_AdoptsAnAuthoredComponentRatherThanCreatingOne()
        {
            var authored = Fixtures.Author<AutoAdopts>("Authored AutoAdopts");
            Assert.AreSame(authored, AutoAdopts.Instance);
        }

        [Test]
        public void ExistsOrFindInScene_FindsAnAuthoredInstance()
        {
            var authored = Fixtures.Author<AutoFindsWithoutCreating>("Authored AutoFinds");
            Assert.IsTrue(AutoFindsWithoutCreating.ExistsOrFindInScene());
            Assert.AreSame(authored, AutoFindsWithoutCreating.Instance);
        }

        [Test]
        public void EditModeCreatedInstance_IsMarkedDontSave()
        {
            // Without DontSave, an instance created because an inspector touched Instance becomes
            // a real member of the open scene and is committed on save, with no undo entry.
            var instance = AutoTransient.Instance;
            Assert.AreEqual(HideFlags.DontSave, instance.gameObject.hideFlags);
        }

        [Test]
        public void Instance_WhenEditModeIsDisabled_ThrowsMissingSingleton()
        {
            Assert.Throws<MissingSingletonException>(() =>
            {
                _ = AutoEditModeDisabled.Instance;
            });
        }

        [Test]
        public void Instance_WhenEditModeIsFindOnly_ResolvesAnAuthoredInstance()
        {
            var authored = Fixtures.Author<AutoEditModeFindOnlyPresent>("Authored FindOnly");
            Assert.AreSame(authored, AutoEditModeFindOnlyPresent.Instance);
        }

        [Test]
        public void Instance_WhenEditModeIsFindOnlyAndSceneIsEmpty_ThrowsMissingSingleton()
        {
            // FindOnly never creates, and Instance does not return null on a policy violation —
            // it throws, exactly as Disabled does. The enum's doc comment claimed null until the
            // behaviour was checked against it.
            Assert.IsFalse(AutoEditModeFindOnlyAbsent.IsAvailable);
            Assert.Throws<MissingSingletonException>(() =>
            {
                _ = AutoEditModeFindOnlyAbsent.Instance;
            });
        }

        [Test]
        public void FindOnly_OnlyExistsOrFindInSceneConsultsTheScene()
        {
            // The trap this policy sets. IsAvailable, Exists and TryGetInstance all read the
            // cache alone, so they report false while an instance is sitting in the loaded scene.
            // ExistsOrFindInScene is the one that searches, and so the guard FindOnly code needs.
            Fixtures.Author<AutoEditModeFindOnlyAvailability>("Authored FindOnly availability");

            Assert.IsFalse(AutoEditModeFindOnlyAvailability.IsAvailable);
            Assert.IsFalse(AutoEditModeFindOnlyAvailability.Exists);
            Assert.IsFalse(AutoEditModeFindOnlyAvailability.TryGetInstance(out _));

            Assert.IsTrue(AutoEditModeFindOnlyAvailability.ExistsOrFindInScene());
            Assert.IsTrue(AutoEditModeFindOnlyAvailability.IsAvailable);   // cached by the search
        }

        [Test]
        public void Disabled_DoesNotResolveEvenWhenAnInstanceIsInTheScene()
        {
            // Disabled means "do not resolve at all outside play mode", and that has to bind the
            // search as well as creation. It did not: MaybeFindInScene consulted only IsQuitting,
            // so ExistsOrFindInScene resolved and cached an instance the policy forbade, leaving
            // TryGetInstance handing out a live object while Instance threw for the same type.
            Fixtures.Author<AutoEditModeDisabledWithSceneInstance>("Authored Disabled");

            Assert.IsFalse(AutoEditModeDisabledWithSceneInstance.ExistsOrFindInScene());
            Assert.IsFalse(AutoEditModeDisabledWithSceneInstance.Exists);
            Assert.IsFalse(AutoEditModeDisabledWithSceneInstance.IsAvailable);
            Assert.IsFalse(AutoEditModeDisabledWithSceneInstance.TryGetInstance(out _));
            Assert.Throws<MissingSingletonException>(() =>
            {
                _ = AutoEditModeDisabledWithSceneInstance.Instance;
            });
        }

        [Test]
        public void KnownGap_PlainFlavourNeverClaimsTheSlotOrDestroysDuplicates()
        {
            // Documented under KNOWN GAPS in MonoBehaviourSingleton.cs: this flavour declares no
            // Awake, so authoring two of them claims nothing and destroys nothing. Asserting that
            // directly keeps this test out of the resolution path, which logs an error by design.
            var first = Fixtures.Author<AutoDuplicatesSurvive>("A");
            var second = Fixtures.Author<AutoDuplicatesSurvive>("B");

            Assert.IsFalse(AutoDuplicatesSurvive.Exists, "no Awake means no slot was claimed");
            Assert.IsTrue(first != null && second != null, "both duplicates are expected to survive");
        }

        [Test]
        public void ResolveDuplicates_PicksTheSameWinnerRegardlessOfCreationOrder()
        {
            // The guarantee is that duplicate resolution is deterministic — scene build index,
            // then ordinal hierarchy path — so the same scene yields the same winner every run.
            // Authoring in reverse order is what makes that a real assertion rather than a
            // restatement of "the first one wins".
            //
            // Resolution logs an error by design. LogCapture intercepts it rather than
            // LogAssert.Expect, so the message can be asserted on directly and a green run leaves
            // nothing in the log.
            Fixtures.Author<AutoResolveOrder>("B");
            var expectedWinner = Fixtures.Author<AutoResolveOrder>("A");

            AutoResolveOrder winner;
            using (var log = new LogCapture())
            {
                winner = AutoResolveOrder.Instance;

                Assert.That(log.Messages, Has.Some.Contains("2 instances of AutoResolveOrder found"),
                    "every candidate should be reported, not just the winner");
                Assert.That(log.Messages, Has.Some.Contains("Keeping 'A'"),
                    "the report should name the instance that kept the slot");
            }

            Assert.AreSame(expectedWinner, winner,
                "'A' sorts before 'B' by ordinal hierarchy path, whichever was created first");
        }
    }
}
