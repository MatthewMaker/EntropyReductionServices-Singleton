// One violation per rule, compiled against the COMMITTED analyzer DLL. Kept in a single file so
// the CI step can assert on diagnostic codes without tracking line numbers.
//
// The stub Unity and singleton hierarchy these types derive from lives in ../Shared/SingletonStubs.cs,
// compiled in via the csproj and shared with the analyzer unit tests.

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
