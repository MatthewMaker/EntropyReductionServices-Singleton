// The deprecated passive flavours keep their coverage until they are removed in 3.0.0, so this
// file names them on purpose.
#pragma warning disable CS0618

using NUnit.Framework;
using UnityEngine;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// Pins the runtime types' metadata names against the strings SingletonAnalyzer hardcodes,
    /// and the inheritance shape its rules reason about.
    ///
    /// The analyzer resolves these with Compilation.GetTypeByMetadataName and registers no actions
    /// when a lookup returns null, so a namespace rename disables rules without failing the
    /// analyzer build, the package build, or any other test. That shipped once — the constant
    /// still named a namespace (…Core.Util) the type had long since moved out of.
    ///
    /// Every constant is covered, not just the base: a rename of MonoBehaviourSingleton&lt;T&gt;
    /// would disable ERS0006 alone, which is quieter still.
    ///
    /// If one of these fails, update the matching constant in Analyzers~/SingletonAnalyzer.cs,
    /// rebuild the analyzer, and commit the DLL.
    /// </summary>
    public class SingletonNamespaceTests
    {
        // Mirrors the *MetadataName constants in Analyzers~/SingletonAnalyzer.cs, one case each.
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingletonBase`1",
            typeof(MonoBehaviourSingletonBase<>))]
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingleton`1",
            typeof(MonoBehaviourSingleton<>))]
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingletonPersistent`1",
            typeof(MonoBehaviourSingletonPersistent<>))]
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingletonPassive`1",
            typeof(MonoBehaviourSingletonPassive<>))]
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingletonPassivePersistent`1",
            typeof(MonoBehaviourSingletonPassivePersistent<>))]
        [TestCase("UnityEngine.MonoBehaviour", typeof(MonoBehaviour))]
        [TestCase("UnityEngine.Component", typeof(Component))]
        [TestCase("UnityEngine.Object", typeof(UnityEngine.Object))]
        [TestCase("UnityEngine.ISerializationCallbackReceiver", typeof(ISerializationCallbackReceiver))]
        [TestCase("UnityEngine.SerializeField", typeof(SerializeField))]
        public void RuntimeType_StillMatchesTheNameTheAnalyzerLooksUp(string expected, System.Type actual)
        {
            Assert.AreEqual(expected, actual.FullName,
                "the analyzer resolves this type by this exact string; a mismatch silently " +
                "disables the rules that depend on it");
        }

        /// <summary>
        /// ERS0006 reports '?.' only on the lazy flavour, because a passive Instance is genuinely
        /// null until an Awake claims the slot. That distinction is inheritance, not naming, and
        /// the analyzer's test stubs model it — so it is pinned here against the real types.
        /// </summary>
        [Test]
        public void PassiveFlavour_IsNotALazySingleton()
        {
            Assert.IsFalse(DerivesFrom(typeof(MonoBehaviourSingletonPassive<>), typeof(MonoBehaviourSingleton<>)),
                "ERS0006 must not fire on the passive flavour, where '?.' is a real guard");
            Assert.IsTrue(DerivesFrom(typeof(MonoBehaviourSingletonPersistent<>), typeof(MonoBehaviourSingleton<>)),
                "ERS0006 must still fire on the persistent flavour");
        }

        /// <summary>
        /// Walks the base chain comparing generic type definitions. Type.IsSubclassOf cannot answer
        /// this: the base of the open MonoBehaviourSingletonPersistent&lt;T&gt; is the constructed
        /// MonoBehaviourSingleton&lt;T&gt;, which is never equal to the open definition.
        /// </summary>
        private static bool DerivesFrom(System.Type type, System.Type openBaseDefinition)
        {
            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openBaseDefinition)
                    return true;
            }

            return false;
        }
    }
}
