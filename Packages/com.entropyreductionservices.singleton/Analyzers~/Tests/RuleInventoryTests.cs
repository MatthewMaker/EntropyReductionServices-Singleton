using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using EntropyReductionServices.Analyzers;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// Checks that the analyzer's own rule list and .editorconfig agree on which rules exist.
    ///
    /// .editorconfig is the source of truth for severities, and scripts/sync-analyzer-severities.py
    /// generates both Default.ruleset copies and the staleness probe's expected-id list from it.
    /// Everything downstream therefore inherits its inventory: a descriptor added to the analyzer
    /// but not to .editorconfig is missing from the generated rulesets *and* from the committed-DLL
    /// check, so the new rule ships unconfigured and unverified with nothing going red.
    /// </summary>
    [TestFixture]
    public class RuleInventoryTests
    {
        private static readonly Regex SeverityLine =
            new Regex(@"^\s*dotnet_diagnostic\.(?<rule>ERS\d{4})\.severity\s*=", RegexOptions.Multiline);

        /// <summary>Reads one [AssemblyMetadata] value injected by the test csproj.</summary>
        private static string Metadata(string key) =>
            typeof(RuleInventoryTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(attribute => attribute.Key == key)
                .Value;

        private static string[] ConfiguredRules()
        {
            var path = Path.GetFullPath(Metadata("EditorConfigPath"));
            Assert.That(File.Exists(path), Is.True, $"No .editorconfig at {path}");

            return SeverityLine.Matches(File.ReadAllText(path))
                .Cast<Match>()
                .Select(match => match.Groups["rule"].Value)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }

        private static string[] DeclaredRules() =>
            new SingletonAnalyzer().SupportedDiagnostics
                .Select(descriptor => descriptor.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

        [Test]
        public void EveryDeclaredRule_HasASeverityInEditorConfig()
        {
            var missing = DeclaredRules().Except(ConfiguredRules()).ToArray();

            Assert.That(missing, Is.Empty,
                "These rules are declared by the analyzer but absent from .editorconfig, so they "
                + "are missing from the generated rulesets and from the staleness probe: "
                + string.Join(", ", missing));
        }

        [Test]
        public void EveryConfiguredRule_IsDeclaredByTheAnalyzer()
        {
            var unknown = ConfiguredRules().Except(DeclaredRules()).ToArray();

            Assert.That(unknown, Is.Empty,
                ".editorconfig configures rules the analyzer does not declare, which generates "
                + "ruleset entries and probe expectations nothing can satisfy: "
                + string.Join(", ", unknown));
        }
    }
}
