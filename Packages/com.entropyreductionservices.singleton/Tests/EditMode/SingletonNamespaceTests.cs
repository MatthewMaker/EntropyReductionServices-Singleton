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
        [TestCase("EntropyReductionServices.Singletons.MonoBehaviourSingleton`1",
            typeof(MonoBehaviourSingleton<>))]
        [TestCase("EntropyReductionServices.Singletons.SingletonLifetimeAttribute",
            typeof(SingletonLifetimeAttribute))]
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
    }
}
