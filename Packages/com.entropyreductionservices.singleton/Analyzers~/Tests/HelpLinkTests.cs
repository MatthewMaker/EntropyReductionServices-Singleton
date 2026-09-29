using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using EntropyReductionServices.Analyzers;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// Checks that every analyzer help link lands on its rule in Documentation~/analyzers.md.
    ///
    /// The links point at <c>analyzers.md#ers000N</c> on <c>main</c>, and GitHub resolves that
    /// fragment only if the page has an explicit <c>&lt;a id="ers000N"&gt;</c>: the headings
    /// generate longer slugs. A missing anchor opens the top of the page instead, which nothing
    /// else notices. Because the links follow <c>main</c>, DLLs from older releases rely on the
    /// current page too, so rules that have since been removed still need an anchor.
    /// </summary>
    [TestFixture]
    public class HelpLinkTests
    {
        private const string DocsPathSuffix =
            "/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md";

        private static readonly Regex ShippedRuleLine =
            new Regex(@"^(?<rule>ERS\d{4})\s*\|", RegexOptions.Multiline);

        /// <summary>Reads one [AssemblyMetadata] value injected by the test csproj.</summary>
        private static string Metadata(string key) =>
            typeof(HelpLinkTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(attribute => attribute.Key == key)
                .Value;

        /// <summary>Reads a file whose path the test csproj injected, failing if it is absent.</summary>
        private static string ReadInjected(string key)
        {
            var path = Path.GetFullPath(Metadata(key));
            Assert.That(File.Exists(path), Is.True, $"No file at {path}");
            return File.ReadAllText(path);
        }

        /// <summary>Whether the rule docs declare an explicit anchor with this id.</summary>
        private static bool HasAnchor(string docs, string fragment) =>
            docs.Contains($"<a id=\"{fragment}\"></a>");

        [Test]
        public void EveryDeclaredRule_LinksToAnAnchorInTheRuleDocs()
        {
            var docs = ReadInjected("AnalyzerDocsPath");

            foreach (var descriptor in new SingletonAnalyzer().SupportedDiagnostics)
            {
                var parts = descriptor.HelpLinkUri.Split('#');

                Assert.That(parts, Has.Length.EqualTo(2),
                    $"{descriptor.Id}'s help link has no fragment: {descriptor.HelpLinkUri}");
                Assert.That(parts[0], Does.EndWith(DocsPathSuffix),
                    $"{descriptor.Id}'s help link does not point at the rule docs: {descriptor.HelpLinkUri}");
                Assert.That(HasAnchor(docs, parts[1]), Is.True,
                    $"analyzers.md has no <a id=\"{parts[1]}\"></a>, so {descriptor.Id}'s help "
                    + "link opens the top of the page.");
            }
        }

        [Test]
        public void EveryShippedRule_HasAnAnchorInTheRuleDocs()
        {
            var docs = ReadInjected("AnalyzerDocsPath");
            var shipped = ShippedRuleLine.Matches(ReadInjected("ShippedReleasesPath"))
                .Cast<Match>()
                .Select(match => match.Groups["rule"].Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            Assert.That(shipped, Is.Not.Empty, "Parsed no rules from AnalyzerReleases.Shipped.md.");

            var missing = shipped.Where(id => !HasAnchor(docs, id.ToLowerInvariant())).ToArray();

            Assert.That(missing, Is.Empty,
                "These rules shipped in a released DLL whose help link points at analyzers.md on "
                + "main, which has no anchor for them: " + string.Join(", ", missing));
        }
    }
}
