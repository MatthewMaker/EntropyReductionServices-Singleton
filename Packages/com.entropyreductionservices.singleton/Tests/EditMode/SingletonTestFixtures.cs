using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// Probe types for the contract tests.
    ///
    /// Every type is used by exactly one test, because the singleton cache is a static on the
    /// closed generic type and EditMode tests share one domain: two tests over one probe type
    /// would see each other's state. A test that needs a type whose cache has never been touched
    /// ("virgin") is the reason several of these look interchangeable but are not.
    ///
    /// [ExecuteAlways] is what makes Awake and OnDestroy run outside play mode. Without it the
    /// AssignInAwake / DestroyDuplicate paths — most of the contract — are unreachable from an
    /// EditMode test.
    /// </summary>
    internal static class Fixtures
    {
        /// <summary>
        /// Destroys every probe left behind by a test, releasing the singleton slots via
        /// OnDestroy — DestroyImmediate rather than Destroy so that happens before the next test
        /// starts rather than at end of frame.
        ///
        /// Sweeps by assembly rather than taking a list of types: a per-type list has to be
        /// restated in every fixture's teardown, and a probe added without its matching line
        /// leaks static state into the next test silently — the exact failure the one-probe-type-
        /// per-test rule exists to prevent. Everything this can reach is a probe declared below.
        ///
        /// FindObjectsOfTypeAll, not FindObjectsByType, because edit-mode transients carry
        /// HideFlags.DontSave and the ordinary find APIs skip them.
        /// </summary>
        /// <summary>Hoisted out of the sweep below, which runs over every live MonoBehaviour.</summary>
        private static readonly System.Reflection.Assembly OwnAssembly = typeof(Fixtures).Assembly;

        public static void PurgeAll()
        {
            foreach (var probe in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (probe == null) continue;
                if (probe.GetType().Assembly != OwnAssembly) continue;
                if (!probe.gameObject.scene.IsValid()) continue;   // a prefab asset, not ours
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>Creates a scene GameObject hosting a probe, named for the calling test.</summary>
        public static T Author<T>(string name) where T : MonoBehaviour
        {
            return new GameObject(name).AddComponent<T>();
        }
    }

    /// <summary>
    /// Redirects Unity's log handler for the duration of a block, so a call that logs by design
    /// can be asserted on without the line reaching the Console or the Editor log.
    ///
    /// LogAssert.Expect is the usual tool and it does keep a test green, but it only suppresses
    /// the failure: Debug.LogError has already written to the native log pipeline by the time the
    /// expectation is matched. Replacing the handler intercepts one level earlier, which both
    /// keeps a green run's log clean and lets the assertion inspect the message text instead of
    /// pattern-matching whatever escaped.
    /// </summary>
    internal sealed class LogCapture : IDisposable, ILogHandler
    {
        private readonly ILogHandler _previous;
        private readonly List<string> _messages = new List<string>();

        public LogCapture()
        {
            _previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = this;
        }

        public IReadOnlyList<string> Messages => _messages;

        public void Dispose() => Debug.unityLogger.logHandler = _previous;

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
            => _messages.Add(args == null || args.Length == 0 ? format : string.Format(format, args));

        public void LogException(Exception exception, Object context)
            => _messages.Add(exception.ToString());
    }

    // --- MonoBehaviourSingleton<T> (lazy, auto-creating, no Awake) -----------------------------

    [ExecuteAlways] internal class AutoCreates : MonoBehaviourSingleton<AutoCreates> { }
    [ExecuteAlways] internal class AutoStable : MonoBehaviourSingleton<AutoStable> { }
    [ExecuteAlways] internal class AutoVirgin : MonoBehaviourSingleton<AutoVirgin> { }
    [ExecuteAlways] internal class AutoVirginTryGet : MonoBehaviourSingleton<AutoVirginTryGet> { }
    [ExecuteAlways] internal class AutoAdopts : MonoBehaviourSingleton<AutoAdopts> { }
    [ExecuteAlways] internal class AutoFindsWithoutCreating : MonoBehaviourSingleton<AutoFindsWithoutCreating> { }
    [ExecuteAlways] internal class AutoDuplicatesSurvive : MonoBehaviourSingleton<AutoDuplicatesSurvive> { }
    [ExecuteAlways] internal class AutoResolveOrder : MonoBehaviourSingleton<AutoResolveOrder> { }
    [ExecuteAlways] internal class AutoTransient : MonoBehaviourSingleton<AutoTransient> { }

    [ExecuteAlways]
    [SingletonEditMode(SingletonEditModePolicy.Disabled)]
    internal class AutoEditModeDisabled : MonoBehaviourSingleton<AutoEditModeDisabled> { }

    // --- MonoBehaviourSingletonPersistent<T> --------------------------------------------------

    [ExecuteAlways] internal class PersistentClaims : MonoBehaviourSingletonPersistent<PersistentClaims> { }
    [ExecuteAlways] internal class PersistentDedupes : MonoBehaviourSingletonPersistent<PersistentDedupes> { }
    [ExecuteAlways] internal class PersistentReleases : MonoBehaviourSingletonPersistent<PersistentReleases> { }
    [ExecuteAlways] internal class PersistentEditMode : MonoBehaviourSingletonPersistent<PersistentEditMode> { }

    /// <summary>Opts out of the whole-GameObject blast radius, so a duplicate loses only itself.</summary>
    [ExecuteAlways]
    internal class PersistentComponentOnly : MonoBehaviourSingletonPersistent<PersistentComponentOnly>
    {
        protected override bool DestroyWholeGameObject => false;
    }

    // --- MonoBehaviourSingletonPassive<T> -----------------------------------------------------

    [ExecuteAlways] internal class PassiveNullUntilAwake : MonoBehaviourSingletonPassive<PassiveNullUntilAwake> { }
    [ExecuteAlways] internal class PassiveNeverCreates : MonoBehaviourSingletonPassive<PassiveNeverCreates> { }
    [ExecuteAlways] internal class PassiveClaims : MonoBehaviourSingletonPassive<PassiveClaims> { }
    [ExecuteAlways] internal class PassiveDedupes : MonoBehaviourSingletonPassive<PassiveDedupes> { }
    [ExecuteAlways] internal class PassiveReleases : MonoBehaviourSingletonPassive<PassiveReleases> { }

    // --- Shutdown tombstone (LastKnown) --------------------------------------------------------

    /// <summary>
    /// Exposes LastKnown, which is protected on the base, and carries a plain C# member so a test
    /// can prove that calling it on the destroyed component is safe. Counter is static because
    /// the point is to observe the call landing on an object whose native peer is gone.
    /// </summary>
    [ExecuteAlways]
    internal class TombstoneReachable : MonoBehaviourSingleton<TombstoneReachable>
    {
        public static int Bumps;
        internal static TombstoneReachable Exposed => LastKnown;
        public void Bump() => Bumps++;
    }

    [ExecuteAlways]
    internal class TombstoneVirgin : MonoBehaviourSingleton<TombstoneVirgin>
    {
        internal static TombstoneVirgin Exposed => LastKnown;
    }

    [ExecuteAlways]
    internal class TombstoneUnityEquality : MonoBehaviourSingleton<TombstoneUnityEquality>
    {
        internal static TombstoneUnityEquality Exposed => LastKnown;
    }

    [ExecuteAlways]
    internal class TombstonePassive : MonoBehaviourSingletonPassive<TombstonePassive>
    {
        internal static TombstonePassive Exposed => LastKnown;
    }

    // --- MonoBehaviourSingletonPassivePersistent<T> -------------------------------------------

    [ExecuteAlways] internal class PassivePersistentClaims : MonoBehaviourSingletonPassivePersistent<PassivePersistentClaims> { }
    [ExecuteAlways] internal class PassivePersistentDedupes : MonoBehaviourSingletonPassivePersistent<PassivePersistentDedupes> { }
    [ExecuteAlways] internal class PassivePersistentNeverCreates : MonoBehaviourSingletonPassivePersistent<PassivePersistentNeverCreates> { }
}
