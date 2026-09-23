using EntropyReductionServices.Singletons.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// The shared-host hazard is scene data, so no analyzer can see it and the runtime cannot fix
    /// it — a Component cannot be moved to another GameObject. These tests pin that the editor-side
    /// validator reports it where it is authored.
    /// </summary>
    public class SingletonHostValidatorTests
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
            _host.AddComponent<ValidatorPersistentHost>();
            _host.AddComponent<ValidatorSecondHost>();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"\[SINGLETON\] 'Managers' hosts 2 singletons"));

            Assert.GreaterOrEqual(SingletonHostValidator.ValidateLoadedScenes(), 1);
        }

        [Test]
        public void StatefulPersistentSingletonWithAnOrdinaryComponent_IsReported()
        {
            _host = new GameObject("Audio");
            _host.AddComponent<ValidatorStatefulHost>();
            _host.AddComponent<AudioSource>();   // dragged to DontDestroyOnLoad on play

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"hosts the persistent singleton 'ValidatorStatefulHost' alongside AudioSource"));

            Assert.GreaterOrEqual(SingletonHostValidator.ValidateLoadedScenes(), 1);
        }

        [Test]
        public void StatelessPersistentSingleton_IsNotReported_BecauseItRebuildsItself()
        {
            _host = new GameObject("Audio");
            _host.AddComponent<ValidatorPersistentHost>();   // no serialized fields
            _host.AddComponent<AudioSource>();

            Assert.AreEqual(0, SingletonHostValidator.ValidateLoadedScenes());
        }

        [Test]
        public void ASingletonAloneOnItsObject_IsNotReported()
        {
            _host = new GameObject("Lonely");
            _host.AddComponent<ValidatorPersistentHost>();

            Assert.AreEqual(0, SingletonHostValidator.ValidateLoadedScenes());
        }
    }
}
