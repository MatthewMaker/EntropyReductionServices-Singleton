using System;
using System.Diagnostics;
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

            // The informational version, which FileVersionInfo reports as ProductVersion, is the
            // one that keeps a pre-release suffix: AssemblyVersion reads 3.0.0.0 for 3.0.0-pre.1
            // and for 3.0.0-pre.2 alike, so it cannot tell a stale pre-release DLL apart. Read from
            // the file's metadata without loading it, so it cannot collide with the copy already
            // loaded through the project reference.
            var actual = FileVersionInfo.GetVersionInfo(path).ProductVersion;

            Assert.That(
                actual,
                Is.EqualTo(expected),
                "The committed analyzer DLL is stale. Rebuild it and copy it into Runtime/Analyzers/ "
                + "— see the release checklist in CLAUDE.md.");
        }
    }
}
