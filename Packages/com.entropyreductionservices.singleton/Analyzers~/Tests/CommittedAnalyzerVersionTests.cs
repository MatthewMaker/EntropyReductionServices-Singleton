using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// Checks that the committed analyzer DLL — the artifact consumers actually receive — claims
    /// the version package.json advertises.
    ///
    /// The release checklist says to rebuild and recommit the DLL after a version bump, because
    /// the csproj stamps the version into the assembly. Skipping that ships a binary claiming the
    /// old version, and nothing else notices: the staleness probe checks behaviour, and a version
    /// bump does not change behaviour. This is the check that does notice.
    /// </summary>
    [TestFixture]
    public class CommittedAnalyzerVersionTests
    {
        /// <summary>Reads one [AssemblyMetadata] value injected by the test csproj.</summary>
        private static string Metadata(string key) =>
            typeof(CommittedAnalyzerVersionTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(attribute => attribute.Key == key)
                .Value;

        [Test]
        public void CommittedDll_ClaimsThePackageVersion()
        {
            var expected = Metadata("ExpectedAnalyzerVersion");
            var path = Path.GetFullPath(Metadata("CommittedAnalyzerPath"));

            Assert.That(File.Exists(path), Is.True, $"No committed analyzer DLL at {path}");

            // GetAssemblyName reads metadata without loading, so this cannot collide with the
            // copy already loaded through the project reference.
            var actual = AssemblyName.GetAssemblyName(path).Version;

            Assert.That(
                $"{actual.Major}.{actual.Minor}.{actual.Build}",
                Is.EqualTo(expected),
                "The committed analyzer DLL is stale. Rebuild it and copy it into Runtime/Analyzers/ "
                + "— see the release checklist in CLAUDE.md.");
        }
    }
}
