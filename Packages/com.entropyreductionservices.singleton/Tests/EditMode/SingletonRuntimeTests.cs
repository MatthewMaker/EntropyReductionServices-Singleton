using NUnit.Framework;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// SingletonRuntime is what makes the cache non-stale across play sessions. A domain-bound
    /// EditMode test cannot cross a session boundary, so this covers only the invariants that
    /// hold within one.
    /// </summary>
    public class SingletonRuntimeTests
    {
        [Test]
        public void IsQuitting_IsFalseOutsideShutdown()
        {
            Assert.IsFalse(SingletonRuntime.IsQuitting);
        }

        [Test]
        public void SessionId_ReadIsSideEffectFree()
        {
            // Not the session-boundary guarantee — a domain-bound test cannot cross one. This
            // only pins that reading the id does not advance it, which is what would break the
            // staleness comparison every singleton's cache depends on.
            var first = SingletonRuntime.SessionId;
            _ = SingletonRuntime.SessionId;
            Assert.AreEqual(first, SingletonRuntime.SessionId);
        }
    }
}
