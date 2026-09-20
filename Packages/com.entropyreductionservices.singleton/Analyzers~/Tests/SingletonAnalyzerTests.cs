using System.Threading.Tasks;
using NUnit.Framework;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// One fixture per rule: at least one snippet that must report, and at least one neighbouring
    /// snippet that must stay silent. The negative cases carry as much weight as the positive
    /// ones — these rules ship as warnings to every consumer on first reference, so a false
    /// positive lands in the build of someone who never opted in.
    /// </summary>
    public class SingletonAnalyzerTests
    {
        // -- ERS0001: an override that never chains to base ------------------------------------

        [Test]
        public Task ERS0001_AwakeOverrideWithoutBaseCall_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Radio : Bus
    {
        protected override void {|ERS0001:Awake|}() { }
    }
}");

        [Test]
        public Task ERS0001_OnDestroyOverrideWithoutBaseCall_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Radio : Bus
    {
        protected override void {|ERS0001:OnDestroy|}() { }
    }
}");

        [Test]
        public Task ERS0001_OverrideThatCallsBase_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Radio : Bus
    {
        protected override void Awake() { base.Awake(); }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}");

        [Test]
        public Task ERS0001_BaseCallInsideABranch_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Radio : Bus
    {
        // The check is deliberately flow-insensitive: a base call anywhere counts.
        protected override void Awake() { if (true) { base.Awake(); } }
    }
}");

        // -- ERS0005: a declaration that hides the virtual instead of overriding it -------------

        [Test]
        public Task ERS0005_AwakeDeclaredWithoutOverride_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Radio : Bus
    {
        protected void {|ERS0005:Awake|}() { }
    }
}");

        [Test]
        public Task ERS0005_UnrelatedMonoBehaviourDeclaringAwake_IsClean() => Harness.Verify(@"
namespace Client
{
    public class Plain : UnityEngine.MonoBehaviour
    {
        // Not a singleton subclass, so its Awake is none of the analyzer's business.
        private void Awake() { }
    }
}");

        // -- ERS0002: storing Instance in a field ----------------------------------------------

        [Test]
        public Task ERS0002_AssignmentToAField_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private Bus _bus;
        private void Start() { _bus = {|ERS0002:Bus.Instance|}; }
    }
}");

        [Test]
        public Task ERS0002_FieldInitializerOffAMonoBehaviour_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder
    {
        // Not a MonoBehaviour, so this is a stale-cache problem rather than a
        // construction-time one, and ERS0002 is the rule that applies.
        private Bus _bus = {|ERS0002:Bus.Instance|};
    }
}");

        [Test]
        public Task ERS0002_LocalVariable_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        // A value used within one method call is exactly the intended usage.
        private void Start() { var bus = Bus.Instance; bus.Stop(); }
    }
}");

        // -- ERS0004: access during MonoBehaviour construction ---------------------------------

        [Test]
        public Task ERS0004_InstanceFieldInitializerOnAMonoBehaviour_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private Bus _bus = {|ERS0004:Bus.Instance|};
    }
}");

        [Test]
        public Task ERS0004_ConstructorOnAMonoBehaviour_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        public Holder() { var bus = {|ERS0004:Bus.Instance|}; }
    }
}");

        [Test]
        public Task ERS0004_StaticFieldInitializer_FallsThroughToERS0002() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        // Static initializers do not run on Unity's deserialization path, so ERS0004 does not
        // apply. The contract forbids caching Instance in a static field just as firmly though,
        // and ERS0002 is the rule that says so — the two rules partition this case rather than
        // leaving it uncovered.
        private static Bus s_bus = {|ERS0002:Bus.Instance|};
    }
}");

        // -- ERS0003: unguarded dereference in a teardown callback ------------------------------

        [Test]
        public Task ERS0003_DereferenceInOnDestroy_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { {|ERS0003:Bus.Instance|}.Stop(); }
    }
}");

        [Test]
        public Task ERS0003_DereferenceInOnApplicationQuit_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnApplicationQuit() { {|ERS0003:Bus.Instance|}.Stop(); }
    }
}");

        /// <summary>
        /// OnDisable is not a teardown callback for this rule's purposes: it also runs during
        /// ordinary play, where Instance is guaranteed non-null.
        /// </summary>
        [Test]
        public Task ERS0003_DereferenceInOnDisable_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDisable() { Bus.Instance.Stop(); }
    }
}");

        /// <summary>
        /// '?.' reads as a guard and is not one. It tests the reference, and during teardown
        /// Instance hands back the destroyed component — a live C# object — so the call proceeds.
        /// This was exempt until the teardown semantics changed; it is now the spelling most
        /// likely to be wrong, so it must be reported.
        /// </summary>
        [Test]
        public Task ERS0003_NullConditionalAccess_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { {|ERS0003:Bus.Instance|}?.Stop(); }
    }
}");

        /// <summary>Outside teardown a live singleton is guaranteed, so '?.' is unremarkable.</summary>
        [Test]
        public Task ERS0003_NullConditionalAccessOutsideTeardown_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void Update() { Bus.Instance?.Stop(); }
    }
}");

        [Test]
        public Task ERS0003_TryGetInstanceGuard_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy()
        {
            Bus bus;
            if (Bus.TryGetInstance(out bus)) bus.Stop();
        }
    }
}");

        [Test]
        public Task ERS0003_IsAvailableGuard_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { if (Bus.IsAvailable) Bus.Instance.Stop(); }
    }
}");

        [Test]
        public Task ERS0003_DereferenceOutsideTeardown_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        // Normal operation: the contract says dereference directly, no guard required.
        private void Start() { Bus.Instance.Stop(); }
    }
}");
    }
}
