using EntropyReductionServices.Singletons.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The shared-GameObject hazard is scene data, so no analyzer can see it and the runtime cannot fix
    /// it — a Component cannot be moved to another GameObject. These tests pin that the editor-side
    /// validator reports it where it is authored.
    /// </summary>
    public class SingletonPlacementValidatorTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            Fixtures.PurgeAll();
        }

        [Test]
        public void TwoSingletonsOnOneObject_AreReported()
        {
            _host = new GameObject("Managers");
            _host.AddComponent<ValidatorStatelessSingleton>();
            _host.AddComponent<ValidatorSecondSingleton>();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"\[SINGLETON\] 'Managers' has 2 singletons on it"));

            Assert.GreaterOrEqual(SingletonPlacementValidator.ValidateLoadedScenes(), 1);
        }

        [Test]
        public void StatefulPersistentSingletonWithAnOrdinaryComponent_IsReported()
        {
            _host = new GameObject("Audio");
            _host.AddComponent<ValidatorStatefulSingleton>();
            _host.AddComponent<AudioSource>();   // dragged to DontDestroyOnLoad on play

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"carries the persistent singleton 'ValidatorStatefulSingleton' alongside AudioSource"));

            Assert.GreaterOrEqual(SingletonPlacementValidator.ValidateLoadedScenes(), 1);
        }

        [Test]
        public void StatelessPersistentSingleton_IsNotReported_BecauseItRebuildsItself()
        {
            _host = new GameObject("Audio");
            _host.AddComponent<ValidatorStatelessSingleton>();   // no serialized fields
            _host.AddComponent<AudioSource>();

            Assert.AreEqual(0, SingletonPlacementValidator.ValidateLoadedScenes());
        }

        [Test]
        public void ASingletonAloneOnItsObject_IsNotReported()
        {
            _host = new GameObject("Lonely");
            _host.AddComponent<ValidatorStatelessSingleton>();

            Assert.AreEqual(0, SingletonPlacementValidator.ValidateLoadedScenes());
        }
    }
}
