using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

using EntropyReductionServices.Analyzers;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// Compiles a snippet against stub Unity and singleton types and checks which diagnostics the
    /// analyzer produces.
    ///
    /// The stubs are source rather than a reference to the real assemblies so the tests run
    /// without Unity installed. They mirror the shape the analyzer actually inspects: which type
    /// declares Instance, which declares the virtual Awake, and which inherits from
    /// UnityEngine.MonoBehaviour. Getting that shape wrong would make the tests agree with
    /// themselves and with nothing else.
    /// </summary>
    internal static class Harness
    {
        /// <summary>
        /// Stub declarations prepended to every snippet. Deliberately free of diagnostics:
        /// the virtual Awake on the persistent and passive bases is not an override of anything,
        /// and no base singleton type declares Awake above them, so neither ERS0001 nor ERS0005
        /// applies to the stubs themselves.
        /// </summary>
        private const string Prelude = @"
namespace UnityEngine
{
    // Enough of the real hierarchy to exercise ERS0003, which now fires only on members declared
    // in the UnityEngine namespace — those are the ones backed by the native peer.
    public class Object { public string name; }
    public class Component : Object { public Transform transform; }
    public class Transform : Component { }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { public void StartCoroutine(object routine) { } }
}

namespace EntropyReductionServices.Singletons
{
    public abstract class MonoBehaviourSingletonBase<T> : UnityEngine.MonoBehaviour
        where T : MonoBehaviourSingletonBase<T>
    {
        public static bool IsAvailable { get { return true; } }
        public static bool TryGetInstance(out T instance) { instance = null; return false; }
        protected virtual void OnDestroy() { }
    }

    public abstract class MonoBehaviourSingleton<T> : MonoBehaviourSingletonBase<T>
        where T : MonoBehaviourSingleton<T>
    {
        public static T Instance { get { return null; } }
    }

    public abstract class MonoBehaviourSingletonPersistent<T> : MonoBehaviourSingleton<T>
        where T : MonoBehaviourSingletonPersistent<T>
    {
        protected virtual void Awake() { }
    }

    public abstract class MonoBehaviourSingletonPassive<T> : MonoBehaviourSingletonBase<T>
        where T : MonoBehaviourSingletonPassive<T>
    {
        public static T Instance { get { return null; } }
        protected virtual void Awake() { }
    }
}

namespace Consuming
{
    using EntropyReductionServices.Singletons;

    public class Bus : MonoBehaviourSingletonPersistent<Bus>
    {
        public void Stop() { }
    }

    // Passive: Instance is null until an Awake claims the slot, so '?.' on it is a real guard.
    public class Board : MonoBehaviourSingletonPassive<Board>
    {
        public void Stop() { }
    }
}
";

        /// <summary>
        /// Runs the analyzer over the snippet. Expected diagnostics are written inline as
        /// {|ERS0001:span|} markup, which keeps them anchored to the code rather than to line
        /// numbers that shift whenever the prelude changes.
        /// </summary>
        public static Task Verify(string snippet)
        {
            var test = new CSharpAnalyzerTest<SingletonAnalyzer, DefaultVerifier>
            {
                TestCode = Prelude + snippet,
            };

            return test.RunAsync();
        }
    }
}
