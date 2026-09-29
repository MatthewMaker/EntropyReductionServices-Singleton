using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// The README's Timers example, run under the default policies: created on first use, and
    /// authored on a GameObject it shares with another component.
    /// </summary>
    public class QuickStartExampleTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("QuickStartExampleScene");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Probes.PurgeAll();

            if (_scene.IsValid() && _scene.isLoaded) yield return Probes.UnloadAndWait(_scene);
        }

        /// <summary>Yields until the flag is set or the frame budget runs out.</summary>
        private static IEnumerator WaitFor(Func<bool> done, int frames = 30)
        {
            for (var i = 0; i < frames && !done(); i++) yield return null;
        }

        [UnityTest]
        public IEnumerator CreatedOnFirstUse_RunsATimer()
        {
            var fired = false;
            QuickStartLazyTimers.Instance.After(0f, () => fired = true);

            yield return WaitFor(() => fired);

            Assert.IsTrue(fired, "the timer never fired");
        }

        [UnityTest]
        public IEnumerator CreatedOnFirstUse_CancelsATimer()
        {
            var fired = false;
            var timer = QuickStartCancelTimers.Instance.After(0.05f, () => fired = true);
            QuickStartCancelTimers.Instance.Cancel(timer);
            QuickStartCancelTimers.Instance.Cancel(null);   // a timer never started

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.IsFalse(fired, "a cancelled timer fired");
        }

        [UnityTest]
        public IEnumerator AuthoredOnASharedGameObject_RunsATimer()
        {
            var owner = new GameObject("Managers");
            owner.SetActive(false);
            owner.AddComponent<QuickStartSharedTimers>();
            owner.AddComponent<AudioSource>();
            owner.SetActive(true);

            yield return null;

            var fired = false;
            QuickStartSharedTimers.Instance.After(0f, () => fired = true);

            yield return WaitFor(() => fired);

            Assert.IsTrue(fired, "the timer never fired");
            Assert.IsFalse(Probes.IsPersistent(owner), "the shared GameObject must not be persisted");

            Object.DestroyImmediate(owner);
        }
    }

    /// <summary>
    /// The README's Timers body, verbatim, behind a generic base so each test gets its own
    /// singleton type. Keep it in step with the README.
    /// </summary>
    internal abstract class QuickStartTimers<T> : MonoBehaviourSingleton<T> where T : QuickStartTimers<T>
    {
        public Coroutine After(float seconds, Action callback) => StartCoroutine(Run(seconds, callback));

        public void Cancel(Coroutine timer)
        {
            if (timer != null) StopCoroutine(timer);
        }

        private static IEnumerator Run(float seconds, Action callback)
        {
            yield return new WaitForSecondsRealtime(seconds);
            callback();
        }
    }

    internal class QuickStartLazyTimers : QuickStartTimers<QuickStartLazyTimers> { }
    internal class QuickStartCancelTimers : QuickStartTimers<QuickStartCancelTimers> { }
    internal class QuickStartSharedTimers : QuickStartTimers<QuickStartSharedTimers> { }
}
