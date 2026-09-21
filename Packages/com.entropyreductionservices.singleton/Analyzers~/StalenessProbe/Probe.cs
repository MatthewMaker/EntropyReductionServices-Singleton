// Stub Unity and singleton types, then one violation per rule. Kept in a single file so the
// CI step can assert on diagnostic codes without tracking line numbers.

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

    public class Ers0007 : Bus
    {
        private int _x;
        protected override void Awake() { _x = 1; base.Awake(); }   // ERS0007 (base call too late)
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
        // ERS0003 now fires only on UnityEngine-declared members, so the probe must use one:
        // Bus.Instance.Stop() is managed and deliberately clean.
        private void OnDestroy() { Bus.Instance.StartCoroutine(null); }   // ERS0003
        private void Update() { Bus.Instance?.Stop(); }         // ERS0006 (dead null-conditional)
    }
}
