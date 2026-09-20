// Stub Unity and singleton types, then one violation per rule. Kept in a single file so the
// CI step can assert on diagnostic codes without tracking line numbers.

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class MonoBehaviour : Component { }
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

    // Kept in step with Tests/AnalyzerHarness.cs deliberately: if the probe models a smaller
    // hierarchy than the unit tests, a rule that only misbehaves on the passive base passes the
    // committed-DLL check.
    public abstract class MonoBehaviourSingletonPassive<T> : MonoBehaviourSingletonBase<T>
        where T : MonoBehaviourSingletonPassive<T>
    {
        public static T Instance { get { return null; } }
        protected virtual void Awake() { }
    }
}

namespace Probe
{
    using EntropyReductionServices.Singletons;

    public class Bus : MonoBehaviourSingletonPersistent<Bus>
    {
        public void Stop() { }
    }

    public class Ers0001 : Bus
    {
        protected override void Awake() { }                     // no base.Awake()
    }

    public class Ers0005 : Bus
    {
        protected void OnDestroy() { }                          // hides instead of overriding
    }

    public class Ers0002And0003And0004 : UnityEngine.MonoBehaviour
    {
        private Bus _cached = Bus.Instance;                     // ERS0004 (construction time)
        private Bus _assigned;

        private void Start() { _assigned = Bus.Instance; }      // ERS0002 (field cache)
        private void OnDestroy() { Bus.Instance.Stop(); }       // ERS0003 (unguarded teardown)
    }
}
