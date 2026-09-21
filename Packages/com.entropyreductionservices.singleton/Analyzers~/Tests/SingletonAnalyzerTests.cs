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
        private void OnDestroy() { {|ERS0003:Bus.Instance|}.StartCoroutine(null); }
    }
}");

        [Test]
        public Task ERS0003_DereferenceInOnApplicationQuit_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnApplicationQuit() { var t = {|ERS0003:Bus.Instance|}.transform; }
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
        private void OnDestroy() { {|ERS0003:Bus.Instance|}?.StartCoroutine(null); }
    }
}");

        /// <summary>
        /// The shape this rule exists to stop nagging about. Instance hands back the destroyed
        /// component during teardown, so a call to the singleton's own method runs against live
        /// managed state — which is what an OnDestroy that unregisters itself actually does.
        /// </summary>
        [Test]
        public Task ERS0003_ManagedMemberInTeardown_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { Bus.Instance.Stop(); }
    }
}");

        /// <summary>Inherited UnityEngine members count, though the receiver is the subclass.</summary>
        [Test]
        public Task ERS0003_InheritedUnityMemberInTeardown_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { var n = {|ERS0003:Bus.Instance|}.name; }
    }
}");

        // -- ERS0007: base call present but misordered -------------------------------------------

        /// <summary>
        /// base.Awake() claims the slot and destroys duplicates, so work ahead of it runs even on
        /// an instance that is about to destroy itself.
        /// </summary>
        [Test]
        public Task ERS0007_AwakeCallsBaseLate_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Late : Bus
    {
        private int _x;
        protected override void Awake() { _x = 1; {|ERS0007:base.Awake();|} }
    }
}");

        /// <summary>base.OnDestroy() releases the slot, so anything after it sees no instance.</summary>
        [Test]
        public Task ERS0007_OnDestroyCallsBaseEarly_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Early : Bus
    {
        private int _x;
        protected override void OnDestroy() { {|ERS0007:base.OnDestroy();|} _x = 1; }
    }
}");

        [Test]
        public Task ERS0007_CorrectOrdering_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Right : Bus
    {
        private int _x;
        protected override void Awake() { base.Awake(); _x = 1; }
        protected override void OnDestroy() { _x = 0; base.OnDestroy(); }
    }
}");

        /// <summary>A single statement cannot be out of order with anything.</summary>
        [Test]
        public Task ERS0007_BaseCallAlone_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Only : Bus
    {
        protected override void Awake() { base.Awake(); }
        protected override void OnDestroy() { base.OnDestroy(); }
    }
}");

        /// <summary>
        /// Expression-bodied members are trivially first and last at once, so the rule has
        /// nothing to say about them.
        /// </summary>
        [Test]
        public Task ERS0007_ExpressionBodied_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Arrow : Bus
    {
        protected override void Awake() => base.Awake();
    }
}");

        /// <summary>
        /// A base call nested in a conditional is left alone. CallsBase deliberately accepts it
        /// for ERS0001, and its position is not a simple ordering question — guessing would turn
        /// a permissive rule into a confusing one.
        /// </summary>
        [Test]
        public Task ERS0007_BaseCallInsideConditional_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Guarded : Bus
    {
        private int _x;
        protected override void Awake() { if (_x == 0) { base.Awake(); } _x = 1; }
    }
}");

        // -- ERS0006: '?.' on a lazy singleton's Instance ---------------------------------------

        /// <summary>
        /// Outside teardown a lazy Instance cannot be null, so '?.' is dead rather than dangerous.
        /// ERS0006 rather than ERS0003, and exactly one of them.
        /// </summary>
        [Test]
        public Task ERS0006_NullConditionalOutsideTeardown_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void Update() { {|ERS0006:Bus.Instance|}?.Stop(); }
    }
}");

        /// <summary>
        /// The case a blanket rule would get wrong. A passive singleton's Instance is null until
        /// some component's Awake claims the slot, so '?.' is the correct spelling there.
        /// </summary>
        [Test]
        public Task ERS0006_NullConditionalOnPassiveSingleton_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void Update() { Board.Instance?.Stop(); }
    }
}");

        /// <summary>
        /// Inside teardown the same expression is reported once, as ERS0003 — the more serious
        /// reading, since there '?.' looks like protection and provides none.
        /// </summary>
        [Test]
        public Task ERS0006_InsideTeardown_DefersToErs0003() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { {|ERS0003:Bus.Instance|}?.StartCoroutine(null); }
    }
}");

        /// <summary>
        /// The interaction the narrowing creates: in teardown on a managed member ERS0003 now
        /// declines, and ERS0006 picks it up instead. Still exactly one diagnostic, and the right
        /// one — the call is safe, but the '?.' is as misleading there as anywhere.
        /// </summary>
        [Test]
        public Task ERS0006_InTeardownOnManagedMember_IsReported() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void OnDestroy() { {|ERS0006:Bus.Instance|}?.Stop(); }
    }
}");

        /// <summary>A plain dereference outside teardown is the intended usage.</summary>
        [Test]
        public Task ERS0006_PlainDereference_IsClean() => Harness.Verify(@"
namespace Client
{
    using Consuming;
    public class Holder : UnityEngine.MonoBehaviour
    {
        private void Update() { Bus.Instance.Stop(); }
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
