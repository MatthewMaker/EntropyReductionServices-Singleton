using NUnit.Framework;

namespace EntropyReductionServices.Singletons.Tests
{
    /// <summary>
    /// Pins the runtime type's metadata name against the string SingletonAnalyzer hardcodes.
    ///
    /// The analyzer resolves its base type with Compilation.GetTypeByMetadataName and bails out
    /// of Initialize when the lookup returns null, which means a namespace rename disables all
    /// five rules without failing the analyzer build, the package build, or any other test. This
    /// was shipped broken once — the constant still named a namespace (…Core.Util) that the type
    /// had long since moved out of.
    ///
    /// If this test fails, update SingletonBaseMetadataName in Analyzers~/SingletonAnalyzer.cs,
    /// rebuild the analyzer, and commit the DLL.
    /// </summary>
    public class SingletonNamespaceTests
    {
        private const string AnalyzerExpectsMetadataName =
            "EntropyReductionServices.Singletons.MonoBehaviourSingletonBase`1";

        [Test]
        public void RuntimeBaseType_StillMatchesTheNameTheAnalyzerLooksUp()
        {
            var actual = typeof(MonoBehaviourSingletonBase<>).FullName;
            Assert.AreEqual(AnalyzerExpectsMetadataName, actual,
                "the analyzer resolves its base type by this exact string; a mismatch silently " +
                "disables every ERS rule");
        }
    }
}
