// The stub Unity and singleton hierarchy, shared by the analyzer unit tests and the staleness
// probe so the two cannot model different shapes.
//
// It is source rather than a reference to the real assemblies so the tests run without Unity
// installed, and it mirrors the shape the analyzer actually inspects: which type declares
// Instance, which declares the virtual Awake, and which inherits from UnityEngine.MonoBehaviour.
// Getting that shape wrong would make the tests agree with themselves and with nothing else.
//
// Deliberately free of diagnostics: the virtual Awake on the persistent and passive bases is not
// an override of anything, and no base singleton type declares Awake above them, so neither
// ERS0001 nor ERS0005 applies to the stubs themselves.
//
// The probe consumes this as compiled source; the test harness embeds it and prepends it to every
// snippet. Previously both kept their own copy, and a comment asked the next reader to keep them
// in step — if the probe modelled a smaller hierarchy than the unit tests, a rule that only
// misbehaved on the passive base would pass the committed-DLL check.

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
